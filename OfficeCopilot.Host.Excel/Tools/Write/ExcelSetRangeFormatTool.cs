using System;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Threading.Tasks;
using OfficeCopilot.Agent.Abstractions;
using OfficeCopilot.Host.Abstractions;

namespace OfficeCopilot.Host.Excel.Tools
{
    public sealed class ExcelSetRangeFormatTool : ExcelToolBase
    {
        private static readonly ToolDefinition _definition = new ToolDefinition
        {
            Name = "excel_set_range_format",
            DisplayName = "设置区域格式",
            Description = "为单元格区域设置数字格式、样式、对齐、字体与颜色。优先使用 Excel 对象模型，失败时自动回退到 VBA 执行。颜色参数一律使用 6 位十六进制 RGB 且带 # 前缀（例如 #2E7D32、#FFFFFF）。",
            SourceKind = ToolSourceKind.BuiltIn,
            SourceName = "Excel",
            IsReadOnly = false,
            RequiresConfirmation = true,
            InputSchemaJson = "{"
                + "\"type\":\"object\","
                + "\"properties\":{"
                + "\"worksheetName\":{\"type\":\"string\"},"
                + "\"address\":{\"type\":\"string\"},"
                + "\"numberFormat\":{\"type\":\"string\"},"
                + "\"styleName\":{\"type\":\"string\"},"
                + "\"horizontalAlignment\":{\"type\":\"string\"},"
                + "\"verticalAlignment\":{\"type\":\"string\"},"
                + "\"wrapText\":{\"type\":\"boolean\"},"
                + "\"shrinkToFit\":{\"type\":\"boolean\"},"
                + "\"fontName\":{\"type\":\"string\"},"
                + "\"fontSize\":{\"type\":\"number\"},"
                + "\"bold\":{\"type\":\"boolean\"},"
                + "\"italic\":{\"type\":\"boolean\"},"
                + "\"underline\":{\"type\":\"boolean\"},"
                + "\"fontColor\":{\"type\":\"string\",\"description\":\"字体颜色。必须使用 6 位十六进制 RGB 并带 # 前缀，例如 \\\"#2E7D32\\\"、\\\"#FFFFFF\\\"。也接受 \\\"rgb(46,125,50)\\\" 或英文颜色名 \\\"Red\\\"。不要传入 VBA 的 RGB()、十进制色号或 ARGB。\"},"
                + "\"fillColor\":{\"type\":\"string\",\"description\":\"单元格填充色。必须使用 6 位十六进制 RGB 并带 # 前缀，例如 \\\"#2E7D32\\\"、\\\"#FFFFFF\\\"。也接受 \\\"rgb(46,125,50)\\\" 或英文颜色名 \\\"Red\\\"。不要传入 VBA 的 RGB()、十进制色号或 ARGB。\"},"
                + "\"useSelectionWhenAddressEmpty\":{\"type\":\"boolean\"},"
                + "\"fallbackToVba\":{\"type\":\"boolean\"}"
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
                var arguments = ExcelToolJsonSerializer.Deserialize<ExcelRangeFormatWriteRequest>(argumentsJson);
                arguments = MergeCamelCaseArguments(argumentsJson, arguments);
                arguments = Normalize(argumentsJson, arguments);

                if (!HasPayload(arguments))
                {
                    return Task.FromResult(CreateFailure(
                        request,
                        "格式写入请求未提供任何可应用的格式属性。",
                        "excel.set_range_format_missing_payload"));
                }

                var result = host.SetRangeFormat(arguments);
                var content = BuildContent(result);
                var structuredContentJson = ExcelToolJsonSerializer.Serialize(result);
                return Task.FromResult(CreateSuccess(request, content, structuredContentJson));
            }
            catch (Exception ex)
            {
                return Task.FromResult(CreateFailure(
                    request,
                    ex,
                    "excel.set_range_format_failed"));
            }
        }

        private static bool HasPayload(ExcelRangeFormatWriteRequest request)
        {
            if (request == null)
            {
                return false;
            }

            return !string.IsNullOrWhiteSpace(request.NumberFormat)
                || !string.IsNullOrWhiteSpace(request.StyleName)
                || !string.IsNullOrWhiteSpace(request.HorizontalAlignment)
                || !string.IsNullOrWhiteSpace(request.VerticalAlignment)
                || request.WrapText.HasValue
                || request.ShrinkToFit.HasValue
                || !string.IsNullOrWhiteSpace(request.FontName)
                || request.FontSize.HasValue
                || request.Bold.HasValue
                || request.Italic.HasValue
                || request.Underline.HasValue
                || !string.IsNullOrWhiteSpace(request.FontColor)
                || !string.IsNullOrWhiteSpace(request.FillColor);
        }

