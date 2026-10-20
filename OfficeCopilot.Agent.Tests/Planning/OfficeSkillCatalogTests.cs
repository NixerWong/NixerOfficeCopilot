using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OfficeCopilot.Agent.Planning;

namespace OfficeCopilot.Agent.Tests.Planning
{
    /// <summary>Excel tool lists express strategy; the runtime execution contract enforces write limits.</summary>
    [TestClass]
    public class OfficeSkillCatalogTests
    {
        private static SkillDefinition Get(string name)
            => OfficeSkillCatalog.CreateDefaultSkills()
                .FirstOrDefault(skill => skill.Name == name);

        [TestMethod]
        public void Catalog_ContainsFourExcelTiers()
        {
            var names = OfficeSkillCatalog.CreateDefaultSkills().Select(skill => skill.Name).ToList();

            CollectionAssert.IsSubsetOf(
                new[] { "read-analysis", "live-result", "structural-transform", "bulk-format" },
                names);
            Assert.IsFalse(names.Contains("transform"), "两档时代的 transform 已被四档取代");
        }

        [TestMethod]
        public void LiveResult_HasNoCodeExecutionAuthority()
        {
            // 用户要的是随源数据自动更新的结果；用 VBA 算成常量就永久答错了需求。
            var skill = Get("live-result");

            Assert.IsNotNull(skill);
            Assert.IsFalse(skill.AllowCodeExecution);
            Assert.IsFalse(skill.AllowedToolNames.Contains("excel_execute_vba"));
            Assert.IsTrue(skill.PreferredToolNames.Contains("excel_set_range_formulas"));
        }

        [TestMethod]
        public void StructuralTransform_PrefersVbaFirst()
        {
            // 分档依据是「是否触碰既有数据」，不是「是否用了 VBA」，所以这里 VBA 是首选而非回退。
            var skill = Get("structural-transform");

            Assert.IsNotNull(skill);
            Assert.IsTrue(skill.AllowCodeExecution);
            Assert.AreEqual("excel_execute_vba", skill.PreferredToolNames.First());
        }

        [TestMethod]
        public void BulkFormat_VerifiesByReadingStyleBack()
        {
            // 调用没抛异常不代表单元格看起来对（D8），验证必须回读实际格式。
            var skill = Get("bulk-format");

            Assert.IsNotNull(skill);
            Assert.IsTrue(skill.RequireVerification);
            Assert.IsTrue(skill.VerificationToolNames.Contains("excel_get_range_style"));
        }

        [TestMethod]
        public void ReadAnalysis_DoesNotFabricateAssertionsForUnmaterializedUnions()
        {
            var skill = Get("read-analysis");

            Assert.IsNotNull(skill);
            StringAssert.Contains(skill.Description, "do not call excel_assert");
            StringAssert.Contains(skill.Description, "Tier C");
        }

        [TestMethod]
        public void Planner_InsertsTargetedVerificationAfterEachExcelWriteStage()
        {
            var skills = OfficeSkillCatalog.CreateDefaultSkills();
            var availableTools = skills
                .SelectMany(skill => skill.AllowedToolNames)
                .Distinct(System.StringComparer.OrdinalIgnoreCase)
                .ToList();
            var request = new SkillPlannerRequest
            {
                UserInput = "写入联动公式并验证",
                HostKind = "Excel",
                RequestedSkillName = "live-result",
                AvailableToolNames = availableTools
            };
            var selection = new KeywordSkillPlanner()
                .SelectSkillAsync(
                    request,
                    new InMemorySkillRegistry(skills),
                    System.Threading.CancellationToken.None)
                .GetAwaiter()
                .GetResult();

            var steps = selection.ExecutionPlanSteps;
            var mutationIndex = steps.ToList().FindIndex(step => step.StepKind == "formula");
            Assert.IsTrue(mutationIndex >= 0);
            Assert.IsTrue(mutationIndex + 1 < steps.Count);

            var mutation = steps[mutationIndex];
            var verification = steps[mutationIndex + 1];
            Assert.AreEqual("verify", verification.StepKind);
            Assert.AreEqual(mutation.StepId, verification.VerifiesStepId);
            Assert.IsFalse(string.IsNullOrWhiteSpace(mutation.StepId));
            Assert.IsTrue(verification.VerificationToolNames.Contains("excel_assert"));
            Assert.IsTrue(verification.VerificationToolNames.Contains("excel_validate_range_formulas"));
            Assert.IsFalse(verification.VerificationToolNames.Contains("excel_read_range"));
            Assert.IsFalse(verification.VerificationToolNames.Contains("runtime.get_host_context"));
        }

