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
            DisplayName = "分组汇总",
            Description = "对数据区做分组聚合（相当于 SQL 的 GROUP BY），在内存中一次算完，不修改工作簿。"
                + "返回行数等于分组数，与源数据行数无关。分类汇总、统计、排名等需求应使用本工具，"
                + "支持同构多表 union 和按键 lookup；维表键默认要求唯一并报告未匹配行。"
                + "严禁把整表数据读进上下文自行计算。若用户需要结果随源数据自动更新，则改用公式或数据透视表。",
            SourceKind = ToolSourceKind.BuiltIn,
            SourceName = "Excel",
            IsReadOnly = true,
            RequiresConfirmation = false,
            InputSchemaJson = "{"
                + "\"type\":\"object\","
                + "\"properties\":{"
                + "\"worksheetName\":{\"type\":\"string\"},"
                + "\"address\":{\"type\":\"string\",\"description\":\"区域地址。与 tableName 二选一；都不给时按选区或当前区域推断。\"},"
                + "\"tableName\":{\"type\":\"string\",\"description\":\"Excel 表（ListObject）名称。\"},"
                + "\"sources\":{\"type\":\"array\",\"minItems\":2,\"description\":\"多源纵向合并。每项必须有唯一 alias，并指定工作表区域或 Excel 表；所有来源必须具有相同列名和顺序。计算在合并后一次完成，自动添加 __source 列。\",\"items\":{\"type\":\"object\",\"properties\":{"
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
                + "\"key\":{\"type\":\"string\",\"description\":\"维表中的唯一键列。\"},"
                + "\"foreignKey\":{\"type\":\"string\",\"description\":\"主数据中的关联键列。\"},"
                + "\"columns\":{\"type\":\"array\",\"minItems\":1,\"items\":{\"type\":\"string\"}},"
                + "\"join\":{\"type\":\"string\",\"enum\":[\"left\",\"inner\"],\"description\":\"默认 left。\"},"
                + "\"onDuplicateKey\":{\"type\":\"string\",\"enum\":[\"error\",\"first\"],\"description\":\"默认 error；first 必须显式指定。\"}},"
                + "\"required\":[\"source\",\"key\",\"foreignKey\",\"columns\"],\"additionalProperties\":false}},"
                + "\"headerMode\":{\"type\":\"string\",\"enum\":[\"auto\",\"yes\",\"no\"]},"
                + "\"groupBy\":{\"type\":\"array\",\"items\":{\"type\":\"string\"},\"description\":\"分组列名。留空表示对整个区域做全局聚合。\"},"
                + "\"derivedGroupBy\":{\"type\":\"array\",\"description\":\"内存中的派生分组列；不会写入工作簿，也不需要辅助列。日期类 transform（year/quarter/month/monthName/yearMonth/day/dayOfWeek）要求源列是日期格式列，例如按出生月份分组用 {column:'生日',transform:'month'}。numberBucket 按 size 指定的桶宽等距分桶。prefix 取前 size 个字符。\",\"items\":{"
                + "\"type\":\"object\",\"properties\":{"
                + "\"column\":{\"type\":\"string\"},"
                + "\"transform\":{\"type\":\"string\",\"enum\":[\"firstCharacter\",\"prefix\",\"year\",\"quarter\",\"month\",\"monthName\",\"yearMonth\",\"day\",\"dayOfWeek\",\"numberBucket\"]},"
                + "\"alias\":{\"type\":\"string\"},"
                + "\"size\":{\"type\":\"number\",\"description\":\"prefix 的字符数、numberBucket 的桶宽；其他 transform 忽略。\"}"
                + "},\"required\":[\"column\",\"transform\"]}},"
                + "\"measures\":{\"type\":\"array\",\"description\":\"聚合度量，至少一个。\",\"items\":{"
                + "\"type\":\"object\","
                + "\"properties\":{"
                + "\"column\":{\"type\":\"string\"},"
                + "\"function\":{\"type\":\"string\",\"enum\":[\"sum\",\"count\",\"countDistinct\",\"errorCount\",\"average\",\"min\",\"max\",\"first\",\"last\"]},"
                + "\"alias\":{\"type\":\"string\"}"
                + "},\"required\":[\"column\",\"function\"]}},"
                + "\"filters\":{\"type\":\"array\",\"description\":\"行过滤条件，多个之间是 AND 关系。isNativeNumber 与 isTextNumber 可分别筛选原生数值和可解析的文本数字，且不需要 value。\",\"items\":{"
                + "\"type\":\"object\","
                + "\"properties\":{"
                + "\"column\":{\"type\":\"string\"},"
                + "\"operator\":{\"type\":\"string\",\"enum\":[\"eq\",\"ne\",\"gt\",\"ge\",\"lt\",\"le\",\"contains\",\"startsWith\",\"in\",\"isEmpty\",\"notEmpty\",\"isNativeNumber\",\"isTextNumber\"]},"
                + "\"value\":{\"type\":\"string\",\"description\":\"in 运算符用逗号分隔多个取值。\"}"
                + "},\"required\":[\"column\",\"operator\"]}},"
                + "\"orderBy\":{\"type\":\"array\",\"items\":{"
                + "\"type\":\"object\","
                + "\"properties\":{"
                + "\"column\":{\"type\":\"string\",\"description\":\"分组列名或度量别名。\"},"
                + "\"descending\":{\"type\":\"boolean\"}"
                + "},\"required\":[\"column\"]}},"
                + "\"maxGroups\":{\"type\":\"integer\",\"minimum\":1,\"description\":\"最多返回的分组数，默认 200。\"}"
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
