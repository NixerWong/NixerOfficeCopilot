using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using OfficeCopilot.Agent.Abstractions;
using OfficeCopilot.Host.Abstractions;
using OfficeCopilot.HostBridge.Contracts;

namespace OfficeCopilot.Host.Excel.Tools
{
    public sealed class ExcelCompareKeySetsTool : ExcelToolBase
    {
        private const int MaximumSampleCount = 10;

        private static readonly ToolDefinition _definition = new ToolDefinition
        {
            Name = "excel_compare_key_sets",
            DisplayName = "Excel 双向键集合差异",
            Description = "只读地比较两个 Excel 数据源的键集合，分别返回 target 中缺失于 source、"
                + "以及 source 中缺失于 target 的数量和有界确定性样例。差异是报告内容，不代表比较失败；"
                + "同时返回匹配键数和双向未匹配总数，均由本次比较直接计算。"
                + "只在两侧覆盖完整时报告成功。键按文本精确匹配（区分大小写，不自动修剪空格）；"
                + "空单元格和空字符串不参与，仅含空格的字符串仍是实际键。"
                + "每侧样例数默认 3、最多 10，不枚举全量键。",
            SourceKind = ToolSourceKind.BuiltIn,
            SourceName = "Excel",
            IsReadOnly = true,
            RequiresConfirmation = false,
            InputSchemaJson = "{"
                + "\"type\":\"object\","
                + "\"properties\":{"
                + "\"target\":{\"$ref\":\"#/definitions/side\"},"
                + "\"source\":{\"$ref\":\"#/definitions/side\"},"
                + "\"headerMode\":{\"type\":\"string\",\"enum\":[\"auto\",\"yes\",\"no\"],\"default\":\"auto\"},"
                + "\"sampleCount\":{\"type\":\"integer\",\"minimum\":1,\"maximum\":10,\"default\":3,"
                + "\"description\":\"每侧最多返回的确定性样例数；3 时取排序后的首、中、末键。\"}"
                + "},"
                + "\"required\":[\"target\",\"source\"],"
                + "\"additionalProperties\":false,"
                + "\"definitions\":{"
                + "\"filter\":{\"type\":\"object\",\"properties\":{\"column\":{\"type\":\"string\"},"
                + "\"operator\":{\"type\":\"string\"},\"value\":{\"type\":\"string\"}},"
                + "\"required\":[\"column\",\"operator\"],\"additionalProperties\":false},"
                + "\"side\":{\"type\":\"object\",\"properties\":{"
                + "\"worksheetName\":{\"type\":\"string\"},\"address\":{\"type\":\"string\"},"
                + "\"tableName\":{\"type\":\"string\"},"
                + "\"keyColumn\":{\"type\":\"string\",\"description\":\"用于构造非空键集合的列名。\"},"
                + "\"filters\":{\"type\":\"array\",\"items\":{\"$ref\":\"#/definitions/filter\"}}},"
                + "\"required\":[\"keyColumn\"],\"additionalProperties\":false,"
                + "\"description\":\"每侧指定 worksheetName、address 或 tableName 之一及 keyColumn。\"}"
                + "}}"
        };

        public override ToolDefinition Definition
        {
            get { return _definition; }
        }

        public override Task<ToolExecutionResult> ExecuteAsync(
            ToolExecutionContext context,
            ToolExecutionRequest request)
        {
            try
            {
                var arguments = ExcelToolJsonSerializer.Deserialize<ExcelKeySetComparisonRequest>(
                    request == null ? null : request.ArgumentsJson);
                ValidateRequest(arguments);

                var basis = new ExcelVerificationBasis
                {
                    Source = ToSource(arguments.Target),
                    HeaderMode = NormalizeHeaderMode(arguments.HeaderMode),
                    Note = "只读双向键集合差异；排除空单元格和空字符串，键值按文本精确匹配。"
                };
                var verificationPlan = new ExcelVerificationPlan
                {
                    Basis = basis,
                    Assertions = new List<ExcelAssertion>
                    {
                        new ExcelAssertion
                        {
                            Id = "key-set-difference",
                            Kind = "keySetDifference",
                            Target = arguments.Target,
                            Source = arguments.Source,
                            SampleCount = arguments.SampleCount
                        }
                    }
                };
                var report = GetExcelToolHost(context).VerifyAssertions(verificationPlan);
                if (report == null || report.Items == null || report.Items.Count != 1)
                {
                    throw new InvalidOperationException(
                        "键集合差异验证未返回唯一的断言结果。");
                }

                var outcome = report.Items[0];
                if (report.Passed
                    && (!outcome.TargetOnlyCount.HasValue
                        || !outcome.SourceOnlyCount.HasValue
                        || outcome.TargetOnlySamples == null
                        || outcome.SourceOnlySamples == null))
                {
                    throw new InvalidOperationException(
                        "键集合差异验证结果缺少双向计数或样例。");
                }

                var verification = ExcelVerificationResult.FromReport(
                    report,
                    ToolResultVerificationFields.ProducerModel,
                    false,
                    basis);
                verification.AssertionCount = 0;
                verification.Operation = "keySetDifference";
                var result = new ExcelKeySetComparisonResult
                {
                    TargetOnlyCount = outcome.TargetOnlyCount,
                    SourceOnlyCount = outcome.SourceOnlyCount,
                    MatchedCount = outcome.ActualCount.HasValue
                        && outcome.TargetOnlyCount.HasValue
                            ? outcome.ActualCount.Value - outcome.TargetOnlyCount.Value
                            : (int?)null,
                    TotalUnmatchedCount = outcome.TargetOnlyCount.HasValue
                        && outcome.SourceOnlyCount.HasValue
                            ? outcome.TargetOnlyCount.Value + outcome.SourceOnlyCount.Value
                            : (int?)null,
                    TargetOnlySamples = outcome.TargetOnlySamples == null
                        ? null
                        : new List<string>(outcome.TargetOnlySamples),
                    SourceOnlySamples = outcome.SourceOnlySamples == null
                        ? null
                        : new List<string>(outcome.SourceOnlySamples),
                    Verification = verification
                };
                var content = BuildContent(report, outcome, verification);
                var structuredContentJson = ExcelToolJsonSerializer.Serialize(result);
                return Task.FromResult(report.Passed
                    ? CreateSuccess(request, content, structuredContentJson)
                    : CreateFailure(
                        request,
                        "键集合差异比较未能完成；请检查覆盖和验证说明。",
                        "excel.key_set_comparison_incomplete",
                        content,
                        structuredContentJson));
            }
            catch (Exception exception)
            {
                return Task.FromResult(
                    CreateFailure(request, exception, "excel.key_set_comparison_failed"));
            }
        }

