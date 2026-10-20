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
            DisplayName = "汇总对账验证",
            Description = "校验一张汇总表是否与明细数据一致：总量对账 + 分组数对账 + 分组抽样比对，"
                + "返回结论与差异项而非全量清单。验证大表结果必须使用本工具，严禁逐行比对。",
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
                + "\"detailGroupColumn\":{\"type\":\"string\",\"description\":\"明细中的分组列名。\"},"
                + "\"detailValueColumn\":{\"type\":\"string\",\"description\":\"明细中参与合计的数值列名。\"},"
                + "\"summaryWorksheetName\":{\"type\":\"string\"},"
                + "\"summaryAddress\":{\"type\":\"string\"},"
                + "\"summaryTableName\":{\"type\":\"string\"},"
                + "\"summaryGroupColumn\":{\"type\":\"string\",\"description\":\"汇总表中的分组列名，留空则沿用 detailGroupColumn。\"},"
                + "\"summaryValueColumn\":{\"type\":\"string\",\"description\":\"汇总表中的合计列名，留空则沿用 detailValueColumn。\"},"
                + "\"sampleCount\":{\"type\":\"integer\",\"minimum\":1,\"description\":\"抽样比对的分组数，默认 5。\"},"
                + "\"tolerance\":{\"type\":\"number\",\"description\":\"浮点比较容差，默认 0.01。\"}"
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
