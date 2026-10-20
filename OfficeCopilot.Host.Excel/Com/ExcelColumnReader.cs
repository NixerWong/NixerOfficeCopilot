using System;
using System.Collections.Generic;
using System.Globalization;
using OfficeCopilot.Host.Abstractions;
using OfficeCopilot.Host.Excel.Analysis;
using ExcelInterop = Microsoft.Office.Interop.Excel;

namespace OfficeCopilot.Host.Excel.Com
{
    internal sealed class ExcelColumnReadRequest
    {
        public ExcelColumnReadRequest()
        {
            Columns = new List<string>();
            HeaderMode = "auto";
            ChunkCellBudget = 200000;
            IncludeAllColumns = false;
        }

        public IList<string> Columns { get; set; }

        public string HeaderMode { get; set; }

        public int ChunkCellBudget { get; set; }

        public bool IncludeAllColumns { get; set; }
    }

    internal sealed class ExcelColumnReadLayout
    {
        public string[] Columns { get; set; }

        public int[] SourceColumnIndexes { get; set; }

        public bool[] ColumnIsDateFormatted { get; set; }

        public int SourceRowCount { get; set; }

        public int TotalRows { get; set; }

        public int SourceColumnCount { get; set; }

        public int SourceFirstRow { get; set; }

        public object[] RawHeaderValues { get; set; }

        public bool HasHeader { get; set; }

        public bool HeaderInferred { get; set; }

        public string HeaderInferenceReason { get; set; }
    }

    internal interface IColumnChunkSink
    {
        void Begin(ExcelColumnReadLayout layout);

        void Accept(int firstRowIndex, object[][] columns);

        void End();
    }

    internal sealed class ExcelColumnReadColumn
    {
        public string Name { get; set; }

        public int SourceColumnIndex { get; set; }
    }

    internal sealed class ExcelColumnReader : ExcelServiceBase
    {
        internal const long MaximumProjectedCellCount = 50000000L;

        public ExcelColumnReader(ExcelInterop.Application application)
            : base(application)
        {
        }

        public ExcelColumnReadLayout Read(
            ExcelInterop.Range sourceRange,
            ExcelColumnReadRequest request,
            IColumnChunkSink sink)
        {
            if (sourceRange == null)
            {
                throw new ArgumentNullException(nameof(sourceRange));
            }

            if (sink == null)
            {
                throw new ArgumentNullException(nameof(sink));
            }

            request = request ?? new ExcelColumnReadRequest();
            if (request.ChunkCellBudget <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(request.ChunkCellBudget),
                    "Chunk cell budget must be greater than zero.");
            }

            var layout = ResolveLayout(sourceRange, request);
            EnsureWithinCellLimit(layout.TotalRows, layout.Columns.Length);
            for (var index = 0; index < layout.Columns.Length; index++)
            {
                layout.ColumnIsDateFormatted[index] = IsDateFormattedColumn(
                    sourceRange,
                    layout.SourceColumnIndexes[index],
                    layout.SourceRowCount,
                    layout.HasHeader);
            }

            sink.Begin(layout);
            var rowsPerChunk = CalculateRowsPerChunk(request.ChunkCellBudget, layout.Columns.Length);
            var firstDataRow = layout.HasHeader ? 2 : 1;
            StreamChunks(
                layout.TotalRows,
                rowsPerChunk,
                (firstRowIndex, rowCount) =>
                {
                    return ReadColumnsChunk(
                        sourceRange,
                        firstDataRow + firstRowIndex,
                        rowCount,
                        layout.SourceColumnIndexes);
                },
                sink);

            sink.End();
            return layout;
        }

