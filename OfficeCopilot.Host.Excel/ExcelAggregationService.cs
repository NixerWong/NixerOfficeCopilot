using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using OfficeCopilot.Host.Abstractions;
using OfficeCopilot.Host.Excel.Analysis;
using OfficeCopilot.Host.Excel.Analysis.Accumulators;
using OfficeCopilot.Host.Excel.Com;
using OfficeCopilot.Host.Excel.Verification;
using ExcelInterop = Microsoft.Office.Interop.Excel;

namespace OfficeCopilot.Host.Excel
{
    /// <summary>
    /// 表结构画像、分组聚合与对账。取数按列投影并分块读取，
    /// 计算全部委托给流式累加器，避免逐格 COM 往返和整区快照占用。
    /// </summary>
    internal sealed class ExcelAggregationService : ExcelServiceBase
    {
        public ExcelAggregationService(ExcelInterop.Application application)
            : base(application)
        {
        }

        internal CalcCostEstimate EstimateCalcCost(CalcQuery query)
        {
            if (VerificationPlanner.IsMultiSourceQuery(query))
            {
                string reason;
                if (!VerificationPlanner.ValidateMultiSourceQuery(query, out reason))
                {
                    throw new InvalidOperationException(reason);
                }

                var totalRows = 0;
                var totalProjectedCells = 0L;
                int? readColumnCount = null;
                foreach (var source in query.Sources)
                {
                    var sourceQuery = VerificationPlanner.CreateSingleSourceQuery(query, source);
                    var estimate = EstimateCalcCost(sourceQuery);
                    if (readColumnCount.HasValue && readColumnCount.Value != estimate.ReadColumnCount)
                    {
                        throw new InvalidOperationException(
                            "多源守恒读取的投影列数不一致；请检查各来源结构和表头。");
                    }

                    readColumnCount = estimate.ReadColumnCount;
                    totalRows = checked(totalRows + estimate.RowCount);
                    totalProjectedCells = checked(totalProjectedCells + estimate.ProjectedCellCount);
                }

                return new CalcCostEstimate
                {
                    RowCount = totalRows,
                    ReadColumnCount = readColumnCount ?? 0,
                    ProjectedCellCount = totalProjectedCells
                };
            }

            if (query == null || query.Source == null)
            {
                throw new InvalidOperationException("验证查询缺少数据源。");
            }

            var requestedColumns = new List<string>();
            var seenColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (query.Columns != null)
            {
                foreach (var column in query.Columns)
                {
                    AddProjectedColumn(requestedColumns, seenColumns, column);
                }
            }

            if (query.Filters != null)
            {
                foreach (var filter in query.Filters)
                {
                    if (filter != null)
                    {
                        AddProjectedColumn(requestedColumns, seenColumns, filter.Column);
                    }
                }
            }

            AddProjectedColumn(requestedColumns, seenColumns, query.GroupByColumn);

            ExcelInterop.Range range = null;
            try
            {
                string sourceKind;
                string tableName;
                string worksheetName;
                range = ResolveSource(
                    query.Source,
                    out sourceKind,
                    out tableName,
                    out worksheetName);
                var layout = new ExcelColumnReader(_application).ResolveLayout(
                    range,
                    new ExcelColumnReadRequest
                    {
                        Columns = requestedColumns,
                        HeaderMode = ResolveHeaderMode(query.HeaderMode, sourceKind),
                        IncludeAllColumns = query.ExcludeLabelRows
                    });

                return new CalcCostEstimate
                {
                    RowCount = layout.TotalRows,
                    ReadColumnCount = layout.Columns.Length,
                    ProjectedCellCount = (long)layout.TotalRows * layout.Columns.Length
                };
            }
            finally
            {
                ReleaseComObject(range);
            }
        }

        private static void AddProjectedColumn(
            IList<string> columns,
            ISet<string> seenColumns,
            string column)
        {
            if (!string.IsNullOrWhiteSpace(column) && seenColumns.Add(column.Trim()))
            {
                columns.Add(column.Trim());
            }
        }

        public static string RenderCoverage(string label, ExcelCoverageReport coverage)
        {
            if (coverage == null)
            {
                return (label ?? string.Empty) + "覆盖报告不可用。";
            }

            var cellCounts = !coverage.CellCountsAvailable.HasValue
                ? "单元格分类计数未知"
                : coverage.CellCountsAvailable.Value
                    ? "读取列非原生数值 " + coverage.NonNumericCount
                        + "（文本型数字 " + coverage.TextNumberCount + "，错误值 " + coverage.ErrorCount + "），"
                        + "空白 " + coverage.BlankCount
                    : "单元格分类计数未采集";

            return (label ?? string.Empty)
                + "覆盖：已扫描 " + coverage.ScannedRows + "/" + coverage.TotalRows + " 数据行；"
                + cellCounts
                + "；后端 " + (coverage.Backend ?? "unknown") + "。";
        }

        public static string RenderVerification(ExcelVerificationResult verification)
        {
            if (verification == null)
            {
                return string.Empty;
            }

            var builder = new StringBuilder();
            builder.AppendLine(
                "自动交叉复核：" + (verification.Passed ? "通过" : "未通过")
                + "（可信度 Tier " + (verification.Tier ?? "C")
                + "；断言 " + verification.AssertionCount
                + "；覆盖行数 " + verification.CoveredRows
                + "；producer " + (verification.Producer ?? "unknown")
                + "；独立 " + (verification.Independent ? "是" : "否")
                + "；抽样 " + (verification.Sampled ? "是" : "否") + "）。");
            if (verification.Details != null)
            {
                foreach (var item in verification.Details.Items ?? new List<ExcelAssertionOutcome>())
                {
                    builder.AppendLine(
                        "- " + (item.Id ?? "(未命名断言)") + "："
                        + (item.Passed ? "通过" : "未通过")
                        + "，target=" + (item.TargetBackend ?? "unknown")
                        + (string.IsNullOrWhiteSpace(item.SourceBackend)
                            ? string.Empty
                            : "，source=" + item.SourceBackend)
                        + (item.Independent ? "（独立）" : "（非独立）"));
                }

                foreach (var difference in verification.Details.Diffs ?? new List<string>())
                {
                    builder.AppendLine("- 差异：" + difference);
                }
            }

            foreach (var note in verification.Notes ?? new List<string>())
            {
                builder.AppendLine("- 说明：" + note);
            }

            return builder.ToString();
        }

        public ExcelProfileRangeResult ProfileRange(ExcelProfileRangeRequest request)
        {
            request = request ?? new ExcelProfileRangeRequest();
            if (request.Sources != null && request.Sources.Count > 0)
            {
                return ProfileMultipleRanges(request);
            }

            return ProfileSingleRange(request);
        }

        private ExcelProfileRangeResult ProfileMultipleRanges(ExcelProfileRangeRequest request)
        {
            if (!string.IsNullOrWhiteSpace(request.WorksheetName)
                || !string.IsNullOrWhiteSpace(request.Address)
                || !string.IsNullOrWhiteSpace(request.TableName))
            {
                throw new InvalidOperationException(
                    "profile_range 使用 sources 时，不能同时指定顶层 worksheetName、address 或 tableName。");
            }

            var aliases = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var source in request.Sources)
            {
                if (source == null || string.IsNullOrWhiteSpace(source.Alias))
                {
                    throw new InvalidOperationException(
                        "profile_range.sources 的每个区域都必须指定唯一 alias。");
                }

                if (!aliases.Add(source.Alias.Trim()))
                {
                    throw new InvalidOperationException(
                        "profile_range.sources 的 alias 不能重复：" + source.Alias);
                }
            }

            var sourceProfiles = new List<ExcelProfileSourceResult>();
            var workbookName = string.Empty;
            foreach (var source in request.Sources)
            {
                var profile = ProfileSingleRange(new ExcelProfileRangeRequest
                {
                    WorksheetName = source.WorksheetName,
                    Address = source.Address,
                    TableName = source.TableName,
                    HeaderMode = request.HeaderMode,
                    TopK = request.TopK,
                    SampleRowCount = request.SampleRowCount
                });
                if (sourceProfiles.Count == 0)
                {
                    workbookName = profile.WorkbookName;
                }

                sourceProfiles.Add(new ExcelProfileSourceResult
                {
                    Alias = source.Alias.Trim(),
                    WorksheetName = profile.WorksheetName,
                    Address = profile.Address,
                    SourceKind = profile.SourceKind,
                    TableName = profile.TableName,
                    RowCount = profile.RowCount,
                    ColumnCount = profile.ColumnCount,
                    HeaderInferred = profile.HeaderInferred,
                    HeaderInferenceReason = profile.HeaderInferenceReason,
                    IsTruncated = profile.IsTruncated,
                    TruncationNote = profile.TruncationNote,
                    Columns = profile.Columns,
                    SampleRows = profile.SampleRows,
                    Coverage = profile.Coverage,
                    DataQualityFlags = profile.DataQualityFlags,
                    DataQualityNotes = profile.DataQualityNotes
                });
            }

