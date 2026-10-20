using System;
using System.Globalization;
using System.Text;
using System.Threading.Tasks;
using OfficeCopilot.Agent.Abstractions;
using OfficeCopilot.Host.Abstractions;
using OfficeCopilot.HostBridge.Contracts;

namespace OfficeCopilot.Host.Excel.Tools
{
    public sealed class ExcelAssertTool : ExcelToolBase
    {
        private static readonly ToolDefinition _definition = new ToolDefinition
        {
            Name = "excel_assert",
            DisplayName = "Excel 断言验证",
            Description = "按明确的数据口径验证 Excel 结果。支持 scalar、rowCount、nonBlank、groupMatch、keySet 和 sorted；"
                + "keySet 用于断言两侧集合相等；只需双向差异数量和有限样例时请用 excel_compare_key_sets；"
                + "union basis 的 scalar source 可按 basis.sources 逐源计算并汇总，仅支持 sum、count、counta、rowCount、nonBlank、errorCount 等可加口径；"
                + "target 必须与待验证结果范围一致；单工作表 target 不能代表多源 union 的汇总结果；"
                + "average、countDistinct、min/max 等不可加指标必须先在 union 后计算；"
                + "单源 scalar/groupMatch 的 target/source 可分别排除工作表行号或小计/合计标签行；"
                + "source.column 与 target.column/columns 必须分别使用各自区域的实际表头；basis.measures 定义源侧指标，不会自动映射目标列；"
                + "目标表头与源指标不同时必须显式指定 target.column，例如源 Amount 对比输出 Value；带表头的目标区域应包含表头行并设置正确的 basis.headerMode；"
                + "返回完整覆盖、后端独立性与差异报告，不修改工作簿。",
            SourceKind = ToolSourceKind.BuiltIn,
            SourceName = "Excel",
            IsReadOnly = true,
            RequiresConfirmation = false,
            InputSchemaJson = "{"
                + "\"type\":\"object\","
                + "\"properties\":{"
                + "\"basis\":{\"type\":\"object\",\"properties\":{"
                + "\"source\":{\"type\":\"object\",\"properties\":{"
                + "\"worksheetName\":{\"type\":\"string\"},\"address\":{\"type\":\"string\"},"
                + "\"tableName\":{\"type\":\"string\"}},\"additionalProperties\":false},"
                + "\"sources\":{\"type\":\"array\",\"items\":{\"type\":\"object\",\"properties\":{"
                + "\"sheet\":{\"type\":\"string\"},\"range\":{\"type\":\"string\"},"
                + "\"alias\":{\"type\":\"string\"}},\"required\":[\"sheet\",\"range\",\"alias\"],"
                + "\"additionalProperties\":false}},"
                + "\"topology\":{\"type\":\"string\",\"enum\":[\"single\",\"union\",\"lookup\",\"compare\"]},"
                + "\"keys\":{\"type\":\"array\",\"items\":{\"type\":\"string\"}},"
                + "\"join\":{\"type\":\"string\",\"enum\":[\"left\",\"inner\"]},"
                + "\"headerMode\":{\"type\":\"string\",\"enum\":[\"auto\",\"yes\",\"no\"],"
                + "\"description\":\"源/目标区域的表头模式；比较包含不同表头的结果表时应根据两个区域实际结构明确指定。\"},"
                + "\"filters\":{\"type\":\"array\",\"items\":{\"$ref\":\"#/definitions/filter\"}},"
                + "\"groupBy\":{\"type\":\"array\",\"items\":{\"type\":\"string\"}},"
                + "\"derivedGroupBy\":{\"type\":\"array\",\"items\":{\"type\":\"object\",\"properties\":{"
                + "\"column\":{\"type\":\"string\"},"
                + "\"transform\":{\"type\":\"string\",\"enum\":[\"firstCharacter\",\"prefix\",\"year\",\"quarter\",\"month\",\"monthName\",\"yearMonth\",\"day\",\"dayOfWeek\",\"numberBucket\"]},"
                + "\"alias\":{\"type\":\"string\"},\"size\":{\"type\":\"number\"}},"
                + "\"required\":[\"column\",\"transform\"],\"additionalProperties\":false}},"
                + "\"measures\":{\"type\":\"array\",\"description\":\"源侧计算指标；不代表 target 区域的列名。\","
                + "\"items\":{\"type\":\"object\",\"properties\":{"
                + "\"column\":{\"type\":\"string\",\"description\":\"源区域中的实际表头列名。\"},"
                + "\"function\":{\"type\":\"string\"}},"
                + "\"required\":[\"column\",\"function\"],\"additionalProperties\":false}},"
                + "\"note\":{\"type\":\"string\"}},"
                + "\"oneOf\":[{\"required\":[\"source\"]},{\"required\":[\"sources\"]}],"
                + "\"additionalProperties\":false},"
                + "\"assertions\":{\"type\":\"array\",\"minItems\":1,\"items\":{\"type\":\"object\",\"properties\":{"
                + "\"id\":{\"type\":\"string\"},"
                + "\"kind\":{\"type\":\"string\",\"enum\":[\"scalar\",\"rowCount\",\"nonBlank\",\"groupMatch\",\"keySet\",\"sorted\"]},"
                + "\"target\":{\"$ref\":\"#/definitions/side\","
                + "\"description\":\"目标侧查询；列名必须匹配目标区域自身的表头，不会自动沿用 basis.measures。\"},"
                + "\"source\":{\"$ref\":\"#/definitions/side\","
                + "\"description\":\"源侧查询；列名必须匹配源区域自身的表头。\"},"
                + "\"expectedValue\":{\"type\":\"number\"},\"tolerance\":{\"type\":\"number\"},"
                + "\"direction\":{\"type\":\"string\",\"enum\":[\"ascending\",\"descending\"]}},"
                + "\"required\":[\"kind\",\"target\"],\"additionalProperties\":false}}},"
                + "\"required\":[\"basis\",\"assertions\"],\"additionalProperties\":false,"
                + "\"definitions\":{"
                + "\"filter\":{\"type\":\"object\",\"properties\":{\"column\":{\"type\":\"string\"},"
                + "\"operator\":{\"type\":\"string\"},\"value\":{\"type\":\"string\"}},"
                + "\"required\":[\"column\",\"operator\"],\"additionalProperties\":false},"
                + "\"side\":{\"type\":\"object\",\"properties\":{"
                + "\"worksheetName\":{\"type\":\"string\"},\"address\":{\"type\":\"string\"},"
                + "\"tableName\":{\"type\":\"string\"},"
                + "\"column\":{\"type\":\"string\",\"description\":\"此侧区域中的实际表头列名；target 与 source 分别填写。\"},"
                + "\"columns\":{\"type\":\"array\",\"description\":\"此侧区域中的实际表头列名；target 与 source 分别填写。\","
                + "\"items\":{\"type\":\"string\"}},"
                + "\"function\":{\"type\":\"string\"},\"keyColumn\":{\"type\":\"string\"},"
                + "\"valueColumn\":{\"type\":\"string\"},"
                + "\"filters\":{\"type\":\"array\",\"items\":{\"$ref\":\"#/definitions/filter\"}},"
                + "\"excludeRows\":{\"type\":\"array\",\"maxItems\":10000,"
                + "\"items\":{\"type\":\"integer\",\"minimum\":1},"
                + "\"description\":\"此侧数据区域的工作表绝对行号，不含表头。\"},"
                + "\"excludeLabelRows\":{\"type\":\"boolean\","
                + "\"description\":\"排除此侧任意单元格匹配小计/合计标签的行。\"}},"
                + "\"additionalProperties\":false}}}"
        };

