using System;
using System.Text;
using System.Threading.Tasks;
using OfficeCopilot.Agent.Abstractions;
using OfficeCopilot.Host.Abstractions;

namespace OfficeCopilot.Host.Excel.Tools
{
    public sealed class ExcelGetRangeStyleTool : ExcelToolBase
    {
        private static readonly ToolDefinition _definition = new ToolDefinition
        {
            Name = "excel_get_range_style",
            DisplayName = "读取区域格式",
            Description = "回读指定区域实际生效的格式：填充色、字体颜色/字号/加粗/倾斜/下划线、数字格式、对齐方式、外边框，以及所套用的命名样式。"
                + "可用于验证格式写入是否生效。区域内某属性取值不一致时该属性返回 mixed（不代表未设置）。若未提供地址，可回退读取当前选区。",
            SourceKind = ToolSourceKind.BuiltIn,
            SourceName = "Excel",
            IsReadOnly = true,
            RequiresConfirmation = false,
            InputSchemaJson = "{"
                + "\"type\":\"object\","
                + "\"properties\":{"
                + "\"worksheetName\":{\"type\":\"string\"},"
                + "\"address\":{\"type\":\"string\"},"
                + "\"useSelectionWhenAddressEmpty\":{\"type\":\"boolean\"}"
                + "},"
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
                var argumentsJson = request == null ? null : request.ArgumentsJson;
                var arguments = ExcelToolJsonSerializer.Deserialize<ExcelRangeRequest>(argumentsJson);
                arguments = ExcelToolRequestNormalizer.NormalizeRangeRequest(argumentsJson, arguments);

                var result = host.GetRangeStyle(arguments);
                var content = BuildContent(result);
                var structuredContentJson = ExcelToolJsonSerializer.Serialize(result);
                return Task.FromResult(CreateSuccess(request, content, structuredContentJson));
            }
            catch (Exception ex)
            {
                return Task.FromResult(CreateFailure(request, ex, "excel.get_range_style_failed"));
            }
        }

        private static string BuildContent(ExcelRangeStyleResult result)
        {
            var builder = new StringBuilder();
            builder.AppendLine("工作簿：" + (result.WorkbookName ?? string.Empty));
            builder.AppendLine("工作表：" + (result.WorksheetName ?? string.Empty));
            builder.AppendLine("区域：" + (result.Address ?? string.Empty) + "（" + result.CellCount + " 个单元格）");
            builder.AppendLine("填充色：" + Display(result.FillColor));
            builder.AppendLine("字体颜色：" + Display(result.FontColor));
            builder.AppendLine("字体：" + Display(result.FontName) + " " + Display(result.FontSize) + "pt");
            builder.AppendLine("加粗/倾斜/下划线：" + Display(result.Bold) + " / " + Display(result.Italic) + " / " + Display(result.Underline));
            builder.AppendLine("数字格式：" + Display(result.NumberFormat));
            builder.AppendLine("对齐：" + Display(result.HorizontalAlignment) + " / " + Display(result.VerticalAlignment));
            builder.AppendLine("外边框：" + Display(result.Borders));
            builder.AppendLine("命名样式：" + Display(result.StyleName) + (result.IsBuiltInStyle ? "（内置）" : string.Empty));

            if (!string.IsNullOrWhiteSpace(result.StyleDescription))
            {
                builder.AppendLine("说明：" + result.StyleDescription);
            }

            if (string.IsNullOrWhiteSpace(result.MixedProperties))
            {
                builder.Append("以上取值在整个区域内一致。");
            }
            else
            {
                builder.Append("注意：" + result.MixedProperties
                    + " 在区域内取值不一致（mixed），不能据此断定该属性未被设置；请缩小到单个单元格再读一次。");
            }

            return builder.ToString();
        }

        private static string Display(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? "-" : value;
        }
    }
}