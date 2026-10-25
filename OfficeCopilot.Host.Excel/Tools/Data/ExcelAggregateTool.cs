using System;
using System.Text;
using System.Threading.Tasks;
using OfficeCopilot.Agent.Abstractions;
using OfficeCopilot.Host.Abstractions;

namespace OfficeCopilot.Host.Excel.Tools
{
    public sealed class ExcelAggregateTool : ExcelToolBase
    {
        private static readonly ToolDefinition _definition = new ToolDefinition
        {
            Name = "excel_aggregate",
            DisplayName = "Aggregate Data",
            Description = "Groups and aggregates a data range (similar to SQL GROUP BY) entirely in memory without modifying the workbook."
                + "The result row count equals the number of groups and is independent of the source row count. Use this tool for categorization, statistics, rankings, and similar tasks."
                + " Supports unions across structurally identical tables and key-based lookups; dimension-table keys must be unique by default, and unmatched rows are reported."
                + " Never load an entire table into context for manual calculation. If results must update automatically when source data changes, use formulas or a PivotTable instead.",
            SourceKind = ToolSourceKind.BuiltIn,
            SourceName = "Excel",
            IsReadOnly = true,
            RequiresConfirmation = false,
            InputSchemaJson = "{"
                + "\"type\":\"object\","
                + "\"properties\":{"
                + "\"worksheetName\":{\"type\":\"string\"},"
                + "\"address\":{\"type\":\"string\",\"description\":\"Range address. Mutually exclusive with tableName; if both are omitted, infer from the current selection or region.\"},"
                + "\"tableName\":{\"type\":\"string\",\"description\":\"Name of the Excel table (ListObject).\"},"
                + "\"sources\":{\"type\":\"array\",\"minItems\":2,\"description\":\"Vertical union of multiple sources. Each item must have a unique alias and specify a worksheet range or an Excel table; all sources must have the same column names and order. Calculation runs once after the union and automatically adds a __source column.\",\"items\":{\"type\":\"object\",\"properties\":{"
                + "\"worksheetName\":{\"type\":\"string\"},\"address\":{\"type\":\"string\"},"
                + "\"tableName\":{\"type\":\"string\"},\"alias\":{\"type\":\"string\"}},"
                + "\"required\":[\"alias\"],"
                + "\"oneOf\":[{\"required\":[\"worksheetName\",\"address\"]},{\"required\":[\"tableName\"]}],"
                + "\"additionalProperties\":false}},"
                + "\"lookups\":{\"type\":\"array\",\"items\":{\"type\":\"object\",\"properties\":{"
                + "\"source\":{\"type\":\"object\",\"properties\":{\"worksheetName\":{\"type\":\"string\"},"
                + "\"address\":{\"type\":\"string\"},\"tableName\":{\"type\":\"string\"}},"
                + "\"oneOf\":[{\"required\":[\"worksheetName\",\"address\"]},{\"required\":[\"tableName\"]}],"
                + "\"additionalProperties\":false},"
                + "\"key\":{\"type\":\"string\",\"description\":\"Unique key column in the dimension table.\"},"
                + "\"foreignKey\":{\"type\":\"string\",\"description\":\"Related key column in the primary data.\"},"
                + "\"columns\":{\"type\":\"array\",\"minItems\":1,\"items\":{\"type\":\"string\"}},"
                + "\"join\":{\"type\":\"string\",\"enum\":[\"left\",\"inner\"],\"description\":\"Defaults to left.\"},"
                + "\"onDuplicateKey\":{\"type\":\"string\",\"enum\":[\"error\",\"first\"],\"description\":\"Defaults to error; first must be explicitly requested.\"}},"
                + "\"required\":[\"source\",\"key\",\"foreignKey\",\"columns\"],\"additionalProperties\":false}},"
                + "\"headerMode\":{\"type\":\"string\",\"enum\":[\"auto\",\"yes\",\"no\"]},"
                + "\"groupBy\":{\"type\":\"array\",\"items\":{\"type\":\"string\"},\"description\":\"Names of columns to group by. Leave empty to aggregate the entire range as one group.\"},"
                + "\"derivedGroupBy\":{\"type\":\"array\",\"description\":\"Derived grouping columns computed in memory; they are not written to the workbook and need no helper columns. Date transforms (year/quarter/month/monthName/yearMonth/day/dayOfWeek) require a date-formatted source column; for example, group by birth month with {column:'Birth Date',transform:'month'}. numberBucket divides values into equal-width buckets using size. prefix takes the first size characters.\",\"items\":{"
                + "\"type\":\"object\",\"properties\":{"
                + "\"column\":{\"type\":\"string\"},"
                + "\"transform\":{\"type\":\"string\",\"enum\":[\"firstCharacter\",\"prefix\",\"year\",\"quarter\",\"month\",\"monthName\",\"yearMonth\",\"day\",\"dayOfWeek\",\"numberBucket\"]},"
                + "\"alias\":{\"type\":\"string\"},"
                + "\"size\":{\"type\":\"number\",\"description\":\"Character count for prefix or bucket width for numberBucket; ignored by other transforms.\"}"
                + "},\"required\":[\"column\",\"transform\"]}},"
                + "\"measures\":{\"type\":\"array\",\"description\":\"Aggregated measures; at least one is required.\",\"items\":{"
                + "\"type\":\"object\","
                + "\"properties\":{"
                + "\"column\":{\"type\":\"string\"},"
                + "\"function\":{\"type\":\"string\",\"enum\":[\"sum\",\"count\",\"countDistinct\",\"errorCount\",\"average\",\"min\",\"max\",\"first\",\"last\"]},"
                + "\"alias\":{\"type\":\"string\"}"
                + "},\"required\":[\"column\",\"function\"]}},"
                + "\"filters\":{\"type\":\"array\",\"description\":\"Row filters combined with AND. isNativeNumber and isTextNumber can separately select native numbers and parseable text numbers; neither requires value.\",\"items\":{"
                + "\"type\":\"object\","
                + "\"properties\":{"
                + "\"column\":{\"type\":\"string\"},"
                + "\"operator\":{\"type\":\"string\",\"enum\":[\"eq\",\"ne\",\"gt\",\"ge\",\"lt\",\"le\",\"contains\",\"startsWith\",\"in\",\"isEmpty\",\"notEmpty\",\"isNativeNumber\",\"isTextNumber\"]},"
                + "\"value\":{\"type\":\"string\",\"description\":\"For the in operator, separate multiple values with commas.\"}"
                + "},\"required\":[\"column\",\"operator\"]}},"
                + "\"orderBy\":{\"type\":\"array\",\"items\":{"
                + "\"type\":\"object\","
                + "\"properties\":{"
                + "\"column\":{\"type\":\"string\",\"description\":\"Group column name or measure alias.\"},"
                + "\"descending\":{\"type\":\"boolean\"}"
                + "},\"required\":[\"column\"]}},"
                + "\"maxGroups\":{\"type\":\"integer\",\"minimum\":1,\"description\":\"Maximum number of groups to return (default: 200).\"}"
                + "},"
                + "\"required\":[\"measures\"],"
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
                var arguments = ExcelToolJsonSerializer.Deserialize<ExcelAggregateRequest>(
                    request == null ? null : request.ArgumentsJson);

                var result = host.Aggregate(arguments);
                var content = BuildContent(result);
                var structuredContentJson = ExcelToolJsonSerializer.Serialize(result);
                return Task.FromResult(CreateSuccess(request, content, structuredContentJson));
            }
            catch (Exception ex)
            {
                return Task.FromResult(CreateFailure(request, ex, "excel.aggregate_failed"));
            }
        }