        private static ExcelRangeFormatWriteRequest Normalize(
            string argumentsJson,
            ExcelRangeFormatWriteRequest request)
        {
            request = request ?? new ExcelRangeFormatWriteRequest();
            request.WorksheetName = NormalizeText(request.WorksheetName);
            request.Address = NormalizeText(request.Address);
            request.NumberFormat = NormalizeText(request.NumberFormat);
            request.StyleName = NormalizeText(request.StyleName);
            request.HorizontalAlignment = NormalizeText(request.HorizontalAlignment);
            request.VerticalAlignment = NormalizeText(request.VerticalAlignment);
            request.FontName = NormalizeText(request.FontName);
            request.FontColor = NormalizeText(request.FontColor);
            request.FillColor = NormalizeText(request.FillColor);

            if (!HasJsonProperty(argumentsJson, "useSelectionWhenAddressEmpty"))
            {
                request.UseSelectionWhenAddressEmpty = string.IsNullOrWhiteSpace(request.Address);
            }

            if (!HasJsonProperty(argumentsJson, "fallbackToVba"))
            {
                request.FallbackToVba = true;
            }

            return request;
        }

        private static ExcelRangeFormatWriteRequest MergeCamelCaseArguments(
            string argumentsJson,
            ExcelRangeFormatWriteRequest request)
        {
            request = request ?? new ExcelRangeFormatWriteRequest();
            if (string.IsNullOrWhiteSpace(argumentsJson))
            {
                return request;
            }

            CamelCaseFormatWriteRequest payload;
            try
            {
                payload = DeserializeCamelCasePayload(argumentsJson);
            }
            catch (SerializationException)
            {
                return request;
            }

            if (payload == null)
            {
                return request;
            }

            if (HasJsonProperty(argumentsJson, "worksheetName"))
            {
                request.WorksheetName = payload.WorksheetName ?? string.Empty;
            }

            if (HasJsonProperty(argumentsJson, "address"))
            {
                request.Address = payload.Address ?? string.Empty;
            }

            if (HasJsonProperty(argumentsJson, "numberFormat"))
            {
                request.NumberFormat = payload.NumberFormat ?? string.Empty;
            }

            if (HasJsonProperty(argumentsJson, "styleName"))
            {
                request.StyleName = payload.StyleName ?? string.Empty;
            }

            if (HasJsonProperty(argumentsJson, "horizontalAlignment"))
            {
                request.HorizontalAlignment = payload.HorizontalAlignment ?? string.Empty;
            }

            if (HasJsonProperty(argumentsJson, "verticalAlignment"))
            {
                request.VerticalAlignment = payload.VerticalAlignment ?? string.Empty;
            }

            if (HasJsonProperty(argumentsJson, "wrapText"))
            {
                request.WrapText = payload.WrapText;
            }

            if (HasJsonProperty(argumentsJson, "shrinkToFit"))
            {
                request.ShrinkToFit = payload.ShrinkToFit;
            }

            if (HasJsonProperty(argumentsJson, "fontName"))
            {
                request.FontName = payload.FontName ?? string.Empty;
            }

            if (HasJsonProperty(argumentsJson, "fontSize"))
            {
                request.FontSize = payload.FontSize;
            }

            if (HasJsonProperty(argumentsJson, "bold"))
            {
                request.Bold = payload.Bold;
            }

            if (HasJsonProperty(argumentsJson, "italic"))
            {
                request.Italic = payload.Italic;
            }

            if (HasJsonProperty(argumentsJson, "underline"))
            {
                request.Underline = payload.Underline;
            }

            if (HasJsonProperty(argumentsJson, "fontColor"))
            {
                request.FontColor = payload.FontColor ?? string.Empty;
            }

            if (HasJsonProperty(argumentsJson, "fillColor"))
            {
                request.FillColor = payload.FillColor ?? string.Empty;
            }

            if (HasJsonProperty(argumentsJson, "useSelectionWhenAddressEmpty"))
            {
                request.UseSelectionWhenAddressEmpty = payload.UseSelectionWhenAddressEmpty;
            }

            if (HasJsonProperty(argumentsJson, "fallbackToVba"))
            {
                request.FallbackToVba = payload.FallbackToVba;
            }

            return request;
        }

