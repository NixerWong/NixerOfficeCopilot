using System;
using System.Text;
using System.Threading.Tasks;
using OfficeCopilot.Agent.Abstractions;
using OfficeCopilot.Host.Abstractions;

namespace OfficeCopilot.Host.Excel.Tools
{
    public sealed class ExcelListWorksheetsTool : ExcelToolBase
    {
        private static readonly ToolDefinition _definition = new ToolDefinition
        {
            Name = "excel_list_worksheets",
            DisplayName = "列出工作表",
            Description = "返回当前工作簿各工作表的名称、可见状态、已用区域大小、表头、公式和保护状态。",
            SourceKind = ToolSourceKind.BuiltIn,
            SourceName = "Excel",
            IsReadOnly = true,
            RequiresConfirmation = false,
            InputSchemaJson = "{\"type\":\"object\",\"properties\":{},\"additionalProperties\":false}"
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
                var result = host.ListWorksheets();
                var content = BuildContent(result);
                var structuredContentJson = ExcelToolJsonSerializer.Serialize(result);

                return Task.FromResult(CreateSuccess(request, content, structuredContentJson));
            }
            catch (Exception ex)
            {
                return Task.FromResult(CreateFailure(
                    request,
                    ex,
                    "excel.list_worksheets_failed"));
            }
        }

        private static string BuildContent(ExcelWorksheetListResult result)
        {
            var builder = new StringBuilder();
            builder.AppendLine("工作簿：" + (string.IsNullOrWhiteSpace(result.WorkbookName) ? "-" : result.WorkbookName));
            builder.AppendLine("工作表数量：" + result.TotalWorksheetCount);
            if (result.IsTruncated)
            {
                builder.AppendLine("工作表结构已截断；仅返回前 " + result.Worksheets.Count + " 张。");
            }

            if (result.WorksheetGroups != null && result.WorksheetGroups.Count > 0)
            {
                builder.AppendLine("表头相似候选组（列名集合相似度；不代表列顺序或类型兼容）：");
                foreach (var group in result.WorksheetGroups)
                {
                    builder.Append("- ")
                        .Append(group.GroupId)
                        .Append("：")
                        .Append(string.Join("、", group.WorksheetNames))
                        .Append("；最低相似度 ")
                        .AppendLine(group.MinimumHeaderSimilarity.ToString("P0"));
                }
            }

            if (result.SharedKeyCandidates != null && result.SharedKeyCandidates.Count > 0)
            {
                builder.AppendLine("跨表共享列候选（唯一性未验证，使用前需画像确认）：");
                foreach (var candidate in result.SharedKeyCandidates)
                {
                    builder.Append("- ")
                        .Append(candidate.ColumnName)
                        .Append("：")
                        .AppendLine(string.Join("、", candidate.WorksheetNames));
                }
            }

            for (var index = 0; index < result.Worksheets.Count; index++)
            {
                var worksheet = result.Worksheets[index];
                builder.Append(index + 1);
                builder.Append(". ");
                builder.Append(worksheet.Name);
                if (!string.IsNullOrWhiteSpace(worksheet.CodeName))
                {
                    builder.Append(" (CodeName=");
                    builder.Append(worksheet.CodeName);
                    builder.Append(")");
                }

                builder.Append(" [");
                builder.Append(worksheet.IsVisible ? "可见" : "隐藏");
                builder.Append("] UsedRange=");
                builder.Append(string.IsNullOrWhiteSpace(worksheet.UsedRangeAddress) ? "-" : worksheet.UsedRangeAddress);
                builder.Append(" (");
                builder.Append(worksheet.UsedRangeRows);
                builder.Append("行×");
                builder.Append(worksheet.UsedRangeColumns);
                builder.Append("列)");
                builder.Append("; 角色=");
                builder.Append(worksheet.RoleGuess ?? "other");
                builder.Append("; 公式=");
                builder.Append(worksheet.HasFormulas ? "有" : "无");
                builder.Append("; 保护=");
                builder.AppendLine(worksheet.IsProtected ? "是" : "否");
                if (!string.IsNullOrWhiteSpace(worksheet.HeaderSignature))
                {
                    builder.AppendLine("   表头：" + worksheet.HeaderSignature);
                }

                if (!string.IsNullOrWhiteSpace(worksheet.InspectionError))
                {
                    builder.AppendLine("   结构读取失败：" + worksheet.InspectionError);
                }
            }

            return builder.ToString();
        }
    }
}