            var headerConsistency = CompareProfileHeaders(sourceProfiles);
            var columnCount = headerConsistency.NamesConsistent
                ? sourceProfiles[0].ColumnCount
                : 0;
            var totalRows = sourceProfiles.Sum(source => source.RowCount);
            var aggregateCoverage = CombineProfileCoverage(sourceProfiles);
            return new ExcelProfileRangeResult
            {
                WorkbookName = workbookName,
                SourceKind = "multiSource",
                RowCount = totalRows,
                ColumnCount = columnCount,
                HeaderInferred = sourceProfiles.Any(source => source.HeaderInferred),
                HeaderInferenceReason = headerConsistency.HeaderInferenceConsistent
                    ? sourceProfiles[0].HeaderInferenceReason
                    : "不同来源的表头推断结果不一致。",
                IsTruncated = sourceProfiles.Any(source => source.IsTruncated),
                TruncationNote = string.Join("; ", sourceProfiles
                    .Where(source => source.IsTruncated)
                    .Select(source => source.Alias + ": " + source.TruncationNote)),
                Columns = new List<ExcelColumnProfile>(),
                SampleRows = new List<string[]>(),
                Coverage = aggregateCoverage,
                DataQualityFlags = new List<ExcelDataQualityFlag>(),
                DataQualityNotes = new List<string>
                {
                    "各来源的列画像、样例和数据质量标记见 sourceProfiles。"
                },
                Topology = "multiSource",
                SourceProfiles = sourceProfiles,
                HeaderConsistency = headerConsistency
            };
        }

        private ExcelProfileRangeResult ProfileSingleRange(ExcelProfileRangeRequest request)
        {

            var workbook = GetRequiredWorkbook();
            ExcelInterop.Range range = null;

            try
            {
                string sourceKind;
                string resolvedTableName;
                string worksheetName;
                range = ResolveSource(
                    workbook,
                    request.WorksheetName,
                    request.Address,
                    request.TableName,
                    out sourceKind,
                    out resolvedTableName,
                    out worksheetName);

                var sink = new ProfileChunkSink(request.TopK, request.SampleRowCount);
                var layout = new ExcelColumnReader(_application).Read(
                    range,
                    new ExcelColumnReadRequest
                    {
                        HeaderMode = ResolveHeaderMode(request.HeaderMode, sourceKind),
                        IncludeAllColumns = true
                    },
                    sink);
                var qualitySnapshot = DataQualityInspector.CreateSnapshot(sink);
                var qualityFlags = DataQualityInspector.InspectManaged(qualitySnapshot);
                var comInspection = DataQualityInspector.InspectCom(
                    _application,
                    range,
                    layout,
                    qualitySnapshot);
                qualityFlags.AddRange(comInspection.Flags);

                return new ExcelProfileRangeResult
                {
                    WorkbookName = workbook.Name ?? string.Empty,
                    WorksheetName = worksheetName,
                    Address = GetAddress(range),
                    SourceKind = sourceKind,
                    TableName = resolvedTableName,
                    RowCount = layout.TotalRows,
                    ColumnCount = layout.SourceColumnCount,
                    HeaderInferred = layout.HeaderInferred,
                    HeaderInferenceReason = layout.HeaderInferenceReason,
                    IsTruncated = false,
                    TruncationNote = string.Empty,
                    Columns = sink.Profiles,
                    SampleRows = sink.SampleRows,
                    Coverage = sink.Coverage,
                    DataQualityFlags = qualityFlags,
                    DataQualityNotes = comInspection.Notes
                };
            }
            finally
            {
                ReleaseComObject(range);
            }
        }