        private static CamelCaseFormatWriteRequest DeserializeCamelCasePayload(string argumentsJson)
        {
            using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(argumentsJson)))
            {
                var serializer = new DataContractJsonSerializer(typeof(CamelCaseFormatWriteRequest));
                return serializer.ReadObject(stream) as CamelCaseFormatWriteRequest;
            }
        }

        private static bool HasJsonProperty(string json, string propertyName)
        {
            if (string.IsNullOrWhiteSpace(json) || string.IsNullOrWhiteSpace(propertyName))
            {
                return false;
            }

            return json.IndexOf("\"" + propertyName + "\"", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static string NormalizeText(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
        }

        private static string BuildContent(ExcelRangeFormatWriteResult result)
        {
            var builder = new StringBuilder();
            builder.AppendLine("Workbook: " + (string.IsNullOrWhiteSpace(result.WorkbookName) ? "-" : result.WorkbookName));
            builder.AppendLine("Worksheet: " + (string.IsNullOrWhiteSpace(result.WorksheetName) ? "-" : result.WorksheetName));
            builder.AppendLine("Range: " + (string.IsNullOrWhiteSpace(result.Address) ? "-" : result.Address));
            builder.AppendLine("Size: " + result.RowCount + " rows x " + result.ColumnCount + " columns");
            builder.AppendLine("Applied properties: " + result.AppliedPropertyCount);
            builder.AppendLine("Applied by: " + (string.IsNullOrWhiteSpace(result.AppliedBy) ? "-" : result.AppliedBy));
            builder.AppendLine("VBA fallback used: " + (result.FallbackUsed ? "Yes" : "No"));
            builder.Append("Summary: " + (string.IsNullOrWhiteSpace(result.Summary) ? "-" : result.Summary));
            return builder.ToString();
        }

        [DataContract]
        private sealed class CamelCaseFormatWriteRequest
        {
            public CamelCaseFormatWriteRequest()
            {
                WorksheetName = string.Empty;
                Address = string.Empty;
                NumberFormat = string.Empty;
                StyleName = string.Empty;
                HorizontalAlignment = string.Empty;
                VerticalAlignment = string.Empty;
                FontName = string.Empty;
                FontColor = string.Empty;
                FillColor = string.Empty;
                UseSelectionWhenAddressEmpty = true;
                FallbackToVba = true;
            }

            [DataMember(Name = "worksheetName", EmitDefaultValue = false)]
            public string WorksheetName { get; set; }

            [DataMember(Name = "address", EmitDefaultValue = false)]
            public string Address { get; set; }

            [DataMember(Name = "numberFormat", EmitDefaultValue = false)]
            public string NumberFormat { get; set; }

            [DataMember(Name = "styleName", EmitDefaultValue = false)]
            public string StyleName { get; set; }

            [DataMember(Name = "horizontalAlignment", EmitDefaultValue = false)]
            public string HorizontalAlignment { get; set; }

            [DataMember(Name = "verticalAlignment", EmitDefaultValue = false)]
            public string VerticalAlignment { get; set; }

            [DataMember(Name = "wrapText", EmitDefaultValue = false)]
            public bool? WrapText { get; set; }

            [DataMember(Name = "shrinkToFit", EmitDefaultValue = false)]
            public bool? ShrinkToFit { get; set; }

            [DataMember(Name = "fontName", EmitDefaultValue = false)]
            public string FontName { get; set; }

            [DataMember(Name = "fontSize", EmitDefaultValue = false)]
            public double? FontSize { get; set; }

            [DataMember(Name = "bold", EmitDefaultValue = false)]
            public bool? Bold { get; set; }

            [DataMember(Name = "italic", EmitDefaultValue = false)]
            public bool? Italic { get; set; }

            [DataMember(Name = "underline", EmitDefaultValue = false)]
            public bool? Underline { get; set; }

            [DataMember(Name = "fontColor", EmitDefaultValue = false)]
            public string FontColor { get; set; }

            [DataMember(Name = "fillColor", EmitDefaultValue = false)]
            public string FillColor { get; set; }

            [DataMember(Name = "useSelectionWhenAddressEmpty", EmitDefaultValue = false)]
            public bool UseSelectionWhenAddressEmpty { get; set; }

            [DataMember(Name = "fallbackToVba", EmitDefaultValue = false)]
            public bool FallbackToVba { get; set; }
        }
    }
}