using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Threading.Tasks;
using OfficeCopilot.Agent.Abstractions;
using OfficeCopilot.Host.Abstractions;

namespace OfficeCopilot.Host.Excel.Tools
{
    public sealed class ExcelWriteRangeTool : ExcelToolBase
    {
        private static readonly ToolDefinition _definition = new ToolDefinition
        {
            Name = "excel_write_range",
            DisplayName = "写入区域",
            Description = "向指定单元格或区域写入文本、数字、布尔值或二维文本矩阵；若未提供地址，可回退写入当前选区。",
            SourceKind = ToolSourceKind.BuiltIn,
            SourceName = "Excel",
            IsReadOnly = false,
            RequiresConfirmation = true,
            InputSchemaJson = "{"
                + "\"type\":\"object\","
                + "\"properties\":{"
                + "\"worksheetName\":{\"type\":\"string\"},"
                + "\"address\":{\"type\":\"string\"},"
                + "\"value\":{\"type\":\"string\"},"
                + "\"numberValue\":{\"type\":\"number\"},"
                + "\"booleanValue\":{\"type\":\"boolean\"},"
                + "\"values\":{\"type\":\"array\",\"items\":{\"type\":\"array\",\"items\":{\"type\":\"string\"}}},"
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
            var logPath = ExcelToolDiagnosticsLogger.GetCurrentLogFilePath();

            try
            {
                var host = GetExcelToolHost(context);
                var argumentsJson = request == null ? null : request.ArgumentsJson;

                ExcelToolDiagnosticsLogger.Log(
                    Definition.Name,
                    context,
                    request,
                    "received",
                    "argumentsJson=" + SafeLogValue(argumentsJson));

                var arguments = ExcelToolJsonSerializer.Deserialize<ExcelRangeWriteRequest>(argumentsJson);
                arguments = MergeCamelCaseArguments(argumentsJson, arguments);
                arguments = ExcelToolRequestNormalizer.NormalizeRangeWriteRequest(argumentsJson, arguments);

                ExcelToolDiagnosticsLogger.Log(
                    Definition.Name,
                    context,
                    request,
                    "normalized",
                    BuildRequestLog(argumentsJson, arguments));

                if (!HasWritePayload(argumentsJson, arguments))
                {
                    const string errorMessage = "写入请求缺少有效值，请提供 value、numberValue、booleanValue 或 values。";
                    ExcelToolDiagnosticsLogger.Log(
                        Definition.Name,
                        context,
                        request,
                        "validation_failed",
                        errorMessage);

                    return Task.FromResult(CreateFailure(
                        request,
                        errorMessage,
                        "excel.write_range_missing_value",
                        BuildFailureContent(logPath)));
                }

                var result = host.WriteRange(arguments);

                ExcelToolDiagnosticsLogger.Log(
                    Definition.Name,
                    context,
                    request,
                    "completed",
                    BuildResultLog(result));

                var content = BuildContent(result, logPath);
                var structuredContentJson = ExcelToolJsonSerializer.Serialize(result);
                return Task.FromResult(CreateSuccess(request, content, structuredContentJson));
            }
            catch (Exception ex)
            {
                ExcelToolDiagnosticsLogger.Log(
                    Definition.Name,
                    context,
                    request,
                    "failed",
                    ex.ToString());

                return Task.FromResult(CreateFailure(
                    request,
                    ex,
                    "excel.write_range_failed",
                    BuildFailureContent(logPath)));
            }
        }

        private static ExcelRangeWriteRequest MergeCamelCaseArguments(
            string argumentsJson,
            ExcelRangeWriteRequest request)
        {
            request = request ?? new ExcelRangeWriteRequest();
            if (string.IsNullOrWhiteSpace(argumentsJson))
            {
                return request;
            }

            CamelCaseWriteRequest payload;
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

            if (HasJsonProperty(argumentsJson, "value"))
            {
                request.Value = payload.Value ?? string.Empty;
            }

            if (HasJsonProperty(argumentsJson, "numberValue"))
            {
                request.NumberValue = payload.NumberValue;
            }

            if (HasJsonProperty(argumentsJson, "booleanValue"))
            {
                request.BooleanValue = payload.BooleanValue;
            }

            if (HasJsonProperty(argumentsJson, "values"))
            {
                request.Values = payload.Values ?? new List<string[]>();
            }

            if (HasJsonProperty(argumentsJson, "useSelectionWhenAddressEmpty"))
            {
                request.UseSelectionWhenAddressEmpty = payload.UseSelectionWhenAddressEmpty;
            }

            return request;
        }

