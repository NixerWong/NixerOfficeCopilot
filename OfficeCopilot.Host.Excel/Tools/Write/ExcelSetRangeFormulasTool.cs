using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Threading.Tasks;
using OfficeCopilot.Agent.Abstractions;
using OfficeCopilot.Host.Abstractions;

namespace OfficeCopilot.Host.Excel.Tools
{
    public sealed class ExcelSetRangeFormulasTool : ExcelToolBase
    {
        private static readonly ToolDefinition _definition = new ToolDefinition
        {
            Name = "excel_set_range_formulas",
            DisplayName = "Set Range Formulas",
            Description = "Sets formulas in cells or ranges using the Excel object model, with an automatic fallback to VBA if needed.",
            SourceKind = ToolSourceKind.BuiltIn,
            SourceName = "Excel",
            IsReadOnly = false,
            RequiresConfirmation = true,
            InputSchemaJson = "{"
                + "\"type\":\"object\","
                + "\"properties\":{"
                + "\"worksheetName\":{\"type\":\"string\"},"
                + "\"address\":{\"type\":\"string\"},"
                + "\"formula\":{\"type\":\"string\"},"
                + "\"formulas\":{\"type\":\"array\",\"items\":{\"type\":\"array\",\"items\":{\"type\":\"string\"}}},"
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
                var arguments = ExcelToolJsonSerializer.Deserialize<ExcelRangeFormulaWriteRequest>(argumentsJson);
                arguments = MergeCamelCaseArguments(argumentsJson, arguments);
                arguments = Normalize(argumentsJson, arguments);

                if (!HasPayload(arguments))
                {
                    return Task.FromResult(CreateFailure(
                        request,
                        "公式写入请求缺少 formula 或 formulas。",
                        "excel.set_range_formulas_missing_formula"));
                }

                var result = host.SetRangeFormulas(arguments);
                var content = BuildContent(result);
                var structuredContentJson = ExcelToolJsonSerializer.Serialize(result);
                return Task.FromResult(CreateSuccess(request, content, structuredContentJson));
            }
            catch (Exception ex)
            {
                return Task.FromResult(CreateFailure(
                    request,
                    ex,
                    "excel.set_range_formulas_failed"));
            }
        }

        private static bool HasPayload(ExcelRangeFormulaWriteRequest request)
        {
            if (request == null)
            {
                return false;
            }

            return !string.IsNullOrWhiteSpace(request.Formula)
                || (request.Formulas != null && request.Formulas.Count > 0);
        }

        private static ExcelRangeFormulaWriteRequest Normalize(
            string argumentsJson,
            ExcelRangeFormulaWriteRequest request)
        {
            request = request ?? new ExcelRangeFormulaWriteRequest();
            request.WorksheetName = NormalizeText(request.WorksheetName);
            request.Address = NormalizeText(request.Address);
            request.Formula = request.Formula ?? string.Empty;

            if (request.Formulas == null)
            {
                request.Formulas = new List<string[]>();
            }

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

        private static ExcelRangeFormulaWriteRequest MergeCamelCaseArguments(
            string argumentsJson,
            ExcelRangeFormulaWriteRequest request)
        {
            request = request ?? new ExcelRangeFormulaWriteRequest();
            if (string.IsNullOrWhiteSpace(argumentsJson))
            {
                return request;
            }

            CamelCaseFormulaWriteRequest payload;
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

            if (HasJsonProperty(argumentsJson, "formula"))
            {
                request.Formula = payload.Formula ?? string.Empty;
            }

            if (HasJsonProperty(argumentsJson, "formulas"))
            {
                request.Formulas = payload.Formulas ?? new List<string[]>();
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

        private static CamelCaseFormulaWriteRequest DeserializeCamelCasePayload(string argumentsJson)
        {
            using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(argumentsJson)))
            {
                var serializer = new DataContractJsonSerializer(typeof(CamelCaseFormulaWriteRequest));
                return serializer.ReadObject(stream) as CamelCaseFormulaWriteRequest;
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

        private static string BuildContent(ExcelRangeFormulaWriteResult result)
        {
            var builder = new StringBuilder();
            builder.AppendLine("工作簿：" + (string.IsNullOrWhiteSpace(result.WorkbookName) ? "-" : result.WorkbookName));
            builder.AppendLine("工作表：" + (string.IsNullOrWhiteSpace(result.WorksheetName) ? "-" : result.WorksheetName));
            builder.AppendLine("区域：" + (string.IsNullOrWhiteSpace(result.Address) ? "-" : result.Address));
            builder.AppendLine("大小：" + result.RowCount + " 行 x " + result.ColumnCount + " 列");
            builder.AppendLine("写入单元格：" + result.WrittenCellCount);
            builder.AppendLine("公式类型：" + (string.IsNullOrWhiteSpace(result.FormulaKind) ? "-" : result.FormulaKind));
            builder.AppendLine("执行方式：" + (string.IsNullOrWhiteSpace(result.AppliedBy) ? "-" : result.AppliedBy));
            builder.AppendLine("是否走 VBA 兜底：" + (result.FallbackUsed ? "是" : "否"));
            builder.Append("预览：" + (string.IsNullOrWhiteSpace(result.Preview) ? "-" : result.Preview));
            return builder.ToString();
        }

        [DataContract]
        private sealed class CamelCaseFormulaWriteRequest
        {
            public CamelCaseFormulaWriteRequest()
            {
                WorksheetName = string.Empty;
                Address = string.Empty;
                Formula = string.Empty;
                Formulas = new List<string[]>();
                UseSelectionWhenAddressEmpty = true;
                FallbackToVba = true;
            }

            [DataMember(Name = "worksheetName", EmitDefaultValue = false)]
            public string WorksheetName { get; set; }

            [DataMember(Name = "address", EmitDefaultValue = false)]
            public string Address { get; set; }

            [DataMember(Name = "formula", EmitDefaultValue = false)]
            public string Formula { get; set; }

            [DataMember(Name = "formulas", EmitDefaultValue = false)]
            public List<string[]> Formulas { get; set; }

            [DataMember(Name = "useSelectionWhenAddressEmpty", EmitDefaultValue = false)]
            public bool UseSelectionWhenAddressEmpty { get; set; }

            [DataMember(Name = "fallbackToVba", EmitDefaultValue = false)]
            public bool FallbackToVba { get; set; }
        }
    }
}