        private static string BuildContent(ExcelAggregateResult result)
        {
            var builder = new StringBuilder();
            builder.AppendLine("工作簿：" + (result.WorkbookName ?? "-"));
            if (string.Equals(result.SourceKind, "union", StringComparison.OrdinalIgnoreCase))
            {
                builder.AppendLine("数据源：多表 union");
                foreach (var source in result.SourceSummaries ?? new System.Collections.Generic.List<ExcelAggregateSourceSummary>())
                {
                    builder.AppendLine("- " + source.Alias + "：" + source.WorksheetName + "!" + source.Address
                        + "（" + source.TotalRowCount + " 行）");
                }
            }
            else
            {
                builder.AppendLine("数据源：" + (result.WorksheetName ?? "-") + "!" + (result.Address ?? "-")
                    + "（来源：" + (result.SourceKind ?? "-") + "）");
            }

            builder.AppendLine("源数据 " + result.TotalRowCount + " 行，参与聚合 " + result.FilteredRowCount
                + " 行，跳过 " + result.SkippedRowCount + " 行，分组数 " + result.GroupCount + "。");
            foreach (var lookup in result.LookupSummaries ?? new System.Collections.Generic.List<ExcelAggregateLookupSummary>())
            {
                builder.AppendLine("lookup：" + lookup.WorksheetName + "!" + lookup.Address
                    + (string.IsNullOrWhiteSpace(lookup.TableName) ? string.Empty : "（表 " + lookup.TableName + "）")
                    + "；键 " + lookup.ForeignKey + " → " + lookup.Key
                    + "，输出列=" + string.Join("、", lookup.Columns ?? new System.Collections.Generic.List<string>())
                    + "；join=" + lookup.Join
                    + "；重复键策略=" + lookup.OnDuplicateKey
                    + "；重复键=" + lookup.DuplicateKeyCount
                    + "；未匹配主行=" + lookup.UnmatchedRowCount
                    + "；inner 丢弃=" + lookup.DroppedRowCount);
            }

            builder.AppendLine(ExcelAggregationService.RenderCoverage(string.Empty, result.Coverage));

            if (result.IsTruncated)
            {
                builder.AppendLine("结果已截断，仅返回前 " + result.Rows.Count + " 组。");
            }

            builder.AppendLine();
            builder.AppendLine(ExcelAggregationService.RenderTable(result.Columns, result.Rows, 100));
            var verification = ExcelAggregationService.RenderVerification(result.Verification);
            if (!string.IsNullOrWhiteSpace(verification))
            {
                builder.AppendLine();
                builder.Append(verification);
            }

            if (result.Warnings.Count > 0)
            {
                builder.AppendLine("提示：");
                foreach (var warning in result.Warnings)
                {
                    builder.AppendLine("- " + warning);
                }
            }

            return builder.ToString();
        }
    }
}