        private static CamelCaseWriteRequest DeserializeCamelCasePayload(string argumentsJson)
        {
            using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(argumentsJson)))
            {
                var serializer = new DataContractJsonSerializer(typeof(CamelCaseWriteRequest));
                return serializer.ReadObject(stream) as CamelCaseWriteRequest;
            }
        }

        private static bool HasWritePayload(string argumentsJson, ExcelRangeWriteRequest request)
        {
            if (request == null)
            {
                return false;
            }

            if (request.NumberValue.HasValue || request.BooleanValue.HasValue)
            {
                return true;
            }

            if (request.Values != null && request.Values.Count > 0)
            {
                return true;
            }

            if (HasJsonProperty(argumentsJson, "value") || HasJsonProperty(argumentsJson, "Value"))
            {
                return true;
            }

            return !string.IsNullOrEmpty(request.Value);
        }

        private static bool HasJsonProperty(string json, string propertyName)
        {
            if (string.IsNullOrWhiteSpace(json) || string.IsNullOrWhiteSpace(propertyName))
            {
                return false;
            }

            return json.IndexOf("\"" + propertyName + "\"", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static string BuildContent(ExcelRangeWriteResult result, string logPath)
        {
            var builder = new StringBuilder();
            builder.AppendLine("工作簿：" + (string.IsNullOrWhiteSpace(result.WorkbookName) ? "-" : result.WorkbookName));
            builder.AppendLine("工作表：" + (string.IsNullOrWhiteSpace(result.WorksheetName) ? "-" : result.WorksheetName));
            builder.AppendLine("区域：" + (string.IsNullOrWhiteSpace(result.Address) ? "-" : result.Address));
            builder.AppendLine("大小：" + result.RowCount + " 行 × " + result.ColumnCount + " 列");
            builder.AppendLine("写入单元格：" + result.WrittenCellCount);
            builder.AppendLine("值类型：" + (string.IsNullOrWhiteSpace(result.ValueKind) ? "-" : result.ValueKind));
            builder.AppendLine("预览：" + GetPreviewText(result));
            builder.Append("日志：" + (string.IsNullOrWhiteSpace(logPath) ? "-" : logPath));
            return builder.ToString();
        }

        private static string BuildFailureContent(string logPath)
        {
            return "日志：" + (string.IsNullOrWhiteSpace(logPath) ? "-" : logPath);
        }

        private static string BuildRequestLog(string argumentsJson, ExcelRangeWriteRequest request)
        {
            var preview = GetRequestPreview(request);
            return string.Format(
                CultureInfo.InvariantCulture,
                "worksheet={0}; address={1}; useSelectionWhenAddressEmpty={2}; value={3}; numberValue={4}; booleanValue={5}; matrixRows={6}; matrixColumns={7}; rawHasValueProperty={8}",
                SafeLogValue(request == null ? string.Empty : request.WorksheetName),
                SafeLogValue(request == null ? string.Empty : request.Address),
                request != null && request.UseSelectionWhenAddressEmpty ? "true" : "false",
                SafeLogValue(preview),
                request != null && request.NumberValue.HasValue ? request.NumberValue.Value.ToString(CultureInfo.InvariantCulture) : "-",
                request != null && request.BooleanValue.HasValue ? (request.BooleanValue.Value ? "true" : "false") : "-",
                request != null && request.Values != null ? request.Values.Count : 0,
                GetMatrixColumnCount(request == null ? null : request.Values),
                HasJsonProperty(argumentsJson, "value") || HasJsonProperty(argumentsJson, "Value") ? "true" : "false");
        }

        private static string BuildResultLog(ExcelRangeWriteResult result)
        {
            if (result == null)
            {
                return "result=<null>";
            }

            return string.Format(
                CultureInfo.InvariantCulture,
                "workbook={0}; worksheet={1}; address={2}; rowCount={3}; columnCount={4}; writtenCellCount={5}; valueKind={6}; preview={7}",
                SafeLogValue(result.WorkbookName),
                SafeLogValue(result.WorksheetName),
                SafeLogValue(result.Address),
                result.RowCount,
                result.ColumnCount,
                result.WrittenCellCount,
                SafeLogValue(result.ValueKind),
                SafeLogValue(result.Preview));
        }

        private static string GetPreviewText(ExcelRangeWriteResult result)
        {
            if (result == null)
            {
                return "-";
            }

            if (!string.IsNullOrEmpty(result.Preview))
            {
                return result.Preview;
            }

            return string.IsNullOrWhiteSpace(result.ValueKind) ? "-" : "(空字符串)";
        }

        private static string GetRequestPreview(ExcelRangeWriteRequest request)
        {
            if (request == null)
            {
                return "-";
            }

            if (request.NumberValue.HasValue)
            {
                return request.NumberValue.Value.ToString(CultureInfo.InvariantCulture);
            }

            if (request.BooleanValue.HasValue)
            {
                return request.BooleanValue.Value ? "true" : "false";
            }

            if (request.Values != null && request.Values.Count > 0)
            {
                return string.Format(
                    CultureInfo.InvariantCulture,
                    "matrix({0}x{1})",
                    request.Values.Count,
                    GetMatrixColumnCount(request.Values));
            }

            if (request.Value == null)
            {
                return "-";
            }

            return request.Value.Length == 0 ? "(空字符串)" : request.Value;
        }

        private static int GetMatrixColumnCount(IList<string[]> values)
        {
            var columnCount = 0;
            if (values == null)
            {
                return columnCount;
            }

            for (var rowIndex = 0; rowIndex < values.Count; rowIndex++)
            {
                var row = values[rowIndex];
                var candidate = row == null ? 0 : row.Length;
                if (candidate > columnCount)
                {
                    columnCount = candidate;
                }
            }

            return columnCount;
        }

        private static string SafeLogValue(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return "-";
            }

            return value
                .Replace("\r", "\\r")
                .Replace("\n", "\\n");
        }

        [DataContract]
        private sealed class CamelCaseWriteRequest
        {
            public CamelCaseWriteRequest()
            {
                WorksheetName = string.Empty;
                Address = string.Empty;
                Value = string.Empty;
                Values = new List<string[]>();
                UseSelectionWhenAddressEmpty = true;
            }

            [DataMember(Name = "worksheetName", EmitDefaultValue = false)]
            public string WorksheetName { get; set; }

            [DataMember(Name = "address", EmitDefaultValue = false)]
            public string Address { get; set; }

            [DataMember(Name = "value", EmitDefaultValue = false)]
            public string Value { get; set; }

            [DataMember(Name = "numberValue", EmitDefaultValue = false)]
            public double? NumberValue { get; set; }

            [DataMember(Name = "booleanValue", EmitDefaultValue = false)]
            public bool? BooleanValue { get; set; }

            [DataMember(Name = "values", EmitDefaultValue = false)]
            public List<string[]> Values { get; set; }

            [DataMember(Name = "useSelectionWhenAddressEmpty", EmitDefaultValue = false)]
            public bool UseSelectionWhenAddressEmpty { get; set; }
        }
    }

    internal static class ExcelToolDiagnosticsLogger
    {
        public static string GetCurrentLogFilePath()
        {
            return ToolCallDiagnosticsLogger.GetCurrentLogFilePath("Excel");
        }

        public static void Log(
            string toolName,
            ToolExecutionContext context,
            ToolExecutionRequest request,
            string stage,
            string message)
        {
            ToolCallDiagnosticsLogger.Log("Excel", toolName, context, request, stage, message);
        }
    }
}