        private static void ValidateRequest(ExcelKeySetComparisonRequest request)
        {
            if (request == null || request.Target == null || request.Source == null)
            {
                throw new InvalidOperationException("必须同时提供 target 和 source 数据源。");
            }

            ValidateSide(request.Target, "target");
            ValidateSide(request.Source, "source");
            if (request.SampleCount < 1 || request.SampleCount > MaximumSampleCount)
            {
                throw new InvalidOperationException(
                    "sampleCount 必须在 1 到 " + MaximumSampleCount + " 之间。");
            }

            NormalizeHeaderMode(request.HeaderMode);
        }

        private static void ValidateSide(ExcelAssertionSide side, string name)
        {
            if (string.IsNullOrWhiteSpace(side.KeyColumn))
            {
                throw new InvalidOperationException(name + ".keyColumn 不能为空。");
            }

            if (string.IsNullOrWhiteSpace(side.WorksheetName)
                && string.IsNullOrWhiteSpace(side.Address)
                && string.IsNullOrWhiteSpace(side.TableName))
            {
                throw new InvalidOperationException(
                    name + " 必须指定 worksheetName、address 或 tableName。");
            }
        }

        private static ExcelSourceRef ToSource(ExcelAssertionSide side)
        {
            return new ExcelSourceRef
            {
                WorksheetName = side.WorksheetName ?? string.Empty,
                Address = side.Address ?? string.Empty,
                TableName = side.TableName ?? string.Empty
            };
        }

        private static string NormalizeHeaderMode(string headerMode)
        {
            var mode = string.IsNullOrWhiteSpace(headerMode)
                ? "auto"
                : headerMode.Trim().ToLowerInvariant();
            if (mode != "auto" && mode != "yes" && mode != "no")
            {
                throw new InvalidOperationException(
                    "headerMode 必须为 auto、yes 或 no。");
            }

            return mode;
        }

        private static string BuildContent(
            ExcelVerificationReport report,
            ExcelAssertionOutcome outcome,
            ExcelVerificationResult verification)
        {
            var builder = new StringBuilder();
            builder.AppendLine(report.Passed ? "比较已完成" : "比较未能完成");
            builder.AppendLine("可信度等级：" + verification.Tier
                + "；完整覆盖：" + (report.Passed ? "是" : "否")
                + "；独立复核：" + (verification.Independent ? "是" : "否"));
            if (outcome.TargetCoverage != null)
            {
                builder.AppendLine(ExcelAggregationService.RenderCoverage(
                    "target",
                    outcome.TargetCoverage));
            }
            if (outcome.SourceCoverage != null)
            {
                builder.AppendLine(ExcelAggregationService.RenderCoverage(
                    "source",
                    outcome.SourceCoverage));
            }

            if (outcome.TargetOnlyCount.HasValue)
            {
                builder.AppendLine("target-only（target 中有、source 中无）数量："
                    + outcome.TargetOnlyCount.Value);
                builder.AppendLine("target-only 样例：" + FormatSamples(outcome.TargetOnlySamples));
            }
            if (outcome.SourceOnlyCount.HasValue)
            {
                builder.AppendLine("source-only（source 中有、target 中无）数量："
                    + outcome.SourceOnlyCount.Value);
                builder.AppendLine("source-only 样例：" + FormatSamples(outcome.SourceOnlySamples));
            }
            if (outcome.ActualCount.HasValue && outcome.TargetOnlyCount.HasValue)
            {
                builder.AppendLine("matched（两侧共有）数量："
                    + (outcome.ActualCount.Value - outcome.TargetOnlyCount.Value));
            }
            if (outcome.TargetOnlyCount.HasValue && outcome.SourceOnlyCount.HasValue)
            {
                builder.AppendLine("total-unmatched（双向未匹配）数量："
                    + (outcome.TargetOnlyCount.Value + outcome.SourceOnlyCount.Value));
            }

            if (report.Notes != null)
            {
                foreach (var note in report.Notes)
                {
                    builder.AppendLine("说明：" + note);
                }
            }

            return builder.ToString();
        }

        private static string FormatSamples(IList<string> samples)
        {
            return samples == null || samples.Count == 0
                ? "无"
                : string.Join("、", samples);
        }
    }
}