        internal ExcelColumnReadLayout ResolveLayout(
            ExcelInterop.Range sourceRange,
            ExcelColumnReadRequest request)
        {
            if (sourceRange == null)
            {
                throw new ArgumentNullException(nameof(sourceRange));
            }

            request = request ?? new ExcelColumnReadRequest();
            var sourceRowCount = Convert.ToInt32(sourceRange.Rows.Count, CultureInfo.InvariantCulture);
            var sourceColumnCount = Convert.ToInt32(sourceRange.Columns.Count, CultureInfo.InvariantCulture);
            var headerRow = sourceRowCount > 0 && sourceColumnCount > 0
                ? ReadFirstRow(sourceRange, sourceColumnCount)
                : new object[0];

            bool headerInferred;
            string headerInferenceReason;
            var hasHeader = ExcelAggregationEngine.ShouldTreatHeaderRow(
                headerRow,
                sourceRowCount,
                request.HeaderMode,
                out headerInferred,
                out headerInferenceReason);

            var headerSnapshot = ExcelAggregationEngine.BuildSnapshot(
                new List<object[]> { headerRow },
                new bool[sourceColumnCount],
                sourceColumnCount,
                hasHeader);
            var projectedColumns = ResolveColumns(headerSnapshot.Headers, request);
            var columnNames = new string[projectedColumns.Length];
            var sourceColumnIndexes = new int[projectedColumns.Length];
            for (var index = 0; index < projectedColumns.Length; index++)
            {
                columnNames[index] = projectedColumns[index].Name;
                sourceColumnIndexes[index] = projectedColumns[index].SourceColumnIndex;
            }

            return new ExcelColumnReadLayout
            {
                Columns = columnNames,
                SourceColumnIndexes = sourceColumnIndexes,
                ColumnIsDateFormatted = new bool[projectedColumns.Length],
                SourceRowCount = sourceRowCount,
                TotalRows = Math.Max(0, sourceRowCount - (hasHeader ? 1 : 0)),
                SourceColumnCount = sourceColumnCount,
                SourceFirstRow = Convert.ToInt32(sourceRange.Row, CultureInfo.InvariantCulture),
                RawHeaderValues = headerRow,
                HasHeader = hasHeader,
                HeaderInferred = headerInferred,
                HeaderInferenceReason = headerInferenceReason ?? string.Empty
            };
        }

        internal static void StreamChunks(
            int totalRows,
            int rowsPerChunk,
            Func<int, int, object[][]> readChunk,
            IColumnChunkSink sink)
        {
            if (totalRows < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(totalRows));
            }

