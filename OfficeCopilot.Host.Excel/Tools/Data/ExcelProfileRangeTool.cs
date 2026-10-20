using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using OfficeCopilot.Agent.Abstractions;
using OfficeCopilot.Host.Abstractions;

namespace OfficeCopilot.Host.Excel.Tools
{
    public sealed class ExcelProfileRangeTool : ExcelToolBase
    {
        private static readonly ToolDefinition _definition = new ToolDefinition
        {
            Name = "excel_profile_range",
            DisplayName = "表结构画像",
            Description = "一次性返回数据区的结构画像：每列的名称、推断类型、非空数、去重数、Top-K 取值及频次，"
                + "数值列另附 min/max/sum/average，并给出前几行样例。返回体量与数据行数无关。"
                + "可通过 sources 一次画像多个区域，并报告列名、顺序、类型和表头推断的一致性。"
                + "处理中大型表格时应先调用本工具了解结构，不要逐页读取原始数据。",
            SourceKind = ToolSourceKind.BuiltIn,
            SourceName = "Excel",
            IsReadOnly = true,
            RequiresConfirmation = false,
            InputSchemaJson = "{"
                + "\"type\":\"object\","
                + "\"properties\":{"
                + "\"worksheetName\":{\"type\":\"string\",\"description\":\"工作表名，留空表示活动工作表。\"},"
                + "\"address\":{\"type\":\"string\",\"description\":\"区域地址，如 A1:H1001。与 tableName 二选一；都不给时按选区或当前区域推断。\"},"
                + "\"tableName\":{\"type\":\"string\",\"description\":\"Excel 表（ListObject）名称。\"},"
                + "\"headerMode\":{\"type\":\"string\",\"enum\":[\"auto\",\"yes\",\"no\"],\"description\":\"首行是否为表头，默认 auto 自动推断。\"},"
                + "\"topK\":{\"type\":\"integer\",\"minimum\":1,\"description\":\"每列返回的高频取值数，默认 10。\"},"
                + "\"sampleRowCount\":{\"type\":\"integer\",\"minimum\":1,\"description\":\"样例行数，默认 5。\"},"
                + "\"sources\":{\"type\":\"array\",\"description\":\"可选。一次画像多个来源区域；使用时不要同时填写顶层 worksheetName、address 或 tableName。每项需有唯一 alias，并指定 worksheetName+address 或 tableName。\","
                + "\"minItems\":1,\"items\":{\"type\":\"object\",\"properties\":{"
                + "\"worksheetName\":{\"type\":\"string\"},"
                + "\"address\":{\"type\":\"string\"},"
                + "\"tableName\":{\"type\":\"string\"},"
                + "\"alias\":{\"type\":\"string\"}"
                + "},\"required\":[\"alias\"],\"additionalProperties\":false}}"
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
                var arguments = ExcelToolJsonSerializer.Deserialize<ExcelProfileRangeRequest>(
                    request == null ? null : request.ArgumentsJson);

                var result = host.ProfileRange(arguments);
                var content = BuildContent(result);
                var structuredContentJson = ExcelToolJsonSerializer.Serialize(result);
                return Task.FromResult(CreateSuccess(request, content, structuredContentJson));
            }
            catch (Exception ex)
            {
                return Task.FromResult(CreateFailure(request, ex, "excel.profile_range_failed"));
            }
        }