        [TestMethod]
        public void StructuralTransformVerification_AllowsWipRegistrationAndCleanupReadback()
        {
            var skills = OfficeSkillCatalog.CreateDefaultSkills();
            var availableTools = skills
                .SelectMany(skill => skill.AllowedToolNames)
                .Distinct(System.StringComparer.OrdinalIgnoreCase)
                .ToList();
            var selection = new KeywordSkillPlanner()
                .SelectSkillAsync(
                    new SkillPlannerRequest
                    {
                        UserInput = "创建 _wip_01_restart_probe 并验证登记",
                        HostKind = "Excel",
                        RequestedSkillName = "structural-transform",
                        AvailableToolNames = availableTools
                    },
                    new InMemorySkillRegistry(skills),
                    System.Threading.CancellationToken.None)
                .GetAwaiter()
                .GetResult();

            var verification = selection.ExecutionPlanSteps
                .First(step => step.StepKind == "verify");

            Assert.IsTrue(verification.VerificationToolNames.Contains("excel_cleanup_workspace"));
            Assert.IsTrue(verification.VerificationToolNames.Contains("excel_list_worksheets"));
            StringAssert.Contains(verification.Instruction, "dryRun=true");
            StringAssert.Contains(verification.Instruction, "excel_list_worksheets");
        }

        [TestMethod]
        public void Planner_DoesNotSelectExcelSkillInPowerPoint()
        {
            // Excel 技能的白名单会隐藏全部 ppt_* 工具。
            var registry = new InMemorySkillRegistry(OfficeSkillCatalog.CreateDefaultSkills());
            var request = new SkillPlannerRequest
            {
                UserInput = "把字体颜色和格式统一一下 format style font color",
                HostKind = "PowerPoint",
                AvailableToolNames = new System.Collections.Generic.List<string> { "ppt_style_search", "ppt_deck_create" }
            };

            var result = new KeywordSkillPlanner()
                .SelectSkillAsync(request, registry, System.Threading.CancellationToken.None)
                .GetAwaiter().GetResult();

            Assert.AreNotEqual("bulk-format", result.SkillName);
            Assert.IsFalse(OfficeSkillCatalog.CreateDefaultSkills()
                .Where(skill => skill.Name == result.SkillName)
                .Any(skill => skill.Metadata.ContainsKey(SkillMetadataKeys.HostKind)
                    && skill.Metadata[SkillMetadataKeys.HostKind] == "excel"));
        }

        [DataTestMethod]
        [DataRow("帮我按照论文格式排版。")]
        [DataRow("把正文字体和样式统一一下")]
        [DataRow("调整一下标题格式 format style")]
        public void Planner_DoesNotSelectKeywordGatedSkillsWithoutKeyword(string input)
        {
            // skillify / record-to-memory 不限宿主，若靠意图/相似度被误选会把轮次上限压到 20/5。
            var registry = new InMemorySkillRegistry(OfficeSkillCatalog.CreateDefaultSkills());
            var request = new SkillPlannerRequest
            {
                UserInput = input,
                HostKind = "Word",
                AvailableToolNames = new System.Collections.Generic.List<string>
                {
                    "word_get_active_document", "word_read_paragraphs", "word_insert_text", "word_set_paragraph_format"
                }
            };

            var result = new KeywordSkillPlanner()
                .SelectSkillAsync(request, registry, System.Threading.CancellationToken.None)
                .GetAwaiter().GetResult();

            Assert.AreNotEqual("skillify", result.SkillName);
            Assert.AreNotEqual("record-to-memory", result.SkillName);
            Assert.AreEqual(0, result.MaxToolRoundCount);
        }

        [DataTestMethod]
        [DataRow("把刚才的流程保存成 skill", "skillify")]
        [DataRow("记住：论文正文用宋体小四", "record-to-memory")]
        public void Planner_SelectsKeywordGatedSkillsWhenKeywordMatches(string input, string expectedSkill)
        {
            var registry = new InMemorySkillRegistry(OfficeSkillCatalog.CreateDefaultSkills());
            var request = new SkillPlannerRequest
            {
                UserInput = input,
                HostKind = "Word",
                AvailableToolNames = new System.Collections.Generic.List<string>
                {
                    "word_get_active_document", "runtime.create_skill", "runtime.ask_user_question", "runtime.record_memory"
                }
            };

            var result = new KeywordSkillPlanner()
                .SelectSkillAsync(request, registry, System.Threading.CancellationToken.None)
                .GetAwaiter().GetResult();

            Assert.AreEqual(expectedSkill, result.SkillName);
        }

