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
            DisplayName = "Excel Assertions",
            Description = "Validates Excel results against explicit data criteria. Supports scalar, rowCount, nonBlank, groupMatch, keySet, and sorted assertions;"
                + " keySet asserts that both sets are equal; use excel_compare_key_sets when you only need bidirectional difference counts and bounded samples;"
                + " for a union basis, scalar sources can be calculated per basis.sources and aggregated; only additive metrics such as sum, count, counta, rowCount, nonBlank, and errorCount are supported;"
                + " target must match the result range being validated; a single-sheet target cannot represent an aggregate across multiple union sources;"
                + " non-additive metrics such as average, countDistinct, min, and max must be calculated after the union;"
                + " for single-source scalar/groupMatch assertions, target/source can exclude worksheet row numbers or subtotal/total label rows;"
                + " source.column and target.column/columns must use the actual headers from their respective ranges; basis.measures defines source-side metrics and does not automatically map target columns;"
                + " when target headers differ from source metrics, specify target.column explicitly (for example, compare source Amount with output Value); target ranges with headers must include the header row and use the correct basis.headerMode;"
                + " returns coverage, backend independence, and difference reports without modifying the workbook.",
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
                + "\"description\":\"Header mode for source/target ranges. Specify it based on the structure of both ranges when comparing result tables with different headers.\"},"
                + "\"filters\":{\"type\":\"array\",\"items\":{\"$ref\":\"#/definitions/filter\"}},"
                + "\"groupBy\":{\"type\":\"array\",\"items\":{\"type\":\"string\"}},"
                + "\"derivedGroupBy\":{\"type\":\"array\",\"items\":{\"type\":\"object\",\"properties\":{"
                + "\"column\":{\"type\":\"string\"},"
                + "\"transform\":{\"type\":\"string\",\"enum\":[\"firstCharacter\",\"prefix\",\"year\",\"quarter\",\"month\",\"monthName\",\"yearMonth\",\"day\",\"dayOfWeek\",\"numberBucket\"]},"
                + "\"alias\":{\"type\":\"string\"},\"size\":{\"type\":\"number\"}},"
                + "\"required\":[\"column\",\"transform\"],\"additionalProperties\":false}},"
                + "\"measures\":{\"type\":\"array\",\"description\":\"Metrics calculated on the source side; these are not column names in the target range.\","
                + "\"items\":{\"type\":\"object\",\"properties\":{"
                + "\"column\":{\"type\":\"string\",\"description\":\"Actual header name of a column in the source range.\"},"
                + "\"function\":{\"type\":\"string\"}},"
                + "\"required\":[\"column\",\"function\"],\"additionalProperties\":false}},"
                + "\"note\":{\"type\":\"string\"}},"
                + "\"oneOf\":[{\"required\":[\"source\"]},{\"required\":[\"sources\"]}],"
                + "\"additionalProperties\":false},"
                + "\"assertions\":{\"type\":\"array\",\"minItems\":1,\"items\":{\"type\":\"object\",\"properties\":{"
                + "\"id\":{\"type\":\"string\"},"
                + "\"kind\":{\"type\":\"string\",\"enum\":[\"scalar\",\"rowCount\",\"nonBlank\",\"groupMatch\",\"keySet\",\"sorted\"]},"
                + "\"target\":{\"$ref\":\"#/definitions/side\","
                + "\"description\":\"Query for the target side. Column names must match headers in the target range; basis.measures is not inherited automatically.\"},"
                + "\"source\":{\"$ref\":\"#/definitions/side\","
                + "\"description\":\"Query for the source side. Column names must match headers in the source range.\"},"
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
                + "\"column\":{\"type\":\"string\",\"description\":\"Actual header name in this side's range; specify target and source separately.\"},"
                + "\"columns\":{\"type\":\"array\",\"description\":\"Actual header names in this side's range; specify target and source separately.\","
                + "\"items\":{\"type\":\"string\"}},"
                + "\"function\":{\"type\":\"string\"},\"keyColumn\":{\"type\":\"string\"},"
                + "\"valueColumn\":{\"type\":\"string\"},"
                + "\"filters\":{\"type\":\"array\",\"items\":{\"$ref\":\"#/definitions/filter\"}},"
                + "\"excludeRows\":{\"type\":\"array\",\"maxItems\":10000,"
                + "\"items\":{\"type\":\"integer\",\"minimum\":1},"
                + "\"description\":\"Absolute worksheet row numbers for this side's data range, excluding the header.\"},"
                + "\"excludeLabelRows\":{\"type\":\"boolean\","
                + "\"description\":\"Exclude rows on this side where any cell matches a subtotal or total label.\"}},"
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