        private static string BuildContent(ExcelProfileRangeResult result)
        {
            if (string.Equals(result.Topology, "multiSource", StringComparison.OrdinalIgnoreCase))
            {
                return BuildMultiSourceContent(result);
            }

            var builder = new StringBuilder();
            builder.AppendLine("工作簿：" + (result.WorkbookName ?? "-"));
            builder.AppendLine("工作表：" + (result.WorksheetName ?? "-"));
            builder.AppendLine("区域：" + (result.Address ?? "-") + "（来源：" + DescribeSourceKind(result.SourceKind) + "）");
            if (!string.IsNullOrWhiteSpace(result.TableName))
            {
                builder.AppendLine("表：" + result.TableName);
            }

            builder.AppendLine("规模：" + result.RowCount + " 数据行 × " + result.ColumnCount + " 列");
            builder.AppendLine(ExcelAggregationService.RenderCoverage(string.Empty, result.Coverage));
            if (result.HeaderInferred)
            {
                builder.AppendLine("表头推断：" + result.HeaderInferenceReason);
            }

            if (result.IsTruncated)
            {
                builder.AppendLine("已截断：" + result.TruncationNote);
            }

            builder.AppendLine();
            builder.AppendLine("列画像：");
            foreach (var column in result.Columns)
            {
                builder.Append("- ").Append(column.Name)
                    .Append("（").Append(column.InferredType).Append("）")
                    .Append(" 非空 ").Append(column.NonEmptyCount)
                    .Append("，去重 ").Append(column.DistinctCount);

                if (column.ErrorCount > 0)
                {
                    builder.Append("，错误值 ").Append(column.ErrorCount);
                }

                if (column.HasNumericStats)
                {
                    builder.Append("，min ").Append(column.MinText)
                        .Append("，max ").Append(column.MaxText)
                        .Append("，sum ").Append(column.Sum)
                        .Append("，avg ").Append(column.Average);
                }

                builder.AppendLine();

                if (column.TopValues.Count > 0)
                {
                    builder.Append("  高频值：");
                    for (var i = 0; i < column.TopValues.Count; i++)
                    {
                        if (i > 0)
                        {
                            builder.Append("、");
                        }

                        var top = column.TopValues[i];
                        builder.Append(string.IsNullOrEmpty(top.Value) ? "(空)" : top.Value)
                            .Append("×").Append(top.Count);
                    }

                    builder.AppendLine();
                }
            }

            if (result.SampleRows.Count > 0)
            {
                builder.AppendLine();
                builder.AppendLine("样例行：");
                foreach (var row in result.SampleRows)
                {
                    builder.AppendLine("  " + string.Join(" | ", row ?? new string[0]));
                }
            }

            if (result.DataQualityFlags != null && result.DataQualityFlags.Count > 0)
            {
                builder.AppendLine();
                builder.AppendLine("数据质量提示：");
                foreach (var flag in result.DataQualityFlags)
                {
                    builder.Append("- ").Append(DescribeQualityFlag(flag.Name));
                    if (flag.Count.HasValue)
                    {
                        builder.Append("（").Append(flag.Count.Value).Append(" 处");
                        if (flag.SampleRows != null && flag.SampleRows.Count > 0)
                        {
                            builder.Append("，样例行 ")
                                .Append(string.Join("、", flag.SampleRows));
                        }

                        builder.Append("）");
                    }
                    else if (flag.SampleRows != null && flag.SampleRows.Count > 0)
                    {
                        builder.Append("（样例行 ")
                            .Append(string.Join("、", flag.SampleRows))
                            .Append("）");
                    }

                    builder.AppendLine();
                    if (!string.IsNullOrWhiteSpace(flag.Impact))
                    {
                        builder.AppendLine("  影响：" + flag.Impact);
                    }

                    if (!string.IsNullOrWhiteSpace(flag.Recommendation))
                    {
                        builder.AppendLine("  建议：" + flag.Recommendation);
                    }
                }
            }

            if (result.DataQualityNotes != null && result.DataQualityNotes.Count > 0)
            {
                builder.AppendLine();
                builder.AppendLine("质量检查范围说明：");
                foreach (var note in result.DataQualityNotes)
                {
                    builder.AppendLine("- " + note);
                }
            }

            return builder.ToString();
        }