        [DataTestMethod]
        [DataRow("preset workbook", "read-analysis")]
        [DataRow("summary of the data", "read-analysis")]
        [DataRow("表述清楚吗", "read-analysis")]
        public void Planner_DoesNotTreatSubstringsAsEnglishIntentTokens(string input, string expectedSkill)
        {
            var skills = OfficeSkillCatalog.CreateDefaultSkills();
            var availableTools = skills
                .SelectMany(skill => skill.AllowedToolNames)
                .Distinct(System.StringComparer.OrdinalIgnoreCase)
                .ToList();
            var result = new KeywordSkillPlanner()
                .SelectSkillAsync(
                    new SkillPlannerRequest
                    {
                        UserInput = input,
                        HostKind = "Excel",
                        AvailableToolNames = availableTools
                    },
                    new InMemorySkillRegistry(skills),
                    System.Threading.CancellationToken.None)
                .GetAwaiter()
                .GetResult();

            Assert.AreEqual(expectedSkill, result.SkillName);
        }

        [TestMethod]
        public void Planner_FallsBackToReadAnalysisForUnmatchedExcelRequests()
        {
            var skills = OfficeSkillCatalog.CreateDefaultSkills();
            var availableTools = skills
                .SelectMany(skill => skill.AllowedToolNames)
                .Distinct(System.StringComparer.OrdinalIgnoreCase)
                .ToList();
            var result = new KeywordSkillPlanner()
                .SelectSkillAsync(
                    new SkillPlannerRequest
                    {
                        UserInput = "你好",
                        HostKind = "Excel",
                        AvailableToolNames = availableTools
                    },
                    new InMemorySkillRegistry(skills),
                    System.Threading.CancellationToken.None)
                .GetAwaiter()
                .GetResult();

            Assert.AreEqual("read-analysis", result.SkillName);
        }