        internal static ExcelProfileHeaderConsistency CompareProfileHeaders(
            IList<ExcelProfileSourceResult> sourceProfiles)
        {
            var profiles = (sourceProfiles ?? new List<ExcelProfileSourceResult>())
                .Where(profile => profile != null)
                .ToList();
            var comparison = new ExcelProfileHeaderConsistency
            {
                NamesConsistent = true,
                OrderConsistent = true,
                TypesConsistent = true,
                HeaderInferenceConsistent = true
            };
            if (profiles.Count < 2)
            {
                return comparison;
            }

            var reference = profiles[0];
            var referenceColumns = reference.Columns ?? new List<ExcelColumnProfile>();
            var referenceNames = referenceColumns
                .Where(column => column != null)
                .Select(column => column.Name ?? string.Empty)
                .ToList();
            var referenceCounts = referenceNames
                .GroupBy(name => name, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);
            var referenceSet = new HashSet<string>(referenceNames, StringComparer.OrdinalIgnoreCase);
            for (var i = 1; i < profiles.Count; i++)
            {
                var current = profiles[i];
                var currentColumnsList = current.Columns ?? new List<ExcelColumnProfile>();
                var currentNames = currentColumnsList
                    .Where(column => column != null)
                    .Select(column => column.Name ?? string.Empty)
                    .ToList();
                var currentCounts = currentNames
                    .GroupBy(name => name, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);
                var currentSet = new HashSet<string>(currentNames, StringComparer.OrdinalIgnoreCase);
                var alias = string.IsNullOrWhiteSpace(current.Alias)
                    ? "source#" + (i + 1)
                    : current.Alias;

                var namesMatch = referenceSet.SetEquals(currentSet)
                    && referenceCounts.All(pair =>
                    {
                        int currentCount;
                        return currentCounts.TryGetValue(pair.Key, out currentCount)
                            && currentCount == pair.Value;
                    });
                if (!namesMatch)
                {
                    comparison.NamesConsistent = false;
                    var missing = referenceNames
                        .GroupBy(name => name, StringComparer.OrdinalIgnoreCase)
                        .Where(group => !currentCounts.ContainsKey(group.Key)
                            || currentCounts[group.Key] < group.Count())
                        .Select(group => group.Key + (currentCounts.ContainsKey(group.Key)
                            && currentCounts[group.Key] < group.Count()
                                ? " ×" + (group.Count() - currentCounts[group.Key])
                                : string.Empty))
                        .ToList();
                    var extra = currentNames
                        .GroupBy(name => name, StringComparer.OrdinalIgnoreCase)
                        .Where(group => !referenceCounts.ContainsKey(group.Key)
                            || group.Count() > referenceCounts[group.Key])
                        .Select(group => group.Key + (referenceCounts.ContainsKey(group.Key)
                            && group.Count() > referenceCounts[group.Key]
                                ? " ×" + (group.Count() - referenceCounts[group.Key])
                                : string.Empty))
                        .ToList();
                    comparison.Differences.Add(
                        alias + " 的列名集合不同；缺少 [" + string.Join("、", missing)
                        + "]，多出 [" + string.Join("、", extra) + "]。");
                }

                if (!referenceNames.SequenceEqual(currentNames, StringComparer.OrdinalIgnoreCase))
                {
                    comparison.OrderConsistent = false;
                    comparison.Differences.Add(
                        alias + " 的列顺序与 "
                        + (string.IsNullOrWhiteSpace(reference.Alias) ? "首个来源" : reference.Alias)
                        + " 不一致。");
                }

                var currentColumns = currentColumnsList
                    .Where(column => column != null)
                    .GroupBy(column => column.Name ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
                foreach (var referenceColumn in referenceColumns.Where(column => column != null))
                {
                    ExcelColumnProfile currentColumn;
                    if (currentColumns.TryGetValue(referenceColumn.Name ?? string.Empty, out currentColumn)
                        && !string.Equals(
                            referenceColumn.InferredType,
                            currentColumn.InferredType,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        comparison.TypesConsistent = false;
                        comparison.Differences.Add(
                            alias + " 的列 " + referenceColumn.Name + " 类型为 "
                            + currentColumn.InferredType + "，与基准类型 "
                            + referenceColumn.InferredType + " 不一致。");
                    }
                }

                if (reference.HeaderInferred != current.HeaderInferred)
                {
                    comparison.HeaderInferenceConsistent = false;
                    comparison.Differences.Add(alias + " 与首个来源的表头推断模式不一致。");
                }
            }

            return comparison;
        }

        private static ExcelCoverageReport CombineProfileCoverage(
            IList<ExcelProfileSourceResult> sourceProfiles)
        {
            var coverage = sourceProfiles
                .Where(source => source.Coverage != null)
                .Select(source => source.Coverage)
                .ToList();
            var allCellCountsAvailable = coverage.Count > 0
                && coverage.All(item => item.CellCountsAvailable == true);
            return new ExcelCoverageReport
            {
                TotalRows = coverage.Sum(item => item.TotalRows),
                ScannedRows = coverage.Sum(item => item.ScannedRows),
                Backend = coverage.Select(item => item.Backend)
                    .Distinct(StringComparer.OrdinalIgnoreCase).Count() == 1
                    ? coverage[0].Backend
                    : "mixed",
                CellCountsAvailable = allCellCountsAvailable,
                NonNumericCount = allCellCountsAvailable ? coverage.Sum(item => item.NonNumericCount) : 0,
                TextNumberCount = allCellCountsAvailable ? coverage.Sum(item => item.TextNumberCount) : 0,
                ErrorCount = allCellCountsAvailable ? coverage.Sum(item => item.ErrorCount) : 0,
                BlankCount = allCellCountsAvailable ? coverage.Sum(item => item.BlankCount) : 0
            };
        }

        public ExcelAggregateResult Aggregate(ExcelAggregateRequest request)
        {
            request = request ?? new ExcelAggregateRequest();

            var workbook = GetRequiredWorkbook();
            ExcelInterop.Range range = null;

            try
            {
                if (request.Sources != null && request.Sources.Count > 0)
                {
                    return AggregateUnion(workbook, request);
                }

                string sourceKind;
                string resolvedTableName;
                string worksheetName;
                range = ResolveSource(
                    workbook,
                    request.WorksheetName,
                    request.Address,
                    request.TableName,
                    out sourceKind,
                    out resolvedTableName,
                    out worksheetName);

                var result = new ExcelAggregateResult
                {
                    WorkbookName = workbook.Name ?? string.Empty,
                    WorksheetName = worksheetName,
                    Address = GetAddress(range),
                    SourceKind = sourceKind,
                    TableName = resolvedTableName
                };

                var lookups = PrepareLookups(workbook, request, result);
                var sink = new AggregateChunkSink(request, result, lookups);
                var layout = new ExcelColumnReader(_application).Read(
                    range,
                    new ExcelColumnReadRequest
                    {
                        HeaderMode = ResolveHeaderMode(request.HeaderMode, sourceKind),
                        IncludeAllColumns = true
                    },
                    sink);

                if (layout.HeaderInferred)
                {
                    result.Warnings.Add("表头为自动推断：" + layout.HeaderInferenceReason);
                }

                return result;
            }
            finally
            {
                ReleaseComObject(range);
            }
        }

        private ExcelAggregateResult AggregateUnion(
            ExcelInterop.Workbook workbook,
            ExcelAggregateRequest request)
        {
            if (request.Sources.Count < 2)
            {
                throw new InvalidOperationException("多源 union 至少需要两个 sources。");
            }

            if (!string.IsNullOrWhiteSpace(request.WorksheetName)
                || !string.IsNullOrWhiteSpace(request.Address)
                || !string.IsNullOrWhiteSpace(request.TableName))
            {
                throw new InvalidOperationException(
                    "sources 与顶层 worksheetName/address/tableName 不能同时提供；请只使用其中一种数据源形式。");
            }

            if ((request.DerivedGroupBy ?? new List<ExcelAggregateDerivedGroup>())
                .Any(group => group != null
                    && string.Equals(
                        (group.Alias ?? string.Empty).Trim(),
                        "__source",
                        StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidOperationException("派生分组 alias 不能与自动来源列 __source 重名。");
            }

            var aliases = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var contexts = new List<AggregateUnionSourceContext>();
            var reader = new ExcelColumnReader(_application);
            var totalRows = 0;
            string[] canonicalHeaders = null;
            ExcelInterop.Range sourceRange = null;

            try
            {
                foreach (var source in request.Sources)
                {
                    if (source == null || string.IsNullOrWhiteSpace(source.Alias))
                    {
                        throw new InvalidOperationException("每个 sources 项都必须提供非空 alias。");
                    }

                    var alias = source.Alias.Trim();
                    if (!aliases.Add(alias))
                    {
                        throw new InvalidOperationException("sources.alias 必须唯一；重复值：" + alias);
                    }

                    var hasAddress = !string.IsNullOrWhiteSpace(source.Address);
                    var hasTableName = !string.IsNullOrWhiteSpace(source.TableName);
                    if (hasAddress == hasTableName)
                    {
                        throw new InvalidOperationException(
                            "sources 项 \"" + alias + "\" 必须且只能指定 address 或 tableName。");
                    }

                    if (hasAddress && string.IsNullOrWhiteSpace(source.WorksheetName))
                    {
                        throw new InvalidOperationException(
                            "sources 项 \"" + alias + "\" 使用 address 时必须指定 worksheetName。");
                    }

                    string sourceKind;
                    string tableName;
                    string worksheetName;
                    sourceRange = ResolveSource(
                        workbook,
                        source.WorksheetName,
                        source.Address,
                        source.TableName,
                        out sourceKind,
                        out tableName,
                        out worksheetName);

                    var readRequest = new ExcelColumnReadRequest
                    {
                        HeaderMode = ResolveHeaderMode(request.HeaderMode, sourceKind),
                        IncludeAllColumns = true
                    };
                    var layout = reader.ResolveLayout(sourceRange, readRequest);
                    if (canonicalHeaders == null)
                    {
                        canonicalHeaders = layout.Columns.ToArray();
                        if (canonicalHeaders.Any(header => string.Equals(
                            header,
                            "__source",
                            StringComparison.OrdinalIgnoreCase)))
                        {
                            throw new InvalidOperationException(
                                "来源数据已包含保留列名 __source；请重命名该列后再执行 union。");
                        }
                    }
                    else if (!HaveSameHeaders(canonicalHeaders, layout.Columns))
                    {
                        throw new InvalidOperationException(
                            "sources 中各表的列名或列顺序不一致；union 已停止，不能按位置猜测字段映射。"
                            + "首个来源列：" + string.Join("、", canonicalHeaders)
                            + "；当前来源 \"" + alias + "\" 列：" + string.Join("、", layout.Columns));
                    }

                    totalRows = checked(totalRows + layout.TotalRows);
                    contexts.Add(new AggregateUnionSourceContext
                    {
                        Alias = alias,
                        WorksheetName = worksheetName,
                        Address = GetAddress(sourceRange),
                        Range = sourceRange,
                        Layout = layout,
                        ReadRequest = readRequest
                    });
                    sourceRange = null;
                }

                var result = new ExcelAggregateResult
                {
                    WorkbookName = workbook.Name ?? string.Empty,
                    WorksheetName = string.Join("、", contexts.Select(source => source.WorksheetName)),
                    SourceKind = "union"
                };
                foreach (var source in contexts)
                {
                    result.SourceSummaries.Add(new ExcelAggregateSourceSummary
                    {
                        Alias = source.Alias,
                        WorksheetName = source.WorksheetName,
                        Address = source.Address,
                        TotalRowCount = source.Layout.TotalRows
                    });
                    if (source.Layout.HeaderInferred)
                    {
                        result.Warnings.Add(
                            "来源 \"" + source.Alias + "\" 的表头为自动推断："
                            + source.Layout.HeaderInferenceReason);
                    }
                }

                var lookups = PrepareLookups(workbook, request, result);
                var sink = new AggregateUnionChunkSink(
                    request,
                    result,
                    contexts,
                    totalRows,
                    lookups);
                for (var index = 0; index < contexts.Count; index++)
                {
                    sink.SelectSource(index);
                    reader.Read(contexts[index].Range, contexts[index].ReadRequest, sink);
                }

                return result;
            }
            finally
            {
                ReleaseComObject(sourceRange);
                foreach (var source in contexts)
                {
                    ReleaseComObject(source.Range);
                }
            }
        }

        private static bool HaveSameHeaders(IList<string> expected, IList<string> actual)
        {
            if (expected == null || actual == null || expected.Count != actual.Count)
            {
                return false;
            }

            for (var index = 0; index < expected.Count; index++)
            {
                if (!string.Equals(expected[index], actual[index], StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }

            return true;
        }

        private IList<PreparedAggregateLookup> PrepareLookups(
            ExcelInterop.Workbook workbook,
            ExcelAggregateRequest request,
            ExcelAggregateResult result)
        {
            var prepared = new List<PreparedAggregateLookup>();
            var lookups = request.Lookups ?? new List<ExcelAggregateLookup>();
            foreach (var lookup in lookups)
            {
                if (lookup == null || lookup.Source == null
                    || string.IsNullOrWhiteSpace(lookup.Key)
                    || string.IsNullOrWhiteSpace(lookup.ForeignKey)
                    || lookup.Columns == null
                    || lookup.Columns.Count == 0)
                {
                    throw new InvalidOperationException(
                        "每个 lookups 项都必须指定 source、key、foreignKey 和至少一个 columns。");
                }

                var join = string.IsNullOrWhiteSpace(lookup.Join) ? "left" : lookup.Join.Trim();
                if (!string.Equals(join, "left", StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(join, "inner", StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException("lookup.join 只支持 left 或 inner。");
                }

                var duplicatePolicy = string.IsNullOrWhiteSpace(lookup.OnDuplicateKey)
                    ? "error"
                    : lookup.OnDuplicateKey.Trim();
                if (!string.Equals(duplicatePolicy, "error", StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(duplicatePolicy, "first", StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(
                        "lookup.onDuplicateKey 只支持 error 或 first。");
                }

                var columns = lookup.Columns.Select(column => (column ?? string.Empty).Trim()).ToList();
                if (columns.Any(string.IsNullOrWhiteSpace)
                    || columns.Distinct(StringComparer.OrdinalIgnoreCase).Count() != columns.Count)
                {
                    throw new InvalidOperationException(
                        "lookup.columns 不能包含空列名或重复列名。");
                }

                var hasAddress = !string.IsNullOrWhiteSpace(lookup.Source.Address);
                var hasTableName = !string.IsNullOrWhiteSpace(lookup.Source.TableName);
                if (hasAddress == hasTableName
                    || (hasAddress && string.IsNullOrWhiteSpace(lookup.Source.WorksheetName)))
                {
                    throw new InvalidOperationException(
                        "lookup.source 必须指定 worksheetName+address 或 tableName。");
                }

                ExcelInterop.Range lookupRange = null;
                try
                {
                    string sourceKind;
                    string tableName;
                    string worksheetName;
                    lookupRange = ResolveSource(
                        workbook,
                        lookup.Source.WorksheetName,
                        lookup.Source.Address,
                        lookup.Source.TableName,
                        out sourceKind,
                        out tableName,
                        out worksheetName);
                    var readRequest = new ExcelColumnReadRequest
                    {
                        Columns = new[] { lookup.Key.Trim() }.Concat(columns).ToList(),
                        HeaderMode = ResolveHeaderMode(request.HeaderMode, sourceKind)
                    };
                    var indexSink = new LookupIndexChunkSink(lookup, columns, duplicatePolicy);
                    new ExcelColumnReader(_application).Read(lookupRange, readRequest, indexSink);
                    if (indexSink.DuplicateKeyCount > 0)
                    {
                        result.Warnings.Add(
                            "lookup 维表 \"" + worksheetName + "\" 存在 "
                            + indexSink.DuplicateKeyCount + " 个重复键；已按 first 策略保留首条。");
                    }

                    var summary = new ExcelAggregateLookupSummary
                    {
                        WorksheetName = worksheetName,
                        Address = GetAddress(lookupRange),
                        TableName = tableName,
                        Key = lookup.Key.Trim(),
                        ForeignKey = lookup.ForeignKey.Trim(),
                        Columns = new List<string>(columns),
                        Join = join.ToLowerInvariant(),
                        OnDuplicateKey = duplicatePolicy.ToLowerInvariant(),
                        DuplicateKeyCount = indexSink.DuplicateKeyCount
                    };
                    result.LookupSummaries.Add(summary);
                    prepared.Add(new PreparedAggregateLookup
                    {
                        Lookup = lookup,
                        Join = summary.Join,
                        Columns = indexSink.Columns,
                        ColumnDateFormats = indexSink.ColumnDateFormats,
                        KeyDateFormatted = indexSink.KeyDateFormatted,
                        ValuesByKey = indexSink.ValuesByKey,
                        Summary = summary
                    });
                }
                finally
                {
                    ReleaseComObject(lookupRange);
                }
            }

            return prepared;
        }

        public ExcelReconcileResult Reconcile(ExcelReconcileRequest request)
        {
            request = request ?? new ExcelReconcileRequest();

            var workbook = GetRequiredWorkbook();
            ExcelInterop.Range detailRange = null;
            ExcelInterop.Range summaryRange = null;

            try
            {
                string detailKind, detailTable, detailSheet;
                detailRange = ResolveSource(
                    workbook,
                    request.DetailWorksheetName,
                    request.DetailAddress,
                    request.DetailTableName,
                    out detailKind,
                    out detailTable,
                    out detailSheet);

                string summaryKind, summaryTable, summarySheet;
                summaryRange = ResolveSource(
                    workbook,
                    request.SummaryWorksheetName,
                    request.SummaryAddress,
                    request.SummaryTableName,
                    out summaryKind,
                    out summaryTable,
                    out summarySheet);

                var result = new ExcelReconcileResult
                {
                    WorkbookName = workbook.Name ?? string.Empty,
                    DetailAddress = GetAddress(detailRange),
                    SummaryAddress = GetAddress(summaryRange)
                };

                var detailSink = new ReconcileChunkSink(
                    request.DetailGroupColumn,
                    request.DetailValueColumn,
                    "detailGroupColumn",
                    "detailValueColumn");
                new ExcelColumnReader(_application).Read(
                    detailRange,
                    new ExcelColumnReadRequest { IncludeAllColumns = true, HeaderMode = "auto" },
                    detailSink);

                var summaryGroupColumn = string.IsNullOrWhiteSpace(request.SummaryGroupColumn)
                    ? request.DetailGroupColumn
                    : request.SummaryGroupColumn;
                var summaryValueColumn = string.IsNullOrWhiteSpace(request.SummaryValueColumn)
                    ? request.DetailValueColumn
                    : request.SummaryValueColumn;
                var summarySink = new ReconcileChunkSink(
                    summaryGroupColumn,
                    summaryValueColumn,
                    "summaryGroupColumn",
                    "summaryValueColumn");
                new ExcelColumnReader(_application).Read(
                    summaryRange,
                    new ExcelColumnReadRequest { IncludeAllColumns = true, HeaderMode = "auto" },
                    summarySink);

                ExcelAggregationEngine.CompleteReconcile(
                    detailSink.Accumulator,
                    summarySink.Accumulator,
                    request,
                    result,
                    detailSink.Coverage,
                    summarySink.Coverage);
                return result;
            }
            finally
            {
                ReleaseComObject(summaryRange);
                ReleaseComObject(detailRange);
            }
        }

        internal sealed class ProfileChunkSink : IColumnChunkSink
        {
            private readonly int _topK;
            private readonly int _sampleRowLimit;
            private ExcelColumnReadLayout _layout;
            private ColumnProfileAccumulator[] _profiles;
            private CoverageAccumulator _coverage;
            private int _nextRowIndex;
            private readonly HashSet<int> _subtotalLabelRows = new HashSet<int>();
            private int _firstDataRowNonEmptyCount;
            private int _firstDataRowTextCount;
            private int _laterDataRowsNonEmptyCount;
            private int _laterDataRowsNumericCount;

            public ProfileChunkSink(int topK, int sampleRowCount)
            {
                _topK = topK;
                _sampleRowLimit = sampleRowCount > 0 ? sampleRowCount : 5;
                Profiles = new List<ExcelColumnProfile>();
                SampleRows = new List<string[]>();
                Coverage = new ExcelCoverageReport();
            }

            public List<ExcelColumnProfile> Profiles { get; private set; }

            public List<string[]> SampleRows { get; private set; }

            public ExcelCoverageReport Coverage { get; private set; }

            internal ExcelColumnReadLayout Layout
            {
                get { return _layout; }
            }

            internal ColumnProfileAccumulator[] ColumnAccumulators
            {
                get { return _profiles; }
            }

            internal IList<int> SubtotalLabelRows
            {
                get { return _subtotalLabelRows.OrderBy(row => row).ToList(); }
            }

            internal int FirstDataRowNonEmptyCount
            {
                get { return _firstDataRowNonEmptyCount; }
            }

            internal int FirstDataRowTextCount
            {
                get { return _firstDataRowTextCount; }
            }

            internal int LaterDataRowsNonEmptyCount
            {
                get { return _laterDataRowsNonEmptyCount; }
            }

            internal int LaterDataRowsNumericCount
            {
                get { return _laterDataRowsNumericCount; }
            }

            public void Begin(ExcelColumnReadLayout layout)
            {
                _layout = layout;
                _profiles = new ColumnProfileAccumulator[layout.Columns.Length];
                for (var column = 0; column < layout.Columns.Length; column++)
                {
                    _profiles[column] = new ColumnProfileAccumulator(
                        layout.ColumnIsDateFormatted[column]);
                }

                _coverage = new CoverageAccumulator();
                _nextRowIndex = 0;
                _subtotalLabelRows.Clear();
                _firstDataRowNonEmptyCount = 0;
                _firstDataRowTextCount = 0;
                _laterDataRowsNonEmptyCount = 0;
                _laterDataRowsNumericCount = 0;
            }

            public void Accept(int firstRowIndex, object[][] columns)
            {
                if (firstRowIndex != _nextRowIndex
                    || columns == null
                    || columns.Length != _layout.Columns.Length)
                {
                    throw new InvalidOperationException("画像分块行偏移或列数不连续，拒绝返回不完整结果。");
                }

                var rowCount = columns.Length == 0 ? 0 : columns[0].Length;
                for (var column = 0; column < columns.Length; column++)
                {
                    if (columns[column] == null || columns[column].Length != rowCount)
                    {
                        throw new InvalidOperationException("画像分块列长度不一致，拒绝返回不完整结果。");
                    }
                }

                for (var row = 0; row < rowCount; row++)
                {
                    var values = new object[columns.Length];
                    var dataRowIndex = firstRowIndex + row;
                    var worksheetRow = _layout.SourceFirstRow
                        + (_layout.HasHeader ? 1 : 0)
                        + dataRowIndex;
                    var renderedValues = row + firstRowIndex < _sampleRowLimit
                        ? new string[columns.Length]
                        : null;
                    var containsSubtotalLabel = false;
                    for (var column = 0; column < columns.Length; column++)
                    {
                        var value = columns[column][row];
                        values[column] = value;
                        _profiles[column].Accept(value, worksheetRow);
                        if (DataQualityInspector.IsSubtotalLabel(value))
                        {
                            containsSubtotalLabel = true;
                        }

                        if (!ExcelValueSemantics.IsEmpty(value))
                        {
                            if (dataRowIndex == 0)
                            {
                                _firstDataRowNonEmptyCount++;
                                if (value is string)
                                {
                                    _firstDataRowTextCount++;
                                }
                            }
                            else
                            {
                                _laterDataRowsNonEmptyCount++;
                                double number;
                                if (!ExcelValueSemantics.IsError(value)
                                    && ExcelValueSemantics.TryGetNumber(value, out number))
                                {
                                    _laterDataRowsNumericCount++;
                                }
                            }
                        }

                        if (renderedValues != null)
                        {
                            renderedValues[column] = ExcelValueSemantics.ToText(
                                value,
                                _layout.ColumnIsDateFormatted[column]);
                        }
                    }

                    if (containsSubtotalLabel)
                    {
                        _subtotalLabelRows.Add(worksheetRow);
                    }

                    _coverage.Accept(values, values.Length);
                    if (renderedValues != null)
                    {
                        SampleRows.Add(renderedValues);
                    }
                }

                _nextRowIndex += rowCount;
            }

            public void End()
            {
                Coverage = _coverage.ToReport(_layout.TotalRows);
                for (var column = 0; column < _profiles.Length; column++)
                {
                    Profiles.Add(_profiles[column].ToProfile(
                        _layout.SourceColumnIndexes[column] + 1,
                        _layout.Columns[column],
                        _topK));
                }
            }
        }

        internal static AggregateRowSchema CreateAggregateRowSchema(
            string[] primaryHeaders,
            bool[] primaryDateFormats,
            IList<PreparedAggregateLookup> lookups,
            bool includeSource)
        {
            var headers = (primaryHeaders ?? new string[0]).ToList();
            var dateFormats = (primaryDateFormats ?? new bool[0]).ToList();
            var usedHeaders = new HashSet<string>(headers, StringComparer.OrdinalIgnoreCase);
            var bindings = new List<AggregateLookupBinding>();
            var primarySchema = new ExcelTabularSnapshot(
                primaryHeaders,
                new List<object[]>(),
                primaryDateFormats);

            foreach (var lookup in lookups ?? new List<PreparedAggregateLookup>())
            {
                var foreignKeyIndex = primarySchema.IndexOfColumn(lookup.Lookup.ForeignKey);
                if (foreignKeyIndex < 0)
                {
                    throw new InvalidOperationException(
                        "lookup.foreignKey 列 \"" + lookup.Lookup.ForeignKey
                        + "\" 不存在于主数据中。可用列：" + string.Join("、", primaryHeaders));
                }

                if (primaryDateFormats[foreignKeyIndex] != lookup.KeyDateFormatted)
                {
                    throw new InvalidOperationException(
                        "主数据 foreignKey 与维表 key 的日期格式类型不一致；拒绝把日期序列与普通数字静默匹配。");
                }

                var binding = new AggregateLookupBinding
                {
                    Lookup = lookup,
                    ForeignKeyIndex = foreignKeyIndex,
                    OutputStartIndex = headers.Count
                };
                foreach (var column in lookup.Columns)
                {
                    if (!usedHeaders.Add(column))
                    {
                        throw new InvalidOperationException(
                            "lookup 输出列 \"" + column + "\" 与主数据列或其他 lookup 输出列重名。");
                    }

                    headers.Add(column);
                    dateFormats.Add(lookup.ColumnDateFormats[Array.IndexOf(lookup.Columns, column)]);
                }

                bindings.Add(binding);
            }

            if (includeSource)
            {
                if (!usedHeaders.Add("__source"))
                {
                    throw new InvalidOperationException("来源数据包含保留列名 __source。");
                }

                headers.Add("__source");
                dateFormats.Add(false);
            }

            return new AggregateRowSchema
            {
                Headers = headers.ToArray(),
                DateFormats = dateFormats.ToArray(),
                Lookups = bindings,
                IncludesSource = includeSource
            };
        }

        internal sealed class LookupIndexChunkSink : IColumnChunkSink
        {
            private readonly ExcelAggregateLookup _lookup;
            private readonly IList<string> _requestedColumns;
            private readonly string _duplicatePolicy;
            private int _keyIndex;
            private int[] _columnIndexes;
            private bool[] _columnDateFormats;
            private bool _keyDateFormatted;
            private int _nextRowIndex;
            private int _expectedRows;

            public LookupIndexChunkSink(
                ExcelAggregateLookup lookup,
                IList<string> requestedColumns,
                string duplicatePolicy)
            {
                _lookup = lookup;
                _requestedColumns = requestedColumns;
                _duplicatePolicy = duplicatePolicy;
                ValuesByKey = new Dictionary<string, object[]>(StringComparer.OrdinalIgnoreCase);
            }

            public IDictionary<string, object[]> ValuesByKey { get; private set; }

            public int DuplicateKeyCount { get; private set; }

            public string[] Columns { get; private set; }

            public bool[] ColumnDateFormats
            {
                get { return _columnDateFormats; }
            }

            public bool KeyDateFormatted
            {
                get { return _keyDateFormatted; }
            }

            public void Begin(ExcelColumnReadLayout layout)
            {
                var schema = new ExcelTabularSnapshot(
                    layout.Columns,
                    new List<object[]>(),
                    layout.ColumnIsDateFormatted);
                _keyIndex = schema.IndexOfColumn(_lookup.Key);
                if (_keyIndex < 0)
                {
                    throw new InvalidOperationException(
                        "lookup.key 列 \"" + _lookup.Key + "\" 不存在。可用列："
                        + string.Join("、", layout.Columns));
                }

                _columnIndexes = _requestedColumns.Select(column => schema.IndexOfColumn(column)).ToArray();
                if (_columnIndexes.Any(index => index < 0))
                {
                    throw new InvalidOperationException("lookup.columns 中存在维表没有的列。");
                }

                Columns = _columnIndexes.Select(index => layout.Columns[index]).ToArray();
                _columnDateFormats = _columnIndexes
                    .Select(index => layout.ColumnIsDateFormatted[index])
                    .ToArray();
                _keyDateFormatted = layout.ColumnIsDateFormatted[_keyIndex];
                _expectedRows = layout.TotalRows;
                _nextRowIndex = 0;
            }

            public void Accept(int firstRowIndex, object[][] columns)
            {
                if (firstRowIndex != _nextRowIndex
                    || columns == null
                    || columns.Length == 0)
                {
                    throw new InvalidOperationException("lookup 分块行偏移不连续或缺少键列，拒绝生成索引。");
                }

                var rowCount = columns[0] == null ? -1 : columns[0].Length;
                if (rowCount < 0 || columns.Any(column => column == null || column.Length != rowCount))
                {
                    throw new InvalidOperationException("lookup 分块列长度不一致，拒绝生成索引。");
                }

                for (var row = 0; row < rowCount; row++)
                {
                    var rawKey = columns[_keyIndex][row];
                    if (ExcelValueSemantics.IsEmpty(rawKey))
                    {
                        continue;
                    }

                    var key = ExcelValueSemantics.ToText(rawKey, _keyDateFormatted);
                    object[] existing;
                    if (ValuesByKey.TryGetValue(key, out existing))
                    {
                        DuplicateKeyCount++;
                        if (!string.Equals(_duplicatePolicy, "first", StringComparison.OrdinalIgnoreCase))
                        {
                            throw new InvalidOperationException(
                                "lookup 维表键不唯一；默认策略为 error。若确认保留首条，请显式设置 onDuplicateKey=first。");
                        }

                        continue;
                    }

                    var values = new object[_columnIndexes.Length];
                    for (var column = 0; column < _columnIndexes.Length; column++)
                    {
                        values[column] = columns[_columnIndexes[column]][row];
                    }

                    ValuesByKey.Add(key, values);
                }

                _nextRowIndex += rowCount;
            }

            public void End()
            {
                if (_nextRowIndex != _expectedRows)
                {
                    throw new InvalidOperationException("lookup 维表读取不完整，拒绝执行关联。");
                }
            }
        }

        internal sealed class PreparedAggregateLookup
        {
            public ExcelAggregateLookup Lookup { get; set; }

            public string Join { get; set; }

            public string[] Columns { get; set; }

            public bool[] ColumnDateFormats { get; set; }

            public bool KeyDateFormatted { get; set; }

            public IDictionary<string, object[]> ValuesByKey { get; set; }

            public ExcelAggregateLookupSummary Summary { get; set; }
        }

        internal sealed class AggregateLookupBinding
        {
            public PreparedAggregateLookup Lookup { get; set; }

            public int ForeignKeyIndex { get; set; }

            public int OutputStartIndex { get; set; }
        }

        internal sealed class AggregateRowSchema
        {
            public string[] Headers { get; set; }

            public bool[] DateFormats { get; set; }

            public IList<AggregateLookupBinding> Lookups { get; set; }

            public bool IncludesSource { get; set; }

            public object[] CreateRow(
                object[] primaryValues,
                string sourceAlias,
                bool primaryRowIsEmpty,
                out bool includeInAggregation)
            {
                var values = new object[Headers.Length];
                Array.Copy(primaryValues, values, Math.Min(primaryValues.Length, values.Length));
                includeInAggregation = true;

                foreach (var binding in Lookups)
                {
                    var rawKey = primaryValues[binding.ForeignKeyIndex];
                    var key = ExcelValueSemantics.IsEmpty(rawKey)
                        ? string.Empty
                        : ExcelValueSemantics.ToText(
                            rawKey,
                            DateFormats[binding.ForeignKeyIndex]);
                    object[] lookupValues;
                    if (key.Length == 0 || !binding.Lookup.ValuesByKey.TryGetValue(key, out lookupValues))
                    {
                        binding.Lookup.Summary.UnmatchedRowCount++;
                        if (string.Equals(binding.Lookup.Join, "inner", StringComparison.Ordinal))
                        {
                            binding.Lookup.Summary.DroppedRowCount++;
                            includeInAggregation = false;
                        }

                        continue;
                    }

                    Array.Copy(
                        lookupValues,
                        0,
                        values,
                        binding.OutputStartIndex,
                        lookupValues.Length);
                }

                if (IncludesSource)
                {
                    values[values.Length - 1] = primaryRowIsEmpty ? null : sourceAlias;
                }

                return values;
            }
        }

        private sealed class AggregateUnionSourceContext
        {
            public string Alias { get; set; }

            public string WorksheetName { get; set; }

            public string Address { get; set; }

            public ExcelInterop.Range Range { get; set; }

            public ExcelColumnReadLayout Layout { get; set; }

            public ExcelColumnReadRequest ReadRequest { get; set; }
        }

        private sealed class AggregateUnionChunkSink : IColumnChunkSink
        {
            private readonly ExcelAggregateRequest _request;
            private readonly ExcelAggregateResult _result;
            private readonly IList<AggregateUnionSourceContext> _sources;
            private readonly IList<PreparedAggregateLookup> _lookups;
            private readonly int _totalRows;
            private ExcelAggregationEngine.AggregateStreamAccumulator _accumulator;
            private AggregateUnionSourceContext _selectedSource;
            private AggregateRowSchema _rowSchema;
            private bool[] _canonicalDateFormats;
            private int _sourceIndex = -1;
            private int _nextRowIndex;

            public AggregateUnionChunkSink(
                ExcelAggregateRequest request,
                ExcelAggregateResult result,
                IList<AggregateUnionSourceContext> sources,
                int totalRows,
                IList<PreparedAggregateLookup> lookups)
            {
                _request = request;
                _result = result;
                _sources = sources;
                _totalRows = totalRows;
                _lookups = lookups;
            }

            public void SelectSource(int sourceIndex)
            {
                if (sourceIndex != _sourceIndex + 1 || sourceIndex < 0 || sourceIndex >= _sources.Count)
                {
                    throw new InvalidOperationException("union 来源读取顺序无效，拒绝返回不完整结果。");
                }

                _sourceIndex = sourceIndex;
                _selectedSource = _sources[sourceIndex];
            }

            public void Begin(ExcelColumnReadLayout layout)
            {
                if (_selectedSource == null
                    || !HaveSameHeaders(_selectedSource.Layout.Columns, layout.Columns)
                    || _selectedSource.Layout.TotalRows != layout.TotalRows)
                {
                    throw new InvalidOperationException(
                        "union 来源在预检后发生变化；为避免按错误列映射计算，已停止聚合。");
                }

                if (_accumulator == null)
                {
                    _canonicalDateFormats = layout.ColumnIsDateFormatted.ToArray();
                    _rowSchema = CreateAggregateRowSchema(
                        layout.Columns,
                        _canonicalDateFormats,
                        _lookups,
                        true);
                    _accumulator = ExcelAggregationEngine.CreateAggregateAccumulator(
                        _rowSchema.Headers,
                        _rowSchema.DateFormats,
                        _request,
                        _result,
                        _totalRows);
                }
                else if (!_canonicalDateFormats.SequenceEqual(layout.ColumnIsDateFormatted))
                {
                    throw new InvalidOperationException(
                        "union 来源的日期格式列不一致；为避免日期序列被误作普通数值，已停止聚合。");
                }

                _nextRowIndex = 0;
            }

            public void Accept(int firstRowIndex, object[][] columns)
            {
                if (_selectedSource == null
                    || firstRowIndex != _nextRowIndex
                    || columns == null
                    || columns.Length != _selectedSource.Layout.Columns.Length)
                {
                    throw new InvalidOperationException("union 聚合分块行偏移或列数不连续，拒绝返回不完整结果。");
                }

                var rowCount = columns.Length == 0 ? 0 : columns[0].Length;
                for (var column = 0; column < columns.Length; column++)
                {
                    if (columns[column] == null || columns[column].Length != rowCount)
                    {
                        throw new InvalidOperationException("union 聚合分块列长度不一致，拒绝返回不完整结果。");
                    }
                }

                for (var row = 0; row < rowCount; row++)
                {
                    var isEmpty = true;
                    var primaryValues = new object[columns.Length];
                    for (var column = 0; column < columns.Length; column++)
                    {
                        primaryValues[column] = columns[column][row];
                        if (!ExcelValueSemantics.IsEmpty(primaryValues[column]))
                        {
                            isEmpty = false;
                        }
                    }

                    bool includeInAggregation;
                    var values = _rowSchema.CreateRow(
                        primaryValues,
                        _selectedSource.Alias,
                        isEmpty,
                        out includeInAggregation);
                    _accumulator.AcceptRow(values, includeInAggregation);
                }

                _nextRowIndex += rowCount;
            }

            public void End()
            {
                if (_accumulator == null || _selectedSource == null)
                {
                    throw new InvalidOperationException("union 聚合分块读取未初始化。");
                }

                if (_nextRowIndex != _selectedSource.Layout.TotalRows)
                {
                    throw new InvalidOperationException("union 来源读取不完整，拒绝返回部分聚合结果。");
                }

                if (_sourceIndex == _sources.Count - 1)
                {
                    _accumulator.Complete();
                }
            }
        }

        internal sealed class AggregateChunkSink : IColumnChunkSink
        {
            private readonly ExcelAggregateRequest _request;
            private readonly ExcelAggregateResult _result;
            private readonly IList<PreparedAggregateLookup> _lookups;
            private ExcelAggregationEngine.AggregateStreamAccumulator _accumulator;
            private AggregateRowSchema _rowSchema;
            private int _nextRowIndex;

            public AggregateChunkSink(ExcelAggregateRequest request, ExcelAggregateResult result)
                : this(request, result, new List<PreparedAggregateLookup>())
            {
            }

            public AggregateChunkSink(
                ExcelAggregateRequest request,
                ExcelAggregateResult result,
                IList<PreparedAggregateLookup> lookups)
            {
                _request = request;
                _result = result;
                _lookups = lookups;
            }

            public void Begin(ExcelColumnReadLayout layout)
            {
                _rowSchema = CreateAggregateRowSchema(
                    layout.Columns,
                    layout.ColumnIsDateFormatted,
                    _lookups,
                    false);
                _accumulator = ExcelAggregationEngine.CreateAggregateAccumulator(
                    _rowSchema.Headers,
                    _rowSchema.DateFormats,
                    _request,
                    _result,
                    layout.TotalRows);
                _nextRowIndex = 0;
            }

            public void Accept(int firstRowIndex, object[][] columns)
            {
                if (firstRowIndex != _nextRowIndex || columns == null)
                {
                    throw new InvalidOperationException("聚合分块行偏移不连续，拒绝返回不完整结果。");
                }

                var rowCount = columns.Length == 0 ? 0 : columns[0].Length;
                for (var column = 0; column < columns.Length; column++)
                {
                    if (columns[column] == null || columns[column].Length != rowCount)
                    {
                        throw new InvalidOperationException("聚合分块列长度不一致，拒绝返回不完整结果。");
                    }
                }

                for (var row = 0; row < rowCount; row++)
                {
                    var primaryValues = new object[columns.Length];
                    var isEmpty = true;
                    for (var column = 0; column < columns.Length; column++)
                    {
                        primaryValues[column] = columns[column][row];
                        if (!ExcelValueSemantics.IsEmpty(primaryValues[column]))
                        {
                            isEmpty = false;
                        }
                    }

                    bool includeInAggregation;
                    var values = _rowSchema.CreateRow(
                        primaryValues,
                        null,
                        isEmpty,
                        out includeInAggregation);
                    _accumulator.AcceptRow(values, includeInAggregation);
                }

                _nextRowIndex += rowCount;
            }

            public void End()
            {
                if (_accumulator == null)
                {
                    throw new InvalidOperationException("聚合分块读取未初始化。");
                }

                _accumulator.Complete();
            }
        }

        internal sealed class ReconcileChunkSink : IColumnChunkSink
        {
            private readonly string _groupColumn;
            private readonly string _valueColumn;
            private readonly string _groupParameter;
            private readonly string _valueParameter;
            private ExcelColumnReadLayout _layout;
            private int _groupIndex;
            private int _valueIndex;
            private int _nextRowIndex;
            private CoverageAccumulator _coverage;

            public ReconcileChunkSink(
                string groupColumn,
                string valueColumn,
                string groupParameter,
                string valueParameter)
            {
                _groupColumn = groupColumn;
                _valueColumn = valueColumn;
                _groupParameter = groupParameter;
                _valueParameter = valueParameter;
                Accumulator = new KeySetAccumulator();
                Coverage = new ExcelCoverageReport();
            }

            public KeySetAccumulator Accumulator { get; private set; }

            public ExcelCoverageReport Coverage { get; private set; }

            public void Begin(ExcelColumnReadLayout layout)
            {
                _layout = layout;
                var schema = new ExcelTabularSnapshot(
                    layout.Columns,
                    new List<object[]>(),
                    layout.ColumnIsDateFormatted);
                _groupIndex = ResolveColumn(schema, _groupColumn, _groupParameter);
                _valueIndex = ResolveColumn(schema, _valueColumn, _valueParameter);
                Accumulator = new KeySetAccumulator();
                _coverage = new CoverageAccumulator();
                _nextRowIndex = 0;
            }

            public void Accept(int firstRowIndex, object[][] columns)
            {
                if (firstRowIndex != _nextRowIndex
                    || columns == null
                    || columns.Length != _layout.Columns.Length)
                {
                    throw new InvalidOperationException("对账分块行偏移或列数不连续，拒绝返回不完整结果。");
                }

                var rowCount = columns.Length == 0 ? 0 : columns[0].Length;
                for (var column = 0; column < columns.Length; column++)
                {
                    if (columns[column] == null || columns[column].Length != rowCount)
                    {
                        throw new InvalidOperationException("对账分块列长度不一致，拒绝返回不完整结果。");
                    }
                }

                for (var row = 0; row < rowCount; row++)
                {
                    var values = new object[columns.Length];
                    var isEmpty = true;
                    for (var column = 0; column < columns.Length; column++)
                    {
                        var value = columns[column][row];
                        values[column] = value;
                        if (!ExcelValueSemantics.IsEmpty(value))
                        {
                            isEmpty = false;
                        }
                    }

                    _coverage.Accept(values, values.Length);
                    if (!isEmpty)
                    {
                        var key = ExcelValueSemantics.ToText(
                            values[_groupIndex],
                            _layout.ColumnIsDateFormatted[_groupIndex]);
                        Accumulator.Accept(key, values[_valueIndex]);
                    }
                }

                _nextRowIndex += rowCount;
            }

            public void End()
            {
                Coverage = _coverage.ToReport(_layout.TotalRows);
            }

            private static int ResolveColumn(
                ExcelTabularSnapshot schema,
                string columnName,
                string parameterName)
            {
                var index = schema.IndexOfColumn(columnName);
                if (index < 0)
                {
                    throw new InvalidOperationException(
                        parameterName + " 中的列 \"" + (columnName ?? string.Empty)
                        + "\" 不存在。可用列：" + string.Join("、", schema.Headers));
                }

                return index;
            }
        }

        /// <summary>ListObject 的数据区不含标题行，因此表来源固定按无表头解析。</summary>
        internal static string ResolveHeaderMode(string requestedMode, string sourceKind)
        {
            if (!string.IsNullOrWhiteSpace(requestedMode)
                && !string.Equals(requestedMode.Trim(), "auto", StringComparison.OrdinalIgnoreCase))
            {
                return requestedMode;
            }

            return string.Equals(sourceKind, ExcelTabularSourceKinds.ListObject, StringComparison.Ordinal)
                ? "yes"
                : "auto";
        }

        // ---------- 数据源解析 ----------

        internal ExcelInterop.Range ResolveSource(
            ExcelSourceRef source,
            out string sourceKind,
            out string resolvedTableName,
            out string resolvedWorksheetName)
        {
            source = source ?? new ExcelSourceRef();
            return ResolveSource(
                GetRequiredWorkbook(),
                source.WorksheetName,
                source.Address,
                source.TableName,
                out sourceKind,
                out resolvedTableName,
                out resolvedWorksheetName);
        }

        private ExcelInterop.Range ResolveSource(
            ExcelInterop.Workbook workbook,
            string worksheetName,
            string address,
            string tableName,
            out string sourceKind,
            out string resolvedTableName,
            out string resolvedWorksheetName)
        {
            resolvedTableName = string.Empty;
            resolvedWorksheetName = string.Empty;

            if (!string.IsNullOrWhiteSpace(tableName))
            {
                var listObjectRange = ResolveListObjectRange(workbook, tableName.Trim(), out resolvedWorksheetName);
                if (listObjectRange == null)
                {
                    throw new InvalidOperationException("未找到名为 \"" + tableName.Trim() + "\" 的 Excel 表。");
                }

                sourceKind = ExcelTabularSourceKinds.ListObject;
                resolvedTableName = tableName.Trim();
                return listObjectRange;
            }

            if (!string.IsNullOrWhiteSpace(address))
            {
                var worksheet = ResolveWorksheet(workbook, worksheetName);
                if (worksheet == null)
                {
                    throw new InvalidOperationException("未找到可用的工作表。");
                }

                resolvedWorksheetName = GetWorksheetName(worksheet);
                sourceKind = ExcelTabularSourceKinds.ExplicitAddress;
                return ResolveAddressRange(worksheet, address.Trim());
            }

            // 显式指定工作表代表用户要分析该表，不能被别处的单格选区劫持。
            if (!string.IsNullOrWhiteSpace(worksheetName))
            {
                var requestedSheet = ResolveWorksheet(workbook, worksheetName);
                if (requestedSheet == null)
                {
                    throw new InvalidOperationException("未找到可用的工作表。");
                }

                resolvedWorksheetName = GetWorksheetName(requestedSheet);
                sourceKind = ExcelTabularSourceKinds.WorksheetUsedRange;
                var requestedUsedRange = requestedSheet.UsedRange;
                if (requestedUsedRange == null)
                {
                    throw new InvalidOperationException("当前工作表没有数据。");
                }

                return requestedUsedRange;
            }

            var selection = _application != null ? _application.Selection as ExcelInterop.Range : null;
            if (selection != null)
            {
                var containing = FindContainingListObject(selection, out resolvedTableName, out resolvedWorksheetName);
                if (containing != null)
                {
                    ReleaseComObject(selection);
                    sourceKind = ExcelTabularSourceKinds.ListObject;
                    return containing;
                }

                if (SafeGetInt32(() => Convert.ToInt32(selection.Count)) > 1)
                {
                    resolvedWorksheetName = GetWorksheetName(selection.Worksheet);
                    sourceKind = ExcelTabularSourceKinds.Selection;
                    return selection;
                }

                var currentRegion = selection.CurrentRegion;
                ReleaseComObject(selection);
                if (currentRegion != null)
                {
                    resolvedWorksheetName = GetWorksheetName(currentRegion.Worksheet);
                    sourceKind = ExcelTabularSourceKinds.CurrentRegion;
                    return currentRegion;
                }
            }

            var activeSheet = ResolveWorksheet(workbook, worksheetName);
            if (activeSheet == null)
            {
                throw new InvalidOperationException("未找到可用的工作表。");
            }

            resolvedWorksheetName = GetWorksheetName(activeSheet);
            sourceKind = ExcelTabularSourceKinds.CurrentRegion;

            var usedRange = activeSheet.UsedRange;
            if (usedRange == null)
            {
                throw new InvalidOperationException("当前工作表没有数据。");
            }

            return usedRange;
        }

        private static ExcelInterop.Range ResolveAddressRange(ExcelInterop.Worksheet worksheet, string address)
        {
            ExcelInterop.Range range;
            try
            {
                range = worksheet.Range[address] as ExcelInterop.Range;
            }
            catch
            {
                throw new InvalidOperationException("区域地址无效：" + address);
            }

            if (range == null)
            {
                throw new InvalidOperationException("区域地址无效：" + address);
            }

            // A:A / 1:1 之类的整行整列引用需要与已用区域取交集，否则会读到百万空行。
            ExcelInterop.Range usedRange = null;
            ExcelInterop.Range intersection = null;
            try
            {
                usedRange = worksheet.UsedRange;
                if (usedRange == null)
                {
                    return range;
                }

                intersection = worksheet.Application.Intersect(range, usedRange) as ExcelInterop.Range;
                if (intersection == null)
                {
                    return range;
                }

                var result = intersection;
                intersection = null;
                ReleaseComObject(range);
                return result;
            }
            catch
            {
                return range;
            }
            finally
            {
                ReleaseComObject(intersection);
                ReleaseComObject(usedRange);
            }
        }

        private ExcelInterop.Range ResolveListObjectRange(
            ExcelInterop.Workbook workbook,
            string tableName,
            out string worksheetName)
        {
            worksheetName = string.Empty;
            object worksheetsObject = null;

            try
            {
                worksheetsObject = workbook.Worksheets;
                dynamic worksheets = worksheetsObject;
                var count = Convert.ToInt32(worksheets.Count);

                for (var index = 1; index <= count; index++)
                {
                    ExcelInterop.Worksheet worksheet = null;
                    object listObjectsObject = null;

                    try
                    {
                        worksheet = worksheets.Item(index) as ExcelInterop.Worksheet;
                        if (worksheet == null)
                        {
                            continue;
                        }

                        listObjectsObject = worksheet.ListObjects;
                        dynamic listObjects = listObjectsObject;
                        var listObjectCount = Convert.ToInt32(listObjects.Count);

                        for (var i = 1; i <= listObjectCount; i++)
                        {
                            object listObject = null;
                            try
                            {
                                listObject = listObjects.Item(i);
                                var name = Convert.ToString(((dynamic)listObject).Name) ?? string.Empty;
                                if (!string.Equals(name, tableName, StringComparison.OrdinalIgnoreCase))
                                {
                                    continue;
                                }

                                worksheetName = GetWorksheetName(worksheet);
                                var range = ((dynamic)listObject).Range as ExcelInterop.Range;
                                return range;
                            }
                            finally
                            {
                                ReleaseComObject(listObject);
                            }
                        }
                    }
                    finally
                    {
                        ReleaseComObject(listObjectsObject);
                        ReleaseComObject(worksheet);
                    }
                }
            }
            finally
            {
                ReleaseComObject(worksheetsObject);
            }

            return null;
        }

        private ExcelInterop.Range FindContainingListObject(
            ExcelInterop.Range selection,
            out string tableName,
            out string worksheetName)
        {
            tableName = string.Empty;
            worksheetName = string.Empty;

            object listObject = null;
            try
            {
                listObject = selection.ListObject;
                if (listObject == null)
                {
                    return null;
                }

                tableName = Convert.ToString(((dynamic)listObject).Name) ?? string.Empty;
                var range = ((dynamic)listObject).Range as ExcelInterop.Range;
                if (range != null)
                {
                    worksheetName = GetWorksheetName(range.Worksheet);
                }

                return range;
            }
            catch
            {
                return null;
            }
            finally
            {
                ReleaseComObject(listObject);
            }
        }

        // ---------- 文本渲染（供工具层构造 content） ----------

        public static string RenderTable(IList<string> headers, IList<string[]> rows, int maxRows)
        {
            var builder = new StringBuilder();
            if (headers != null && headers.Count > 0)
            {
                builder.AppendLine(string.Join(" | ", headers));
                builder.AppendLine(string.Join(" | ", new List<string>(headers).ConvertAll(h => "---")));
            }

            if (rows == null)
            {
                return builder.ToString();
            }

            var limit = maxRows > 0 ? Math.Min(maxRows, rows.Count) : rows.Count;
            for (var i = 0; i < limit; i++)
            {
                builder.AppendLine(string.Join(" | ", rows[i] ?? new string[0]));
            }

            if (limit < rows.Count)
            {
                builder.AppendLine("…（共 " + rows.Count + " 行，此处显示前 " + limit + " 行；完整结果见结构化输出）");
            }

            return builder.ToString();
        }
    }
}