        public override ToolDefinition Definition
        {
            get { return _definition; }
        }

        public override Task<ToolExecutionResult> ExecuteAsync(
            ToolExecutionContext context,
            ToolExecutionRequest request)
        {
            ExcelVerificationPlan arguments = null;
            try
            {
                var host = GetExcelToolHost(context);
                arguments = ExcelToolJsonSerializer.Deserialize<ExcelVerificationPlan>(
                    request == null ? null : request.ArgumentsJson);
                var report = host.VerifyAssertions(arguments);
                var verification = ExcelVerificationResult.FromReport(
                    report,
                    ToolResultVerificationFields.ProducerModel,
                    false,
                    arguments.Basis);
                var content = BuildContent(report, verification);
                var structuredContentJson = ExcelToolJsonSerializer.Serialize(
                    new ExcelVerificationToolResult { Verification = verification });

                return Task.FromResult(report.Passed
                    ? CreateSuccess(request, content, structuredContentJson)
                    : CreateFailure(
                        request,
                        "一个或多个 Excel 断言未通过。",
                        "excel.assertion_failed",
                        content,
                        structuredContentJson));
            }
            catch (Exception exception)
            {
                var report = new ExcelVerificationReport
                {
                    Passed = false,
                    Tier = "C"
                };
                if (arguments != null && arguments.Assertions != null)
                {
                    foreach (var assertion in arguments.Assertions)
                    {
                        if (assertion == null)
                        {
                            continue;
                        }

                        report.Items.Add(new ExcelAssertionOutcome
                        {
                            Id = assertion.Id ?? string.Empty,
                            Kind = assertion.Kind ?? string.Empty,
                            Passed = false,
                            Tier = "C",
                            Independent = false,
                            TargetBackend = "unavailable"
                        });
                    }
                }

                report.Notes.Add("断言框架无法完成验证：" + exception.Message);
                var verification = ExcelVerificationResult.FromReport(
                    report,
                    ToolResultVerificationFields.ProducerModel,
                    false,
                    arguments == null ? null : arguments.Basis);
                var content = BuildContent(report, verification);
                var structuredContentJson = ExcelToolJsonSerializer.Serialize(
                    new ExcelVerificationToolResult { Verification = verification });
                return Task.FromResult(
                    CreateFailure(
                        request,
                        exception,
                        "excel.assertion_failed",
                        content,
                        structuredContentJson));
            }
        }