        [TestMethod]
        public void Planner_ClassifiesStructuredExcelIntentsAcrossAtLeastFiftyCases()
        {
            var cases = new[]
            {
                new[] { "统计各部门销售额", "read-analysis" },
                new[] { "统计订单数量", "read-analysis" },
                new[] { "计算每月平均收入", "read-analysis" },
                new[] { "分析各产品的销量趋势", "read-analysis" },
                new[] { "比较两个工作表的客户 ID 差异", "read-analysis" },
                new[] { "检查 Revenue 列中的错误值", "read-analysis" },
                new[] { "汇总 Jan、Feb、Mar 的 Revenue", "read-analysis" },
                new[] { "把 Jan!A1:B4、Feb!A1:B4、Mar!A1:B4 作为三个明确来源，用 excel_aggregate 的 sources[] 按同一列结构纵向 union，headerMode 设为 yes，对合并后的全部 Revenue 明细行计算一次 average。只报告总明细的平均销售额；不要先求每月平均再对三个平均值做无权平均。", "read-analysis" },
                new[] { "这列的平均值是多少", "read-analysis" },
                new[] { "找出销售额最高的地区", "read-analysis" },
                new[] { "查看客户结构", "read-analysis" },
                new[] { "Analyze revenue by department", "read-analysis" },
                new[] { "Count rows in Orders", "read-analysis" },
                new[] { "Compare sales across months", "read-analysis" },
                new[] { "What is the average amount?", "read-analysis" },
                new[] { "Find duplicate customer IDs", "read-analysis" },
                new[] { "帮我看看 2025 年几个部门各卖了多少、销售额占比呢？", "read-analysis" },
                new[] { "Orders 和 Customers 对下 ID，双方各有多少没配上？", "read-analysis" },
                new[] { "Jan、Feb、Mar 一起算算，Revenue 总额多少？", "read-analysis" },
                new[] { "Orders 连上 Customers 后总共多少钱？还有几单没找到客户？", "read-analysis" },
                new[] { "把 Orders 左连接 Customers，保留全部订单。订单金额总共多少？另外有几单没有找到客户？", "read-analysis" },
                new[] { "2025 年销售数据覆盖几个部门？请说明部门名称。", "read-analysis" },
                new[] { "请直接按 OrderDate 筛选 2025-01-01 至 2026-01-01，计算 Revenue 总和。最终只报告总和；不要附加命中行数、占比、独立后端数值或参考公式。", "read-analysis" },

                new[] { "把 Orders 中未匹配的订单复制到新工作表", "structural-transform" },
                new[] { "把 Orders 中未匹配的订单复制到新工作表，并将表头加粗。", "structural-transform" },
                new[] { "将目标区域覆盖为新的订单数据", "structural-transform" },
                new[] { "删除重复的订单行", "structural-transform" },
                new[] { "拆分 FullName 列为姓和名", "structural-transform" },
                new[] { "把 Jan、Feb 两张表合并成明细表", "structural-transform" },
                new[] { "提取错误行到单独的表", "structural-transform" },
                new[] { "将 Revenue 列中的文本数字转换为数值", "structural-transform" },
                new[] { "清除空白行并保留原来的记录顺序", "structural-transform" },
                new[] { "将满足条件的订单移动到新表", "structural-transform" },
                new[] { "重命名工作表并调整顺序", "structural-transform" },
                new[] { "把宽表转成长表", "structural-transform" },
                new[] { "Copy unmatched orders into a new worksheet", "structural-transform" },
                new[] { "Remove duplicate customer rows", "structural-transform" },
                new[] { "Split the FullName column into first and last names", "structural-transform" },
                new[] { "Merge Jan and Feb data into one detail sheet", "structural-transform" },
                new[] { "Extract error rows into a separate sheet", "structural-transform" },

                new[] { "把 Revenue 列设为两位小数", "bulk-format" },
                new[] { "将表头加粗", "bulk-format" },
                new[] { "把字体颜色改成蓝色", "bulk-format" },
                new[] { "给所有金额设置人民币格式", "bulk-format" },
                new[] { "调整列宽以完整显示内容", "bulk-format" },
                new[] { "为标题添加边框", "bulk-format" },
                new[] { "设置日期显示格式为 yyyy/MM/dd", "bulk-format" },
                new[] { "把选区背景填充为黄色", "bulk-format" },
                new[] { "统一整张表的字体", "bulk-format" },
                new[] { "应用条件格式突出显示负数", "bulk-format" },
                new[] { "Format the Revenue column as currency", "bulk-format" },
                new[] { "Bold the header row", "bulk-format" },
                new[] { "Change font color to blue", "bulk-format" },
                new[] { "Set date display format to yyyy-mm-dd", "bulk-format" },
                new[] { "Adjust column width to fit content", "bulk-format" },

                new[] { "在 C2:C20 写入求和公式", "live-result" },
                new[] { "在 C2:C20 写入求和公式，并把结果显示为两位小数。", "live-result" },
                new[] { "添加能随明细更新的合计公式", "live-result" },
                new[] { "用 XLOOKUP 公式返回客户名称", "live-result" },
                new[] { "为每行创建 Revenue = Quantity * Price 的公式", "live-result" },
                new[] { "新增一列，写入 IF 公式标记状态", "live-result" },
                new[] { "让汇总单元格引用明细表并自动更新", "live-result" },
                new[] { "创建动态销售额公式", "live-result" },
                new[] { "用公式计算税额，不要写固定值", "live-result" },
                new[] { "插入 VLOOKUP 函数", "live-result" },
                new[] { "构建联动的月度汇总公式", "live-result" },
                new[] { "Add a SUM formula to cell C20", "live-result" },
                new[] { "Insert an XLOOKUP formula for customer names", "live-result" },
                new[] { "Create formulas that update when rows are added", "live-result" },
                new[] { "Use an IF formula to flag overdue records", "live-result" },
                new[] { "Link the summary total to the detail data", "live-result" }
            };

            Assert.IsTrue(cases.Length >= 50, "The routing corpus must contain at least 50 cases.");
            var skills = OfficeSkillCatalog.CreateDefaultSkills();
            var availableTools = skills
                .SelectMany(skill => skill.AllowedToolNames)
                .Distinct(System.StringComparer.OrdinalIgnoreCase)
                .ToList();
            var registry = new InMemorySkillRegistry(skills);
            var planner = new KeywordSkillPlanner();
            var failures = new System.Collections.Generic.List<string>();

            foreach (var testCase in cases)
            {
                var result = planner
                    .SelectSkillAsync(
                        new SkillPlannerRequest
                        {
                            UserInput = testCase[0],
                            HostKind = "Excel",
                            AvailableToolNames = availableTools
                        },
                        registry,
                        System.Threading.CancellationToken.None)
                    .GetAwaiter()
                    .GetResult();

                if (!string.Equals(result.SkillName, testCase[1], System.StringComparison.Ordinal))
                {
                    failures.Add("'" + testCase[0] + "' expected " + testCase[1] + " but got " + result.SkillName);
                }

                if (result.Reason.IndexOf("Classified ", System.StringComparison.Ordinal) < 0)
                {
                    failures.Add("'" + testCase[0] + "' did not use structured intent classification.");
                }
            }

            var accuracy = (double)(cases.Length - failures.Count) / cases.Length;
            Assert.IsTrue(
                accuracy >= 0.95d,
                "Structured Excel routing accuracy was "
                    + (accuracy * 100d).ToString("F1")
                    + "% (" + (cases.Length - failures.Count) + "/" + cases.Length + "). "
                    + string.Join("; ", failures));
            Assert.AreEqual(
                cases.Length,
                cases.Length - failures.Count,
                "All curated routing cases should match their expected skill: " + string.Join("; ", failures));
        }

