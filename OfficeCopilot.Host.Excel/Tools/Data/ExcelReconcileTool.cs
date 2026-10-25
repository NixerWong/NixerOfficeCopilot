using System;
using System.Text;
using System.Threading.Tasks;
using OfficeCopilot.Agent.Abstractions;
using OfficeCopilot.Host.Abstractions;

namespace OfficeCopilot.Host.Excel.Tools
{
    public sealed class ExcelReconcileTool : ExcelToolBase
    {
        private static readonly ToolDefinition _definition = new ToolDefinition
        {
            Name = "excel_reconcile",
            DisplayName = "Reconciliation Summary",
            Description = "Checks whether a summary table matches its detail data using total reconciliation, per-group counts, and sampled group comparisons;"
                + " returns a conclusion and discrepancies instead of a full listing. Use this tool to verify large-table results; do not compare rows one by one.",
            SourceKind = ToolSourceKind.BuiltIn,
            SourceName = "Excel",
            IsReadOnly = true,
            RequiresConfirmation = false,
            InputSchemaJson = "{"
                + "\"type\":\"object\","
                + "\"properties\":{"
                + "\"detailWorksheetName\":{\"type\":\"string\"},"
                + "\"detailAddress\":{\"type\":\"string\"},"
                + "\"detailTableName\":{\"type\":\"string\"},"
                + "\"detailGroupColumn\":{\"type\":\"string\",\"description\":\"Name of the grouping column in the detail data.\"},"
                + "\"detailValueColumn\":{\"type\":\"string\",\"description\":\"Name of the numeric column to total in the detail data.\"},"
                + "\"summaryWorksheetName\":{\"type\":\"string\"},"
                + "\"summaryAddress\":{\"type\":\"string\"},"
                + "\"summaryTableName\":{\"type\":\"string\"},"
                + "\"summaryGroupColumn\":{\"type\":\"string\",\"description\":\"Name of the grouping column in the summary table; leave empty to use detailGroupColumn.\"},"
                + "\"summaryValueColumn\":{\"type\":\"string\",\"description\":\"Name of the total column in the summary table; leave empty to use detailValueColumn.\"},"
                + "\"sampleCount\":{\"type\":\"integer\",\"minimum\":1,\"description\":\"Number of groups to compare by sampling (default: 5).\"},"
                + "\"tolerance\":{\"type\":\"number\",\"description\":\"Floating-point comparison tolerance (default: 0.01).\"}"
                + "},"
                + "\"required\":[\"detailGroupColumn\",\"detailValueColumn\"],"
                + "\"additionalProperties\":false"
                + "}"
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
                var host = GetExcelToolHost(context);
                var arguments = ExcelToolJsonSerializer.Deserialize<ExcelReconcileRequest>(
                    request == null ? null : request.ArgumentsJson);

                var result = host.Reconcile(arguments);
                var content = BuildContent(result);
                var structuredContentJson = ExcelToolJsonSerializer.Serialize(result);
                return Task.FromResult(CreateSuccess(request, content, structuredContentJson));
            }
            catch (Exception ex)
            {
                return Task.FromResult(CreateFailure(request, ex, "excel.reconcile_failed"));
            }
        }

        private static string BuildContent(ExcelReconcileResult result)
        {
            var builder = new StringBuilder();
            builder.AppendLine("结论：" + (result.Passed ? "通过" : "未通过"));
            builder.AppendLine(result.Conclusion ?? string.Empty);
            builder.AppendLine();
            builder.AppendLine("明细：" + (result.DetailAddress ?? "-") + "，" + result.DetailRowCount + " 行，"
                + result.DetailGroupCount + " 个分组，合计 " + result.DetailTotal);
            builder.AppendLine(ExcelAggregationService.RenderCoverage("明细", result.DetailCoverage));
            builder.AppendLine("汇总：" + (result.SummaryAddress ?? "-") + "，" + result.SummaryRowCount + " 行，合计 "
                + result.SummaryTotal);
            builder.AppendLine(ExcelAggregationService.RenderCoverage("汇总", result.SummaryCoverage));
            builder.AppendLine("差额：" + result.TotalDifference);

            var verification = ExcelAggregationService.RenderVerification(result.Verification);
            if (!string.IsNullOrWhiteSpace(verification))
            {
                builder.AppendLine();
                builder.Append(verification);
            }

            if (result.Samples.Count > 0)
            {
                builder.AppendLine();
                builder.AppendLine("分层抽样比对（抽中理由一并给出，便于判断这次抽样是否有说服力）：");
                foreach (var sample in result.Samples)
                {
                    builder.AppendLine("- " + sample.GroupKey
                        + "［" + (string.IsNullOrWhiteSpace(sample.SelectionReason) ? "未标注" : sample.SelectionReason) + "］"
                        + "：明细 " + sample.DetailValue
                        + "，汇总 " + sample.SummaryValue
                        + "，差 " + sample.Difference
                        + (sample.Matched ? "（一致）" : "（不一致）"));
                }
            }

            if (result.MissingInSummary.Count > 0)
            {
                builder.AppendLine("汇总缺失分组：" + string.Join("、", result.MissingInSummary.ToArray()));
            }

            if (result.ExtraInSummary.Count > 0)
            {
                builder.AppendLine("汇总多出分组：" + string.Join("、", result.ExtraInSummary.ToArray()));
            }

            return builder.ToString();
        }
    }
}