        private static string BuildMultiSourceContent(ExcelProfileRangeResult result)
        {
            var builder = new StringBuilder();
            builder.AppendLine("工作簿：" + (result.WorkbookName ?? "-"));
            builder.AppendLine("多区域画像：共 " + result.SourceProfiles.Count
                + " 个来源，合计 " + result.RowCount + " 数据行。");
            var consistency = result.HeaderConsistency;
            if (consistency != null)
            {
                builder.AppendLine("表头一致性：列名 "
                    + (consistency.NamesConsistent ? "一致" : "不一致")
                    + "；列顺序 " + (consistency.OrderConsistent ? "一致" : "不一致")
                    + "；推断类型 " + (consistency.TypesConsistent ? "一致" : "不一致")
                    + "；表头推断模式 "
                    + (consistency.HeaderInferenceConsistent ? "一致" : "不一致") + "。");
                foreach (var difference in consistency.Differences ?? new System.Collections.Generic.List<string>())
                {
                    builder.AppendLine("- " + difference);
                }
            }

            foreach (var source in result.SourceProfiles)
            {
                builder.AppendLine();
                builder.AppendLine("来源 " + source.Alias + "：" + source.WorksheetName + "!"
                    + source.Address + "（" + source.RowCount + " 行 × "
                    + source.ColumnCount + " 列）");
                builder.AppendLine(ExcelAggregationService.RenderCoverage(
                    source.Alias + "：",
                    source.Coverage));
                if (source.HeaderInferred)
                {
                    builder.AppendLine("表头推断：" + source.HeaderInferenceReason);
                }

                foreach (var column in source.Columns ?? new System.Collections.Generic.List<ExcelColumnProfile>())
                {
                    builder.Append("- ").Append(column.Name)
                        .Append("（").Append(column.InferredType).Append("）")
                        .Append(" 非空 ").Append(column.NonEmptyCount)
                        .Append("，去重 ").Append(column.DistinctCount);
                    if (column.HasNumericStats)
                    {
                        builder.Append("，min ").Append(column.MinText)
                            .Append("，max ").Append(column.MaxText)
                            .Append("，sum ").Append(column.Sum)
                            .Append("，avg ").Append(column.Average);
                    }

                    builder.AppendLine();
                    if (column.TopValues != null && column.TopValues.Count > 0)
                    {
                        builder.Append("  高频值：")
                            .AppendLine(string.Join("、", column.TopValues.Select(value =>
                                (string.IsNullOrEmpty(value.Value) ? "(空)" : value.Value)
                                + "×" + value.Count)));
                    }
                }

                if (source.SampleRows != null && source.SampleRows.Count > 0)
                {
                    builder.AppendLine("样例行：");
                    foreach (var row in source.SampleRows)
                    {
                        builder.AppendLine("  " + string.Join(" | ", row ?? new string[0]));
                    }
                }

                if (source.IsTruncated)
                {
                    builder.AppendLine("已截断：" + source.TruncationNote);
                }

                if (source.DataQualityFlags != null && source.DataQualityFlags.Count > 0)
                {
                    builder.AppendLine("数据质量标记："
                        + string.Join("、", source.DataQualityFlags.Select(flag =>
                            flag.Name + (flag.Count.HasValue ? "（" + flag.Count.Value + "）" : string.Empty))));
                }

                if (source.DataQualityNotes != null && source.DataQualityNotes.Count > 0)
                {
                    builder.AppendLine("质量检查范围说明："
                        + string.Join("；", source.DataQualityNotes));
                }
            }

            return builder.ToString();
        }

        private static string DescribeQualityFlag(string name)
        {
            switch (name)
            {
                case "subtotalFormulaRows": return "公式小计行";
                case "subtotalLabelRows": return "小计/合计标签行";
                case "hiddenOrFilteredRows": return "隐藏或筛选状态";
                case "textNumbers": return "文本型数字";
                case "mergedCells": return "合并单元格";
                case "suspiciousHeader": return "可疑表头";
                case "staleCalculation": return "计算结果可能过期";
                default: return name ?? "未知质量标记";
            }
        }

        private static string DescribeSourceKind(string sourceKind)
        {
            switch (sourceKind)
            {
                case ExcelTabularSourceKinds.ListObject: return "Excel 表";
                case ExcelTabularSourceKinds.ExplicitAddress: return "显式地址";
                case ExcelTabularSourceKinds.Selection: return "当前选区";
                case ExcelTabularSourceKinds.CurrentRegion: return "推断的当前区域";
                default: return sourceKind ?? "-";
            }
        }
    }
}