        [TestMethod]
        public void Planner_ExplicitSkillRequestTakesPrecedenceOverStructuredIntent()
        {
            var skills = OfficeSkillCatalog.CreateDefaultSkills();
            var availableTools = skills
                .SelectMany(skill => skill.AllowedToolNames)
                .Distinct(System.StringComparer.OrdinalIgnoreCase)
                .ToList();
            var result = new KeywordSkillPlanner()
                .SelectSkillAsync(
                    new SkillPlannerRequest
                    {
                        UserInput = "把 Revenue 列设为货币格式",
                        HostKind = "Excel",
                        RequestedSkillName = "read-analysis",
                        AvailableToolNames = availableTools
                    },
                    new InMemorySkillRegistry(skills),
                    System.Threading.CancellationToken.None)
                .GetAwaiter()
                .GetResult();

            Assert.AreEqual("read-analysis", result.SkillName);
            StringAssert.Contains(result.Reason, "explicitly requested");
        }

        [TestMethod]
        public void ReadAnalysis_UsesAnalysisToolsAsRecommendationsWithoutRecommendingVba()
        {
            var skill = Get("read-analysis");

            Assert.IsNotNull(skill);
            StringAssert.Contains(skill.Description, "measurements requested and supported by tool results");
            StringAssert.Contains(skill.Description, "Honor requested decimal precision exactly");
            StringAssert.Contains(skill.Description, "Reuse the exact source scope");
            StringAssert.Contains(skill.Description, "explicitly retain the tool's Tier C status");
            Assert.IsFalse(skill.AllowCodeExecution);
            Assert.IsTrue(skill.AllowedToolNames.Contains("excel_aggregate"));
            Assert.IsFalse(skill.AllowedToolNames.Contains("excel_execute_vba"));
            Assert.IsFalse(skill.AllowedToolNames.Contains("excel_write_range"));
        }

        [TestMethod]
        public void ReadAnalysis_VerificationRecognizesReadBackTools()
        {
            // 模型用回读/复算/截图做验证，验证步骤必须能识别这些工具；
            // 否则「已完成验证」仍被判为 Verification pending（假失败）。
            var skill = Get("read-analysis");

            Assert.IsNotNull(skill);
            Assert.IsTrue(skill.VerificationToolNames.Contains("excel_read_range"));
            Assert.IsTrue(skill.VerificationToolNames.Contains("excel_aggregate"));
            Assert.IsTrue(skill.VerificationToolNames.Contains("excel_get_range_info"));
            Assert.IsTrue(skill.VerificationToolNames.Contains("excel_get_range_style"));
            Assert.IsTrue(skill.VerificationToolNames.Contains("excel_capture_worksheet_screenshot"));
            // 平凡工具在检查阶段就会被调用，不能充当验证工具，否则会过早判定验证完成。
            Assert.IsFalse(skill.VerificationToolNames.Contains("excel_get_selection"));
        }