            if (rowsPerChunk <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(rowsPerChunk));
            }

            if (readChunk == null)
            {
                throw new ArgumentNullException(nameof(readChunk));
            }

            if (sink == null)
            {
                throw new ArgumentNullException(nameof(sink));
            }

            for (var firstRowIndex = 0; firstRowIndex < totalRows;)
            {
                var rowCount = Math.Min(rowsPerChunk, totalRows - firstRowIndex);
                sink.Accept(firstRowIndex, readChunk(firstRowIndex, rowCount));
                firstRowIndex += rowCount;
            }
        }

        internal static ExcelColumnReadColumn[] ResolveColumns(
            string[] headers,
            IList<string> requestedColumns)
        {
            headers = headers ?? new string[0];
            requestedColumns = requestedColumns ?? new string[0];
            var snapshot = new ExcelTabularSnapshot(headers, new List<object[]>(), null);
            var resolved = new List<ExcelColumnReadColumn>();
            var seenIndexes = new HashSet<int>();

            foreach (var requestedColumn in requestedColumns)
            {
                var sourceColumnIndex = snapshot.IndexOfColumn(requestedColumn);
                if (sourceColumnIndex < 0)
                {
                    throw new InvalidOperationException(
                        "未找到列 \"" + (requestedColumn ?? string.Empty).Trim()
                        + "\"。可用列名：" + string.Join("、", headers) + "。");
                }

                if (seenIndexes.Add(sourceColumnIndex))
                {
                    resolved.Add(new ExcelColumnReadColumn
                    {
                        Name = headers[sourceColumnIndex],
                        SourceColumnIndex = sourceColumnIndex
                    });
                }
            }

            return resolved.ToArray();
        }

        internal static ExcelColumnReadColumn[] ResolveColumns(
            string[] headers,
            ExcelColumnReadRequest request)
        {
            request = request ?? new ExcelColumnReadRequest();
            return request.IncludeAllColumns
                ? ResolveColumns(headers, headers)
                : ResolveColumns(headers, request.Columns);
        }

        internal static int CalculateRowsPerChunk(int chunkCellBudget, int projectedColumnCount)
        {
            if (chunkCellBudget <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(chunkCellBudget));
            }

            if (projectedColumnCount < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(projectedColumnCount));
            }

            return projectedColumnCount == 0
                ? int.MaxValue
                : Math.Max(1, chunkCellBudget / projectedColumnCount);
        }

        internal static void EnsureWithinCellLimit(int totalRows, int projectedColumnCount)
        {
            if (totalRows < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(totalRows));
            }

            if (projectedColumnCount < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(projectedColumnCount));
            }

            var projectedCellCount = (long)totalRows * projectedColumnCount;
            if (projectedCellCount > MaximumProjectedCellCount)
            {
                throw new ExcelSourceTooLargeException(projectedCellCount, MaximumProjectedCellCount);
            }
        }

        private static object[] ReadFirstRow(ExcelInterop.Range sourceRange, int columnCount)
        {
            ExcelInterop.Range rows = null;
            ExcelInterop.Range firstRow = null;
            try
            {
                rows = sourceRange.Rows as ExcelInterop.Range;
                firstRow = rows == null ? null : rows[1] as ExcelInterop.Range;
                if (firstRow == null)
                {
                    throw new InvalidOperationException("无法读取数据源首行以解析列名。");
                }

                return ConvertToRow(firstRow.Value2, columnCount);
            }
            finally
            {
                ReleaseComObject(firstRow);
                ReleaseComObject(rows);
            }
        }

        private static object[] ConvertToRow(object value, int columnCount)
        {
            var row = new object[columnCount];
            var matrix = value as object[,];
            if (matrix == null)
            {
                if (columnCount > 1)
                {
                    throw new InvalidOperationException("首行读取返回单值，但数据源包含多列。");
                }

                if (columnCount == 1)
                {
                    row[0] = value;
                }

                return row;
            }

            if (matrix.GetLength(0) != 1 || matrix.GetLength(1) != columnCount)
            {
                throw new InvalidOperationException("首行读取的二维数组大小与数据源列数不一致。");
            }

            var matrixRow = matrix.GetLowerBound(0);
            var matrixColumn = matrix.GetLowerBound(1);
            var count = Math.Min(columnCount, matrix.GetLength(1));
            for (var column = 0; column < count; column++)
            {
                row[column] = matrix[matrixRow, matrixColumn + column];
            }

            return row;
        }

        private static bool IsDateFormattedColumn(
            ExcelInterop.Range sourceRange,
            int sourceColumnIndex,
            int sourceRowCount,
            bool hasHeader)
        {
            var firstSampleRow = hasHeader ? 2 : 1;
            var lastSampleRow = Math.Min(sourceRowCount, firstSampleRow + 5);
            if (firstSampleRow > lastSampleRow)
            {
                return false;
            }

            ExcelInterop.Range cells = null;
            try
            {
                cells = sourceRange.Cells as ExcelInterop.Range;
                for (var row = firstSampleRow; row <= lastSampleRow; row++)
                {
                    ExcelInterop.Range cell = null;
                    try
                    {
                        cell = cells[row, sourceColumnIndex + 1] as ExcelInterop.Range;
                        if (cell != null
                            && ExcelValueSemantics.IsDateNumberFormat(cell.NumberFormat as string))
                        {
                            return true;
                        }
                    }
                    finally
                    {
                        ReleaseComObject(cell);
                    }
                }

                return false;
            }
            finally
            {
                ReleaseComObject(cells);
            }
        }

        private static object[][] ReadColumnsChunk(
            ExcelInterop.Range sourceRange,
            int firstSourceRow,
            int rowCount,
            int[] sourceColumnIndexes)
        {
            var columns = new object[sourceColumnIndexes.Length][];
            var outputColumn = 0;
            while (outputColumn < sourceColumnIndexes.Length)
            {
                var segmentStart = outputColumn;
                while (outputColumn + 1 < sourceColumnIndexes.Length
                    && sourceColumnIndexes[outputColumn + 1] == sourceColumnIndexes[outputColumn] + 1)
                {
                    outputColumn++;
                }

                var segmentColumnCount = outputColumn - segmentStart + 1;
                var segmentColumns = ReadContiguousColumnsChunk(
                    sourceRange,
                    firstSourceRow,
                    rowCount,
                    sourceColumnIndexes[segmentStart],
                    segmentColumnCount);
                for (var offset = 0; offset < segmentColumnCount; offset++)
                {
                    columns[segmentStart + offset] = segmentColumns[offset];
                }

                outputColumn++;
            }

            return columns;
        }

        private static object[][] ReadContiguousColumnsChunk(
            ExcelInterop.Range sourceRange,
            int firstSourceRow,
            int rowCount,
            int firstSourceColumn,
            int columnCount)
        {
            ExcelInterop.Range cells = null;
            ExcelInterop.Range firstCell = null;
            ExcelInterop.Range chunk = null;
            try
            {
                cells = sourceRange.Cells as ExcelInterop.Range;
                firstCell = cells[firstSourceRow, firstSourceColumn + 1] as ExcelInterop.Range;
                if (firstCell == null)
                {
                    throw new InvalidOperationException("无法定位数据块起始单元格。");
                }

                chunk = rowCount == 1 && columnCount == 1
                    ? firstCell
                    : firstCell.Resize[rowCount, columnCount] as ExcelInterop.Range;
                if (chunk == null)
                {
                    throw new InvalidOperationException("无法创建数据分块区域。");
                }

                return ConvertToColumns(chunk.Value2, rowCount, columnCount);
            }
            finally
            {
                if (!ReferenceEquals(chunk, firstCell))
                {
                    ReleaseComObject(chunk);
                }

                ReleaseComObject(firstCell);
                ReleaseComObject(cells);
            }
        }

        internal static object[][] ConvertToColumns(object value, int rowCount, int columnCount)
        {
            var values = new object[columnCount][];
            for (var column = 0; column < columnCount; column++)
            {
                values[column] = new object[rowCount];
            }

            var matrix = value as object[,];
            if (matrix == null)
            {
                if (rowCount * (long)columnCount > 1)
                {
                    throw new InvalidOperationException("多单元格数据块读取返回单值。");
                }

                if (rowCount > 0 && columnCount > 0)
                {
                    values[0][0] = value;
                }

                return values;
            }

            if (matrix.GetLength(0) != rowCount || matrix.GetLength(1) != columnCount)
            {
                throw new InvalidOperationException("数据块二维数组大小与请求的区域大小不一致。");
            }

            var matrixRow = matrix.GetLowerBound(0);
            var matrixColumn = matrix.GetLowerBound(1);
            var actualRows = Math.Min(rowCount, matrix.GetLength(0));
            var actualColumns = Math.Min(columnCount, matrix.GetLength(1));
            for (var row = 0; row < actualRows; row++)
            {
                for (var column = 0; column < actualColumns; column++)
                {
                    values[column][row] = matrix[matrixRow + row, matrixColumn + column];
                }
            }

            return values;
        }
    }

    internal sealed class ExcelSourceTooLargeException : InvalidOperationException
    {
        public const string ErrorCode = "excel.source_too_large";

        public ExcelSourceTooLargeException(long projectedCellCount, long maximumCellCount)
            : base(string.Format(
                CultureInfo.InvariantCulture,
                "所选列共包含 {0:N0} 个数据单元格，超过单次托管计算上限 {1:N0}。请减少列数或拆分数据范围。",
                projectedCellCount,
                maximumCellCount))
        {
        }
    }
}