        private static string BuildContent(
            ExcelVerificationReport report,
            ExcelVerificationResult verification)
        {
            var builder = new StringBuilder();
            builder.AppendLine("结论：" + (report.Passed ? "通过" : "未通过"));
            builder.AppendLine("可信度等级：" + (report.Tier ?? "C"));
            builder.AppendLine("断言：" + report.Items.Count + "，差异：" + report.DiffCount);
            builder.AppendLine(
                "Producer：" + verification.Producer
                + "；覆盖行数：" + verification.CoveredRows
                + "；独立复核：" + (verification.Independent ? "是" : "否")
                + "；抽样：" + (verification.Sampled ? "是" : "否"));

            foreach (var item in report.Items)
            {
                builder.Append("- ")
                    .Append(string.IsNullOrWhiteSpace(item.Id) ? item.Kind : item.Id)
                    .Append("（").Append(item.Kind).Append("）：")
                    .Append(item.Passed ? "通过" : "未通过")
                    .Append("；等级 ").Append(item.Tier)
                    .Append("；独立复核 ").Append(item.Independent ? "是" : "否")
                    .Append("；后端 ").Append(item.TargetBackend);
                if (!string.IsNullOrWhiteSpace(item.SourceBackend))
                {
                    builder.Append(" / ").Append(item.SourceBackend);
                }

                AppendNumber(builder, "期望值", item.ExpectedValue);
                AppendNumber(builder, "实际值", item.ActualValue);
                AppendNumber(builder, "差值", item.Difference);
                AppendNumber(builder, "容差", item.Tolerance);
                AppendCount(builder, "期望数量", item.ExpectedCount);
                AppendCount(builder, "实际数量", item.ActualCount);
                AppendCount(builder, "比较数量", item.ComparedCount);
                if (item.IsSorted.HasValue)
                {
                    builder.Append("；排序 ").Append(item.IsSorted.Value ? "通过" : "未通过");
                }

                builder.AppendLine();
                AppendCoverage(builder, "target", item.TargetCoverage);
                AppendCoverage(builder, "source", item.SourceCoverage);
            }

            if (report.Diffs.Count > 0)
            {
                builder.AppendLine("差异明细：");
                foreach (var diff in report.Diffs)
                {
                    builder.AppendLine("- " + diff);
                }
            }

            if (report.Notes.Count > 0)
            {
                builder.AppendLine("说明：");
                foreach (var note in report.Notes)
                {
                    builder.AppendLine("- " + note);
                }
            }

            return builder.ToString();
        }

        private static void AppendNumber(
            StringBuilder builder,
            string label,
            double? value)
        {
            if (value.HasValue)
            {
                builder.Append("；").Append(label).Append(' ')
                    .Append(value.Value.ToString("G", CultureInfo.InvariantCulture));
            }
        }

        private static void AppendCount(
            StringBuilder builder,
            string label,
            int? value)
        {
            if (value.HasValue)
            {
                builder.Append("；").Append(label).Append(' ').Append(value.Value);
            }
        }

        private static void AppendCoverage(
            StringBuilder builder,
            string label,
            ExcelCoverageReport coverage)
        {
            if (coverage != null)
            {
                builder.AppendLine(ExcelAggregationService.RenderCoverage(label, coverage));
            }
        }
    }
}