        [TestMethod]
        public void ReadAnalysis_OffersPreferredBidirectionalKeySetComparison()
        {
            var skill = Get("read-analysis");

            Assert.IsNotNull(skill);
            Assert.IsTrue(skill.AllowedToolNames.Contains("excel_compare_key_sets"));
            Assert.IsTrue(skill.PreferredToolNames.Contains("excel_compare_key_sets"));
            Assert.IsTrue(skill.VerificationToolNames.Contains("excel_compare_key_sets"));
        }

        [TestMethod]
        public void ExcelAssert_IsAllowedPreferredAndRecognizedForEveryExcelSkill()
        {
            foreach (var name in new[] { "read-analysis", "live-result", "structural-transform", "bulk-format" })
            {
                var skill = Get(name);
                Assert.IsNotNull(skill, name);
                Assert.IsTrue(skill.AllowedToolNames.Contains("excel_assert"), name);
                Assert.IsTrue(skill.PreferredToolNames.Contains("excel_assert"), name);
                Assert.IsTrue(skill.VerificationToolNames.Contains("excel_assert"), name);
            }
        }

        [TestMethod]
        public void ExcelSkills_AllowQuestionsAndDeterministicCalculations()
        {
            foreach (var name in new[] { "read-analysis", "live-result", "structural-transform", "bulk-format" })
            {
                var skill = Get(name);
                Assert.IsNotNull(skill, name);
                Assert.IsTrue(skill.AllowedToolNames.Contains("runtime.ask_user_question"), name);
                Assert.IsTrue(skill.AllowedToolNames.Contains("runtime.calculate"), name);
            }
        }

        [TestMethod]
        public void Catalog_ContainsPowerPointSkillsWithPptBaseTools()
        {
            foreach (var name in new[] { "ppt-generate-deck", "ppt-edit-slide", "ppt-beautify", "ppt-style-advisor" })
            {
                var skill = Get(name);
                Assert.IsNotNull(skill, name);
                Assert.AreEqual("powerpoint", skill.Metadata[SkillMetadataKeys.HostKind]);
                Assert.IsTrue(skill.AllowedToolNames.Contains("ppt_read_slide"), name);
                Assert.IsFalse(skill.AllowedToolNames.Any(tool => tool.StartsWith("excel_")), name);
            }

            var generate = Get("ppt-generate-deck");
            Assert.IsTrue(generate.PreferredToolNames.Contains("ppt_style_preview"));
            Assert.IsTrue(generate.PreferredToolNames.Contains("runtime.ask_user_question"));
            Assert.IsFalse(generate.AllowedToolNames.Contains("ppt_restore_backup"));
            StringAssert.Contains(generate.Description, "3 visually distinct cover candidates");
            Assert.IsTrue(generate.AllowedToolNames.Contains("ppt_deck_validate"));
            Assert.IsFalse(generate.PreferredToolNames.Contains("ppt_deck_validate"));
            Assert.IsFalse(generate.RequireVerification);
            Assert.AreEqual(0, generate.VerificationToolNames.Count);

            Assert.IsFalse(Get("ppt-style-advisor").AllowHighRiskTools);
            Assert.IsTrue(Get("ppt-beautify").AllowCodeExecution);
        }

        [TestMethod]
        public void HostMatcher_HandlesAliases()
        {
            Assert.IsTrue(SkillHostMatcher.Matches("powerpoint", "PowerPoint"));
            Assert.IsTrue(SkillHostMatcher.Matches("powerpoint", "ppt"));
            Assert.IsTrue(SkillHostMatcher.Matches("excel", "Microsoft Excel"));
            Assert.IsTrue(SkillHostMatcher.Matches("excel", ""));
            Assert.IsFalse(SkillHostMatcher.Matches("excel", "PowerPoint"));
            Assert.IsFalse(SkillHostMatcher.Matches("powerpoint", "Word"));
        }

        [TestMethod]
        public void Planner_SelectsPowerPointSkillInPowerPoint()
        {
            var registry = new InMemorySkillRegistry(OfficeSkillCatalog.CreateDefaultSkills());
            var request = new SkillPlannerRequest
            {
                UserInput = "帮我美化这一页，对齐并统一字体",
                HostKind = "ppt",
                AvailableToolNames = new System.Collections.Generic.List<string> { "ppt_read_slide", "ppt_format_shapes", "ppt_arrange_shapes" }
            };

            var result = new KeywordSkillPlanner()
                .SelectSkillAsync(request, registry, System.Threading.CancellationToken.None)
                .GetAwaiter().GetResult();

            Assert.AreEqual("ppt-beautify", result.SkillName);
        }
    }
}
