using OfficeCopilot.Host.Abstractions;
using OfficeCopilot.Host.Excel.Analysis;
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using ExcelInterop = Microsoft.Office.Interop.Excel;
using System.Drawing;
using System.Globalization;
using OfficeCopilot.HostBridge.Contracts;

namespace OfficeCopilot.Host.Excel
{
    internal sealed class ExcelRangeService : ExcelServiceBase
    {
        private const int MaximumHiddenRowsScan = 100000;
        private readonly IVbaExecutionHostAdapter _vbaExecutor;

        public ExcelRangeService(ExcelInterop.Application application, IVbaExecutionHostAdapter vbaExecutor)
            : base(application)
        {
            _vbaExecutor = vbaExecutor;
        }

        public ExcelRangeWriteResult WriteRange(ExcelRangeWriteRequest request)
        {
            request = request ?? new ExcelRangeWriteRequest();

            OfficeCopilot.Host.Excel.Tools.ExcelToolDiagnosticsLogger.Log(
                "excel_write_range.host",
                null,
                null,
                "enter",
                BuildWriteRangeRequestLog(request));

            var workbook = GetRequiredWorkbook();
            EnsureWorkbookWritable(workbook);

            var worksheet = ResolveWorksheet(workbook, request.WorksheetName);
            var range = ResolveWriteRange(worksheet, request.Address, request.UseSelectionWhenAddressEmpty);
            if (range == null)
            {
                throw new InvalidOperationException("未找到可写入的区域。");
            }

            ExcelInterop.Range targetRange = null;

            try
            {
                var hasMatrixValues = request.Values != null && request.Values.Count > 0;
                var valueKind = string.Empty;
                var preview = string.Empty;

                if (hasMatrixValues)
                {
                    var rowCount = request.Values.Count;
                    var columnCount = GetMatrixColumnCount(request.Values);
                    if (columnCount <= 0)
                    {
                        throw new InvalidOperationException("写入请求中的 values 不能为空。");
                    }

                    targetRange = ResizeRangeForWrite(range, rowCount, columnCount);
                    targetRange.Value2 = BuildExcelMatrix(request.Values, rowCount, columnCount);
                    valueKind = "matrix";
                    preview = BuildMatrixPreview(request.Values, rowCount, columnCount);
                }
                else
                {
                    targetRange = range;
                    var value = ResolveScalarWriteValue(request, out valueKind);
                    OfficeCopilot.Host.Excel.Tools.ExcelToolDiagnosticsLogger.Log(
                        "excel_write_range.host",
                        null,
                        null,
                        "before_assign",
                        "valueKind=" + valueKind + "; value=" + (Convert.ToString(value) ?? string.Empty));
                    targetRange.Value2 = value;
                    preview = Convert.ToString(value) ?? string.Empty;
                }

                OfficeCopilot.Host.Excel.Tools.ExcelToolDiagnosticsLogger.Log(
                    "excel_write_range.host",
                    null,
                    null,
                    "completed",
                    "workbook=" + (workbook.Name ?? string.Empty)
                    + "; worksheet=" + GetWorksheetName(worksheet)
                    + "; address=" + GetAddress(targetRange)
                    + "; valueKind=" + valueKind
                    + "; preview=" + preview);

                return new ExcelRangeWriteResult
                {
                    WorkbookName = workbook.Name ?? string.Empty,
                    WorksheetName = GetWorksheetName(worksheet),
                    Address = GetAddress(targetRange),
                    RowCount = Convert.ToInt32(targetRange.Rows.Count),
                    ColumnCount = Convert.ToInt32(targetRange.Columns.Count),
                    WrittenCellCount = Convert.ToInt32(targetRange.Rows.Count) * Convert.ToInt32(targetRange.Columns.Count),
                    ValueKind = valueKind,
                    Preview = preview
                };
            }
            finally
            {
                if (!ReferenceEquals(targetRange, range))
                {
                    ReleaseComObject(targetRange);
                }
            }
        }

        private static string BuildWriteRangeRequestLog(ExcelRangeWriteRequest request)
        {
            if (request == null)
            {
                return "request=<null>";
            }

            return "worksheet=" + (request.WorksheetName ?? string.Empty)
                + "; address=" + (request.Address ?? string.Empty)
                + "; useSelectionWhenAddressEmpty=" + request.UseSelectionWhenAddressEmpty
                + "; value=" + (request.Value ?? string.Empty)
                + "; numberValue=" + (request.NumberValue.HasValue ? request.NumberValue.Value.ToString() : "-")
                + "; booleanValue=" + (request.BooleanValue.HasValue ? request.BooleanValue.Value.ToString() : "-")
                + "; valuesCount=" + (request.Values == null ? 0 : request.Values.Count).ToString();
        }

        public ExcelRangeClearResult ClearRange(ExcelRangeRequest request)
        {
            request = request ?? new ExcelRangeRequest();

            var workbook = GetRequiredWorkbook();
            EnsureWorkbookWritable(workbook);

            var worksheet = ResolveWorksheet(workbook, request.WorksheetName);
            var range = ResolveToolRange(worksheet, request);
            if (range == null)
            {
                throw new InvalidOperationException("未找到可写入的区域。");
            }

            range.ClearContents();

            return new ExcelRangeClearResult
            {
                WorkbookName = workbook.Name ?? string.Empty,
                WorksheetName = GetWorksheetName(worksheet),
                Address = GetAddress(range),
                RowCount = Convert.ToInt32(range.Rows.Count),
                ColumnCount = Convert.ToInt32(range.Columns.Count),
                ClearedCellCount = Convert.ToInt32(range.Rows.Count) * Convert.ToInt32(range.Columns.Count)
            };
        }

        public ExcelRangeFormulaResult ReadRangeFormulas(ExcelRangeRequest request)
        {
            request = request ?? new ExcelRangeRequest();

            var workbook = GetRequiredWorkbook();
            var worksheet = ResolveWorksheet(workbook, request.WorksheetName);
            var range = ResolveToolRange(worksheet, request);
            if (range == null)
            {
                throw new InvalidOperationException("未找到可读取的区域。");
            }

            var maxRows = NormalizePositive(request.MaxRows, 50);
            var maxColumns = NormalizePositive(request.MaxColumns, 20);
            var totalRows = Convert.ToInt32(range.Rows.Count);
            var totalColumns = Convert.ToInt32(range.Columns.Count);

            return new ExcelRangeFormulaResult
            {
                WorkbookName = workbook.Name ?? string.Empty,
                WorksheetName = GetWorksheetName(worksheet),
                Address = GetAddress(range),
                RowCount = totalRows,
                ColumnCount = totalColumns,
                IsTruncated = totalRows > maxRows || totalColumns > maxColumns,
                Formulas = BuildFormulaRows(range, maxRows, maxColumns),
                Values = BuildDisplayValueRows(range, maxRows, maxColumns)
            };
        }

        public ExcelRangeInfoResult GetRangeInfo(ExcelRangeRequest request)
        {
            request = request ?? new ExcelRangeRequest();

            var workbook = GetRequiredWorkbook();
            var worksheet = ResolveWorksheet(workbook, request.WorksheetName);
            var range = ResolveToolRange(worksheet, request);
            if (range == null)
            {
                throw new InvalidOperationException("未找到可读取的区域。");
            }

            var rowCount = Convert.ToInt32(range.Rows.Count);
            var address = GetAddress(range);
            var worksheetName = GetWorksheetName(worksheet);
            var hiddenRowNumbers = request.IncludeHiddenRows
                ? ReadHiddenRowNumbers(range, rowCount)
                : null;
            var result = new ExcelRangeInfoResult
            {
                WorkbookName = workbook.Name ?? string.Empty,
                WorksheetName = worksheetName,
                Address = address,
                RowCount = rowCount,
                ColumnCount = Convert.ToInt32(range.Columns.Count),
                NumberFormat = GetFirstCellNumberFormat(range),
                Left = SafeGetDouble(() => Convert.ToDouble(range.Left)),
                Top = SafeGetDouble(() => Convert.ToDouble(range.Top)),
                Width = SafeGetDouble(() => Convert.ToDouble(range.Width)),
                Height = SafeGetDouble(() => Convert.ToDouble(range.Height)),
                HiddenRowsScanComplete = request.IncludeHiddenRows,
                HiddenRowsScanned = request.IncludeHiddenRows ? rowCount : 0,
                HiddenRowNumbers = hiddenRowNumbers,
                HiddenRowCount = hiddenRowNumbers == null ? 0 : hiddenRowNumbers.Count
            };
            if (request.IncludeHiddenRows)
            {
                result.Verification = CreateHiddenRowsVerification(
                    worksheetName,
                    address,
                    rowCount);
            }

            return result;
        }

        private static List<int> ReadHiddenRowNumbers(ExcelInterop.Range range, int rowCount)
        {
            if (rowCount <= 0)
            {
                throw new InvalidOperationException(
                    "隐藏行扫描区域未包含任何工作表行。");
            }

            if (rowCount > MaximumHiddenRowsScan)
            {
                throw new InvalidOperationException(
                    "隐藏行扫描最多支持 " + MaximumHiddenRowsScan
                    + " 行；请指定较小且完整的源数据区域后重试。");
            }

            var hiddenRows = new List<int>();
            ExcelInterop.Areas areas = null;
            ExcelInterop.Range rows = null;
            try
            {
                areas = range.Areas;
                if (Convert.ToInt32(areas.Count, CultureInfo.InvariantCulture) != 1)
                {
                    throw new InvalidOperationException(
                        "隐藏行扫描要求单一连续区域；请将多区域选择拆分后重试。");
                }

                rows = range.Rows;
                var firstWorksheetRow = Convert.ToInt32(range.Row, CultureInfo.InvariantCulture);
                for (var index = 1; index <= rowCount; index++)
                {
                    ExcelInterop.Range rowSegment = null;
                    ExcelInterop.Range entireRow = null;
                    try
                    {
                        rowSegment = rows[index] as ExcelInterop.Range;
                        if (rowSegment == null)
                        {
                            throw new InvalidOperationException(
                                "Excel 未返回第 " + index + " 个区域行。");
                        }

                        entireRow = rowSegment.EntireRow;
                        var hiddenValue = entireRow.Hidden;
                        if (!(hiddenValue is bool))
                        {
                            throw new InvalidOperationException(
                                "Excel 未返回第 "
                                + (firstWorksheetRow + index - 1)
                                + " 行的确定隐藏状态。");
                        }

                        if ((bool)hiddenValue)
                        {
                            hiddenRows.Add(firstWorksheetRow + index - 1);
                        }
                    }
                    finally
                    {
                        ReleaseComObject(entireRow);
                        ReleaseComObject(rowSegment);
                    }
                }
            }
            finally
            {
                ReleaseComObject(rows);
                ReleaseComObject(areas);
            }

            return hiddenRows;
        }

        private static ExcelVerificationResult CreateHiddenRowsVerification(
            string worksheetName,
            string address,
            int coveredRows)
        {
            var basis = new ExcelVerificationBasis
            {
                Source = new ExcelSourceRef
                {
                    WorksheetName = worksheetName ?? string.Empty,
                    Address = address ?? string.Empty
                },
                HeaderMode = "no",
                Note = "通过 Excel COM 对源区域逐行读取 EntireRow.Hidden，完整包含手动隐藏及筛选隐藏状态。"
            };
            var report = new ExcelVerificationReport
            {
                Passed = true,
                Tier = "A"
            };
            report.Notes.Add(
                "隐藏行状态由 Excel COM 对源区域全部 "
                + coveredRows
                + " 行直接扫描；结果为完整覆盖，未抽样。");

            return new ExcelVerificationResult
            {
                Tier = "A",
                Passed = true,
                AssertionCount = 0,
                CoveredRows = coveredRows,
                Independent = true,
                Sampled = false,
                Producer = ToolResultVerificationFields.ProducerManaged,
                Basis = basis,
                Notes = new List<string>(report.Notes),
                Details = report,
                Operation = "hiddenRowsScan"
            };
        }

        public ExcelRangeHyperlinkResult GetRangeHyperlinks(ExcelRangeRequest request)
        {
            request = request ?? new ExcelRangeRequest();

            var workbook = GetRequiredWorkbook();
            var worksheet = ResolveWorksheet(workbook, request.WorksheetName);
            var range = ResolveToolRange(worksheet, request);
            if (range == null)
            {
                throw new InvalidOperationException("未找到可读取的区域。");
            }

            var result = new ExcelRangeHyperlinkResult
            {
                WorkbookName = workbook.Name ?? string.Empty,
                WorksheetName = GetWorksheetName(worksheet),
                Address = GetAddress(range)
            };

            ExcelInterop.Hyperlinks hyperlinks = null;
            ExcelInterop.Hyperlink hyperlink = null;
            ExcelInterop.Range hyperlinkRange = null;

            try
            {
                hyperlinks = range.Hyperlinks;
                var count = Convert.ToInt32(hyperlinks.Count);

                for (var index = 1; index <= count; index++)
                {
                    try
                    {
                        hyperlink = hyperlinks[index];
                        if (hyperlink == null)
                        {
                            continue;
                        }

                        try
                        {
                            hyperlinkRange = hyperlink.Range;
                        }
                        catch
                        {
                            hyperlinkRange = null;
                        }

                        var address = hyperlink.Address ?? string.Empty;
                        var subAddress = hyperlink.SubAddress ?? string.Empty;

                        result.Hyperlinks.Add(new ExcelHyperlinkInfo
                        {
                            CellAddress = hyperlinkRange != null ? GetAddress(hyperlinkRange) : string.Empty,
                            Address = address,
                            SubAddress = subAddress,
                            DisplayText = GetHyperlinkDisplayText(hyperlink, hyperlinkRange),
                            ScreenTip = hyperlink.ScreenTip ?? string.Empty,
                            IsInternal = string.IsNullOrWhiteSpace(address) && !string.IsNullOrWhiteSpace(subAddress)
                        });
                    }
                    finally
                    {
                        ReleaseComObject(hyperlinkRange);
                        hyperlinkRange = null;
                        ReleaseComObject(hyperlink);
                        hyperlink = null;
                    }
                }
            }
            finally
            {
                ReleaseComObject(hyperlinkRange);
                ReleaseComObject(hyperlink);
                ReleaseComObject(hyperlinks);
            }

            return result;
        }

        private const int ScreenshotCopyPictureMaxRetries = 10;
        private const int ScreenshotCopyPictureRetryDelayMs = 700;
        private const int ScreenshotUsedRangeMaxRows = 500;
        private const int ScreenshotUsedRangeMaxColumns = 50;
        private const double ScreenshotMinimumWidthPoints = 50d;
        private const double ScreenshotMinimumHeightPoints = 30d;
        private const int ScreenshotAbsoluteMaxPixels = 4096;
        private const string ScreenshotExportFormat = "PNG";
        private const int ScreenshotPrepareDelayMs = 500;
        private const int ScreenshotPasteDelayMs = 250;

        public ExcelWorksheetScreenshotResult CaptureWorksheetScreenshot(ExcelWorksheetScreenshotRequest request)
        {
            request = request ?? new ExcelWorksheetScreenshotRequest();

            var workbook = GetRequiredWorkbook();
            var requestedWorksheet = ResolveWorksheet(workbook, request.WorksheetName);
            ExcelInterop.Worksheet actualWorksheet = requestedWorksheet;
            ExcelInterop.Range range = null;
            var captureSource = "range";

            try
            {
                range = ResolveScreenshotRange(
                    requestedWorksheet,
                    request,
                    out actualWorksheet,
                    out captureSource);

                if (range == null)
                {
                    throw new InvalidOperationException("未找到可截取的区域。");
                }

                int imageWidth;
                int imageHeight;
                var imageBytes = CaptureRangeScreenshotPng(
                    _application,
                    actualWorksheet,
                    range,
                    request.MaxImageWidth,
                    request.MaxImageHeight,
                    out imageWidth,
                    out imageHeight);

                return new ExcelWorksheetScreenshotResult
                {
                    WorkbookName = workbook.Name ?? string.Empty,
                    WorksheetName = GetWorksheetName(actualWorksheet),
                    Address = GetAddress(range),
                    RowCount = Convert.ToInt32(range.Rows.Count),
                    ColumnCount = Convert.ToInt32(range.Columns.Count),
                    CaptureSource = captureSource,
                    MimeType = "image/png",
                    ImageWidth = imageWidth,
                    ImageHeight = imageHeight,
                    ByteCount = imageBytes.Length,
                    ImageBase64 = Convert.ToBase64String(imageBytes)
                };
            }
            finally
            {
                ReleaseComObject(range);

                if (!ReferenceEquals(actualWorksheet, requestedWorksheet))
                {
                    ReleaseComObject(actualWorksheet);
                }
            }
        }

        private ExcelInterop.Range ResolveScreenshotRange(
            ExcelInterop.Worksheet worksheet,
            ExcelWorksheetScreenshotRequest request,
            out ExcelInterop.Worksheet actualWorksheet,
            out string captureSource)
        {
            actualWorksheet = worksheet;
            captureSource = "range";

            if (request == null)
            {
                return null;
            }

            if (!string.IsNullOrWhiteSpace(request.Address))
            {
                if (worksheet == null)
                {
                    throw new InvalidOperationException("未找到可截取的工作表。");
                }

                try
                {
                    return worksheet.Range[request.Address.Trim()] as ExcelInterop.Range;
                }
                catch
                {
                    throw new InvalidOperationException("区域地址无效：" + request.Address);
                }
            }

            if (request.UseSelectionWhenAddressEmpty)
            {
                var selection = _application != null ? _application.Selection as ExcelInterop.Range : null;
                if (selection != null)
                {
                    ExcelInterop.Worksheet selectionWorksheet = null;

                    try
                    {
                        selectionWorksheet = selection.Worksheet as ExcelInterop.Worksheet;
                    }
                    catch
                    {
                        selectionWorksheet = null;
                    }

                    actualWorksheet = selectionWorksheet ?? worksheet;
                    captureSource = "selection";
                    return selection;
                }
            }

            if (request.UseUsedRangeWhenAddressEmpty && worksheet != null)
            {
                captureSource = "usedRange";
                return BuildScreenshotUsedRange(worksheet);
            }

            return null;
        }

        private static ExcelInterop.Range BuildScreenshotUsedRange(ExcelInterop.Worksheet worksheet)
        {
            ExcelInterop.Range usedRange = null;
            ExcelInterop.Range startCell = null;
            ExcelInterop.Range endCell = null;
            ExcelInterop.Range cappedRange = null;

            try
            {
                if (worksheet == null)
                {
                    return null;
                }

                usedRange = worksheet.UsedRange as ExcelInterop.Range;
                if (usedRange == null)
                {
                    return null;
                }

                var rowCount = Convert.ToInt32(usedRange.Rows.Count);
                var columnCount = Convert.ToInt32(usedRange.Columns.Count);

                if (rowCount <= ScreenshotUsedRangeMaxRows
                    && columnCount <= ScreenshotUsedRangeMaxColumns)
                {
                    var result = usedRange;
                    usedRange = null;
                    return result;
                }

                var startRow = Convert.ToInt32(usedRange.Row);
                var startColumn = Convert.ToInt32(usedRange.Column);
                var endRow = startRow + Math.Min(rowCount, ScreenshotUsedRangeMaxRows) - 1;
                var endColumn = startColumn + Math.Min(columnCount, ScreenshotUsedRangeMaxColumns) - 1;

                startCell = worksheet.Cells[startRow, startColumn] as ExcelInterop.Range;
                endCell = worksheet.Cells[endRow, endColumn] as ExcelInterop.Range;
                cappedRange = worksheet.Range[startCell, endCell] as ExcelInterop.Range;

                var cappedResult = cappedRange;
                cappedRange = null;
                return cappedResult;
            }
            finally
            {
                ReleaseComObject(cappedRange);
                ReleaseComObject(endCell);
                ReleaseComObject(startCell);
                ReleaseComObject(usedRange);
            }
        }

        private static byte[] CaptureRangeScreenshotPng(
            ExcelInterop.Application application,
            ExcelInterop.Worksheet worksheet,
            ExcelInterop.Range range,
            int maxImageWidth,
            int maxImageHeight,
            out int imageWidth,
            out int imageHeight)
        {
            object chartObjects = null;
            object chartObject = null;
            object chart = null;
            string tempFile = null;
            var wasVisible = false;
            var originalWindowState = ExcelInterop.XlWindowState.xlNormal;
            var windowStateChanged = false;

            try
            {
                if (application == null)
                {
                    throw new InvalidOperationException("当前未连接 Excel 实例。");
                }

                if (worksheet == null)
                {
                    throw new InvalidOperationException("未找到可导出截图的工作表。");
                }

                if (range == null)
                {
                    throw new ArgumentNullException(nameof(range));
                }

                wasVisible = application.Visible;
                if (!wasVisible)
                {
                    application.Visible = true;
                    Thread.Sleep(2000);
                }
                else
                {
                    Thread.Sleep(1000);
                }

                try
                {
                    originalWindowState = application.WindowState;
                    if (originalWindowState == ExcelInterop.XlWindowState.xlMinimized)
                    {
                        application.WindowState = ExcelInterop.XlWindowState.xlNormal;
                        windowStateChanged = true;
                        Thread.Sleep(500);
                    }
                }
                catch
                {
                }

                TryActivateApplication(application);
                PrepareRangeForCapture(application, worksheet, range);

                var widthPoints = Math.Max(
                    ScreenshotMinimumWidthPoints,
                    Convert.ToDouble(range.Width));

                var heightPoints = Math.Max(
                    ScreenshotMinimumHeightPoints,
                    Convert.ToDouble(range.Height));

                var maxWidthPoints = Math.Min(
                    ConvertPixelsToPoints(NormalizePositive(maxImageWidth, 1600)),
                    ConvertPixelsToPoints(ScreenshotAbsoluteMaxPixels));

                var maxHeightPoints = Math.Min(
                    ConvertPixelsToPoints(NormalizePositive(maxImageHeight, 1200)),
                    ConvertPixelsToPoints(ScreenshotAbsoluteMaxPixels));

                if (widthPoints > maxWidthPoints || heightPoints > maxHeightPoints)
                {
                    var widthScale = maxWidthPoints / Math.Max(1d, widthPoints);
                    var heightScale = maxHeightPoints / Math.Max(1d, heightPoints);
                    var scale = Math.Min(widthScale, heightScale);

                    widthPoints = Math.Max(ScreenshotMinimumWidthPoints, widthPoints * scale);
                    heightPoints = Math.Max(ScreenshotMinimumHeightPoints, heightPoints * scale);
                }

                CopyPictureWithRetry(range);

                dynamic runtimeChartObjects = worksheet.ChartObjects();
                chartObjects = runtimeChartObjects;

                dynamic runtimeChartObject = runtimeChartObjects.Add(0d, 0d, widthPoints, heightPoints);
                chartObject = runtimeChartObject;

                dynamic runtimeChart = runtimeChartObject.Chart;
                chart = runtimeChart;

                PastePictureWithRetry(application, worksheet, range, runtimeChart);

                try
                {
                    application.CutCopyMode = (ExcelInterop.XlCutCopyMode)0;
                }
                catch
                {
                }

                tempFile = Path.Combine(
                    Path.GetTempPath(),
                    "officecopilot-excel-screenshot-" + Guid.NewGuid().ToString("N") + ".png");

                var exportSucceeded = Convert.ToBoolean(
                    runtimeChart.Export(tempFile, ScreenshotExportFormat, false));

                if (!exportSucceeded || !File.Exists(tempFile))
                {
                    throw new InvalidOperationException("Excel 未能成功导出截图文件。");
                }

                var imageBytes = File.ReadAllBytes(tempFile);
                var dimensions = GetPngDimensions(imageBytes);
                imageWidth = dimensions.width;
                imageHeight = dimensions.height;

                return imageBytes;
            }
            finally
            {
                if (chartObject != null)
                {
                    try
                    {
                        ((dynamic)chartObject).Delete();
                    }
                    catch
                    {
                    }
                }

                try
                {
                    if (application != null)
                    {
                        application.CutCopyMode = (ExcelInterop.XlCutCopyMode)0;
                    }
                }
                catch
                {
                }

                ReleaseComObject(chart);
                ReleaseComObject(chartObject);
                ReleaseComObject(chartObjects);

                if (!wasVisible && application != null)
                {
                    try
                    {
                        application.Visible = false;
                    }
                    catch
                    {
                    }
                }

                if (windowStateChanged && application != null)
                {
                    try
                    {
                        application.WindowState = originalWindowState;
                    }
                    catch
                    {
                    }
                }

                if (!string.IsNullOrWhiteSpace(tempFile) && File.Exists(tempFile))
                {
                    try
                    {
                        File.Delete(tempFile);
                    }
                    catch
                    {
                    }
                }
            }
        }

        private static void TryActivateApplication(ExcelInterop.Application application)
        {
            if (application == null)
            {
                return;
            }

            try
            {
                var window = application.ActiveWindow;
                if (window != null)
                {
                    window.Activate();
                    ReleaseComObject(window);
                }
                Thread.Sleep(500);
            }
            catch
            {
            }
        }

        private static void TryActivateWorksheet(ExcelInterop.Worksheet worksheet)
        {
            if (worksheet == null)
            {
                return;
            }

            try
            {
                worksheet.Activate();
                Thread.Sleep(200);
            }
            catch
            {
            }
        }

        private static void PrepareRangeForCapture(
            ExcelInterop.Application application,
            ExcelInterop.Worksheet worksheet,
            ExcelInterop.Range range)
        {
            ExcelInterop.Range topLeftCell = null;
            ExcelInterop.Window activeWindow = null;

            try
            {
                TryActivateWorksheet(worksheet);

                try
                {
                    topLeftCell = range.Cells[1, 1] as ExcelInterop.Range;
                }
                catch
                {
                    topLeftCell = null;
                }

                try
                {
                    if (application != null && topLeftCell != null)
                    {
                        application.Goto(topLeftCell, true);
                    }
                }
                catch
                {
                }

                try
                {
                    activeWindow = application != null ? application.ActiveWindow : null;
                    if (activeWindow != null && topLeftCell != null)
                    {
                        activeWindow.ScrollRow = Convert.ToInt32(topLeftCell.Row);
                        activeWindow.ScrollColumn = Convert.ToInt32(topLeftCell.Column);
                    }
                }
                catch
                {
                }

                try
                {
                    range.Select();
                }
                catch
                {
                }

                Thread.Sleep(ScreenshotPrepareDelayMs);
            }
            finally
            {
                ReleaseComObject(activeWindow);
                ReleaseComObject(topLeftCell);
            }
        }

        private static void PastePictureWithRetry(
            ExcelInterop.Application application,
            ExcelInterop.Worksheet worksheet,
            ExcelInterop.Range range,
            object chart)
        {
            if (chart == null)
            {
                throw new ArgumentNullException(nameof(chart));
            }

            for (var attempt = 0; attempt < ScreenshotCopyPictureMaxRetries; attempt++)
            {
                try
                {
                    dynamic runtimeChart = chart;

                    try
                    {
                        runtimeChart.Parent.Activate();
                    }
                    catch
                    {
                    }

                    try
                    {
                        runtimeChart.ChartArea.Select();
                    }
                    catch
                    {
                    }

                    Thread.Sleep(100);
                    runtimeChart.Paste();
                    Thread.Sleep(ScreenshotPasteDelayMs);
                    return;
                }
                catch (COMException)
                {
                    if (attempt >= ScreenshotCopyPictureMaxRetries - 1)
                    {
                        throw;
                    }

                    Thread.Sleep(ScreenshotCopyPictureRetryDelayMs * (attempt + 1));
                    PrepareRangeForCapture(application, worksheet, range);

                    try
                    {
                        if (application != null)
                        {
                            application.CutCopyMode = (ExcelInterop.XlCutCopyMode)0;
                        }
                    }
                    catch
                    {
                    }

                    CopyPictureWithRetry(range);
                }
            }
        }

        private static void CopyPictureWithRetry(ExcelInterop.Range range)
        {
            if (range == null)
            {
                throw new ArgumentNullException(nameof(range));
            }

            for (var attempt = 0; attempt < ScreenshotCopyPictureMaxRetries; attempt++)
            {
                try
                {
                    range.CopyPicture(
                        ExcelInterop.XlPictureAppearance.xlScreen,
                        ExcelInterop.XlCopyPictureFormat.xlPicture);
                    return;
                }
                catch (COMException)
                {
                    if (attempt >= ScreenshotCopyPictureMaxRetries - 1)
                    {
                        throw;
                    }

                    Thread.Sleep(ScreenshotCopyPictureRetryDelayMs * (attempt + 1));
                }
            }
        }

        private static double ConvertPixelsToPoints(int pixels)
        {
            return Math.Max(1, pixels) * 72d / 96d;
        }

        private static (int width, int height) GetPngDimensions(byte[] data)
        {
            if (data == null || data.Length < 24)
            {
                return (0, 0);
            }

            var width = (data[16] << 24) | (data[17] << 16) | (data[18] << 8) | data[19];
            var height = (data[20] << 24) | (data[21] << 16) | (data[22] << 8) | data[23];
            return (width, height);
        }

        public ExcelRangeValidationResult GetRangeValidation(ExcelRangeRequest request)
        {
            request = request ?? new ExcelRangeRequest();

            var workbook = GetRequiredWorkbook();
            var worksheet = ResolveWorksheet(workbook, request.WorksheetName);
            var range = ResolveToolRange(worksheet, request);
            if (range == null)
            {
                throw new InvalidOperationException("未找到可读取的区域。");
            }

            ExcelInterop.Range firstCell = null;
            ExcelInterop.Validation validation = null;

            try
            {
                firstCell = range.Cells[1, 1] as ExcelInterop.Range;
                if (firstCell == null)
                {
                    firstCell = range;
                }

                try
                {
                    validation = firstCell.Validation;
                    var validationType = Convert.ToInt32(validation.Type);

                    return new ExcelRangeValidationResult
                    {
                        WorkbookName = workbook.Name ?? string.Empty,
                        WorksheetName = GetWorksheetName(worksheet),
                        Address = GetAddress(range),
                        HasValidation = validationType != (int)ExcelInterop.XlDVType.xlValidateInputOnly,
                        ValidationType = MapValidationType(validationType),
                        ValidationOperator = MapValidationOperator(Convert.ToInt32(validation.Operator)),
                        Formula1 = Convert.ToString(validation.Formula1) ?? string.Empty,
                        Formula2 = Convert.ToString(validation.Formula2) ?? string.Empty,
                        IgnoreBlank = Convert.ToBoolean(validation.IgnoreBlank),
                        ShowInputMessage = Convert.ToBoolean(validation.ShowInput),
                        InputTitle = validation.InputTitle ?? string.Empty,
                        InputMessage = validation.InputMessage ?? string.Empty,
                        ShowErrorAlert = Convert.ToBoolean(validation.ShowError),
                        ErrorStyle = MapValidationAlertStyle(Convert.ToInt32(validation.AlertStyle)),
                        ErrorTitle = validation.ErrorTitle ?? string.Empty,
                        ValidationErrorMessage = validation.ErrorMessage ?? string.Empty
                    };
                }
                catch (COMException)
                {
                    return new ExcelRangeValidationResult
                    {
                        WorkbookName = workbook.Name ?? string.Empty,
                        WorksheetName = GetWorksheetName(worksheet),
                        Address = GetAddress(range),
                        HasValidation = false
                    };
                }
                catch
                {
                    return new ExcelRangeValidationResult
                    {
                        WorkbookName = workbook.Name ?? string.Empty,
                        WorksheetName = GetWorksheetName(worksheet),
                        Address = GetAddress(range),
                        HasValidation = false
                    };
                }
            }
            finally
            {
                ReleaseComObject(validation);
                ReleaseComObject(firstCell);
            }
        }

        public ExcelRangeNumberFormatResult GetRangeNumberFormats(ExcelRangeRequest request)
        {
            request = request ?? new ExcelRangeRequest();

            var workbook = GetRequiredWorkbook();
            var worksheet = ResolveWorksheet(workbook, request.WorksheetName);
            var range = ResolveToolRange(worksheet, request);
            if (range == null)
            {
                throw new InvalidOperationException("未找到可读取的区域。");
            }

            var maxRows = NormalizePositive(request.MaxRows, 50);
            var maxColumns = NormalizePositive(request.MaxColumns, 20);
            var totalRows = Convert.ToInt32(range.Rows.Count);
            var totalColumns = Convert.ToInt32(range.Columns.Count);

            return new ExcelRangeNumberFormatResult
            {
                WorkbookName = workbook.Name ?? string.Empty,
                WorksheetName = GetWorksheetName(worksheet),
                Address = GetAddress(range),
                RowCount = totalRows,
                ColumnCount = totalColumns,
                IsTruncated = totalRows > maxRows || totalColumns > maxColumns,
                Formats = BuildNumberFormatRows(range, maxRows, maxColumns)
            };
        }

        public ExcelRangeMergeInfoResult GetRangeMergeInfo(ExcelRangeRequest request)
        {
            request = request ?? new ExcelRangeRequest();

            var workbook = GetRequiredWorkbook();
            var worksheet = ResolveWorksheet(workbook, request.WorksheetName);
            var range = ResolveToolRange(worksheet, request);
            if (range == null)
            {
                throw new InvalidOperationException("未找到可读取的区域。");
            }

            ExcelInterop.Range mergeArea = null;
            var isMerged = SafeGetBool(() => Convert.ToBoolean(range.MergeCells), false);

            try
            {
                if (isMerged)
                {
                    mergeArea = range.MergeArea;
                }

                return new ExcelRangeMergeInfoResult
                {
                    WorkbookName = workbook.Name ?? string.Empty,
                    WorksheetName = GetWorksheetName(worksheet),
                    Address = GetAddress(range),
                    IsMerged = isMerged,
                    MergeAreaAddress = mergeArea != null ? GetAddress(mergeArea) : string.Empty
                };
            }
            finally
            {
                ReleaseComObject(mergeArea);
            }
        }

        public ExcelRangeLockInfoResult GetRangeLockInfo(ExcelRangeRequest request)
        {
            request = request ?? new ExcelRangeRequest();

            var workbook = GetRequiredWorkbook();
            var worksheet = ResolveWorksheet(workbook, request.WorksheetName);
            var range = ResolveToolRange(worksheet, request);
            if (range == null)
            {
                throw new InvalidOperationException("未找到可读取的区域。");
            }

            var lockedValue = default(object);

            try
            {
                lockedValue = range.Locked;

                if (lockedValue is bool)
                {
                    return new ExcelRangeLockInfoResult
                    {
                        WorkbookName = workbook.Name ?? string.Empty,
                        WorksheetName = GetWorksheetName(worksheet),
                        Address = GetAddress(range),
                        IsLocked = (bool)lockedValue,
                        IsMixed = false
                    };
                }

                return new ExcelRangeLockInfoResult
                {
                    WorkbookName = workbook.Name ?? string.Empty,
                    WorksheetName = GetWorksheetName(worksheet),
                    Address = GetAddress(range),
                    IsLocked = false,
                    IsMixed = true
                };
            }
            catch
            {
                return new ExcelRangeLockInfoResult
                {
                    WorkbookName = workbook.Name ?? string.Empty,
                    WorksheetName = GetWorksheetName(worksheet),
                    Address = GetAddress(range),
                    IsLocked = false,
                    IsMixed = true
                };
            }
        }

        public ExcelRangeStyleResult GetRangeStyle(ExcelRangeRequest request)
        {
            request = request ?? new ExcelRangeRequest();

            var workbook = GetRequiredWorkbook();
            var worksheet = ResolveWorksheet(workbook, request.WorksheetName);
            var range = ResolveToolRange(worksheet, request);
            if (range == null)
            {
                throw new InvalidOperationException("未找到可读取的区域。");
            }

            var result = new ExcelRangeStyleResult
            {
                WorkbookName = workbook.Name ?? string.Empty,
                WorksheetName = GetWorksheetName(worksheet),
                Address = GetAddress(range),
                CellCount = ReadCellCount(range)
            };

            ReadNamedStyle(range, result);
            ReadEffectiveFormat(range, result);

            var tracked = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                { "fillColor", result.FillColor },
                { "fontColor", result.FontColor },
                { "fontName", result.FontName },
                { "fontSize", result.FontSize },
                { "bold", result.Bold },
                { "italic", result.Italic },
                { "underline", result.Underline },
                { "numberFormat", result.NumberFormat },
                { "horizontalAlignment", result.HorizontalAlignment },
                { "verticalAlignment", result.VerticalAlignment },
                { "borders", result.Borders }
            };
            result.MixedProperties = ExcelStyleReadback.CollectInconclusiveProperties(tracked);

            return result;
        }

        private static int ReadCellCount(ExcelInterop.Range range)
        {
            try
            {
                return range.Count;
            }
            catch
            {
                return 0;
            }
        }

        /// <summary>
        /// 命名样式只回答"套了哪个样式"，回答不了"实际是什么颜色"。
        /// 必须先取 Style.Name，直接 Convert.ToString(range.Style) 得到的是 System.__ComObject。
        /// </summary>
        private static void ReadNamedStyle(ExcelInterop.Range range, ExcelRangeStyleResult result)
        {
            object styleObject = null;
            try
            {
                styleObject = range.Style;
                if (styleObject == null)
                {
                    // Excel 对样式不一致的区域返回 null。
                    result.StyleName = ExcelStyleReadback.Mixed;
                    return;
                }

                dynamic runtimeStyle = styleObject;

                try
                {
                    result.StyleName = Convert.ToString(runtimeStyle.Name) ?? string.Empty;
                }
                catch
                {
                    result.StyleName = ExcelStyleReadback.Unknown;
                }

                try
                {
                    result.IsBuiltInStyle = Convert.ToBoolean(runtimeStyle.BuiltIn);
                }
                catch
                {
                    result.IsBuiltInStyle = false;
                }

                try
                {
                    var localName = Convert.ToString(runtimeStyle.NameLocal) ?? string.Empty;
                    if (!string.IsNullOrWhiteSpace(localName)
                        && !string.Equals(localName, result.StyleName, StringComparison.OrdinalIgnoreCase))
                    {
                        result.StyleDescription = "本地名称：" + localName;
                    }
                }
                catch
                {
                }
            }
            catch
            {
                result.StyleName = ExcelStyleReadback.Unknown;
            }
            finally
            {
                ReleaseComObject(styleObject);
            }
        }

        private static void ReadEffectiveFormat(ExcelInterop.Range range, ExcelRangeStyleResult result)
        {
            object interior = null;
            object font = null;
            try
            {
                try
                {
                    interior = range.Interior;
                    dynamic runtimeInterior = interior;
                    result.FillColor = ExcelStyleReadback.FromOleColor(
                        ReadComProperty(() => (object)runtimeInterior.Color),
                        ReadComProperty(() => (object)runtimeInterior.ColorIndex));
                }
                catch
                {
                    result.FillColor = ExcelStyleReadback.Unknown;
                }

                try
                {
                    font = range.Font;
                    dynamic runtimeFont = font;
                    result.FontColor = ExcelStyleReadback.FromOleColor(
                        ReadComProperty(() => (object)runtimeFont.Color),
                        null);
                    result.FontName = ExcelStyleReadback.FromText(ReadComProperty(() => (object)runtimeFont.Name));
                    result.FontSize = ExcelStyleReadback.FromNumber(ReadComProperty(() => (object)runtimeFont.Size));
                    result.Bold = ExcelStyleReadback.FromBoolean(ReadComProperty(() => (object)runtimeFont.Bold));
                    result.Italic = ExcelStyleReadback.FromBoolean(ReadComProperty(() => (object)runtimeFont.Italic));
                    result.Underline = ExcelStyleReadback.FromUnderline(ReadComProperty(() => (object)runtimeFont.Underline));
                }
                catch
                {
                    result.FontColor = ExcelStyleReadback.Unknown;
                    result.FontName = ExcelStyleReadback.Unknown;
                    result.FontSize = ExcelStyleReadback.Unknown;
                    result.Bold = ExcelStyleReadback.Unknown;
                    result.Italic = ExcelStyleReadback.Unknown;
                    result.Underline = ExcelStyleReadback.Unknown;
                }

                result.NumberFormat = ExcelStyleReadback.FromText(ReadComProperty(() => (object)range.NumberFormat));
                result.HorizontalAlignment = ExcelStyleReadback.DescribeHorizontalAlignment(
                    ReadComProperty(() => (object)range.HorizontalAlignment));
                result.VerticalAlignment = ExcelStyleReadback.DescribeVerticalAlignment(
                    ReadComProperty(() => (object)range.VerticalAlignment));
                result.Borders = ReadBorderSummary(range);
            }
            finally
            {
                ReleaseComObject(font);
                ReleaseComObject(interior);
            }
        }

        private static string ReadBorderSummary(ExcelInterop.Range range)
        {
            var edges = new[]
            {
                ExcelInterop.XlBordersIndex.xlEdgeLeft,
                ExcelInterop.XlBordersIndex.xlEdgeTop,
                ExcelInterop.XlBordersIndex.xlEdgeBottom,
                ExcelInterop.XlBordersIndex.xlEdgeRight
            };

            var lineStyles = new List<object>(4);
            foreach (var edge in edges)
            {
                object border = null;
                try
                {
                    border = range.Borders[edge];
                    dynamic runtimeBorder = border;
                    lineStyles.Add(ReadComProperty(() => (object)runtimeBorder.LineStyle));
                }
                catch
                {
                    lineStyles.Add(null);
                }
                finally
                {
                    ReleaseComObject(border);
                }
            }

            return ExcelStyleReadback.SummarizeBorders(lineStyles);
        }

        /// <summary>读取 COM 属性；Excel 对区域内取值不一致的属性返回 null，这里保持 null 语义交由上层翻译成 mixed。</summary>
        private static object ReadComProperty(Func<object> reader)
        {
            try
            {
                return reader();
            }
            catch
            {
                return null;
            }
        }

        private ExcelInterop.Range ResolveToolRange(ExcelInterop.Worksheet worksheet, ExcelRangeRequest request)
        {
            return ResolveWriteRange(
                worksheet,
                request == null ? string.Empty : request.Address,
                request != null && request.UseSelectionWhenAddressEmpty);
        }

        private static Array BuildExcelMatrix(IList<string[]> values, int rowCount, int columnCount)
        {
            var matrix = Array.CreateInstance(typeof(object), new[] { rowCount, columnCount }, new[] { 1, 1 });

            for (var rowIndex = 0; rowIndex < rowCount; rowIndex++)
            {
                var row = values[rowIndex];

                for (var columnIndex = 0; columnIndex < columnCount; columnIndex++)
                {
                    var cellValue = row != null && columnIndex < row.Length
                        ? (object)(row[columnIndex] ?? string.Empty)
                        : string.Empty;
                    matrix.SetValue(cellValue, rowIndex + 1, columnIndex + 1);
                }
            }

            return matrix;
        }

        private static object ResolveScalarWriteValue(ExcelRangeWriteRequest request, out string valueKind)
        {
            valueKind = "text";

            if (request != null)
            {
                if (request.NumberValue.HasValue)
                {
                    valueKind = "number";
                    return request.NumberValue.Value;
                }

                if (request.BooleanValue.HasValue)
                {
                    valueKind = "boolean";
                    return request.BooleanValue.Value;
                }

                return request.Value ?? string.Empty;
            }

            return string.Empty;
        }

        private static List<string[]> BuildDisplayValueRows(ExcelInterop.Range range, int maxRows, int maxColumns)
        {
            var rows = new List<string[]>();
            if (range == null)
            {
                return rows;
            }

            var rowCount = Math.Min(Convert.ToInt32(range.Rows.Count), Math.Max(1, maxRows));
            var columnCount = Math.Min(Convert.ToInt32(range.Columns.Count), Math.Max(1, maxColumns));

            for (var row = 1; row <= rowCount; row++)
            {
                var rowValues = new string[columnCount];

                for (var column = 1; column <= columnCount; column++)
                {
                    ExcelInterop.Range cell = null;

                    try
                    {
                        cell = range.Cells[row, column] as ExcelInterop.Range;
                        rowValues[column - 1] = cell != null
                            ? (Convert.ToString(cell.Text) ?? string.Empty)
                            : string.Empty;
                    }
                    finally
                    {
                        ReleaseComObject(cell);
                    }
                }

                rows.Add(rowValues);
            }

            return rows;
        }

        private static string GetFirstCellNumberFormat(ExcelInterop.Range range)
        {
            if (range == null)
            {
                return string.Empty;
            }

            ExcelInterop.Range firstCell = null;

            try
            {
                firstCell = range.Cells[1, 1] as ExcelInterop.Range;
                if (firstCell == null)
                {
                    firstCell = range;
                }

                return Convert.ToString(firstCell.NumberFormat) ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
            finally
            {
                if (!ReferenceEquals(firstCell, range))
                {
                    ReleaseComObject(firstCell);
                }
            }
        }

        private static string GetHyperlinkDisplayText(ExcelInterop.Hyperlink hyperlink, ExcelInterop.Range hyperlinkRange)
        {
            try
            {
                var textToDisplay = hyperlink != null
                    ? (Convert.ToString(hyperlink.TextToDisplay) ?? string.Empty)
                    : string.Empty;

                if (!string.IsNullOrWhiteSpace(textToDisplay))
                {
                    return textToDisplay;
                }
            }
            catch
            {
            }

            try
            {
                var rangeText = hyperlinkRange != null
                    ? (Convert.ToString(hyperlinkRange.Text) ?? string.Empty)
                    : string.Empty;

                if (!string.IsNullOrWhiteSpace(rangeText))
                {
                    return rangeText;
                }
            }
            catch
            {
            }

            try
            {
                var address = hyperlink != null ? hyperlink.Address ?? string.Empty : string.Empty;
                if (!string.IsNullOrWhiteSpace(address))
                {
                    return address;
                }
            }
            catch
            {
            }

            try
            {
                var subAddress = hyperlink != null ? hyperlink.SubAddress ?? string.Empty : string.Empty;
                if (!string.IsNullOrWhiteSpace(subAddress))
                {
                    return subAddress;
                }
            }
            catch
            {
            }

            return string.Empty;
        }

        private static string MapValidationType(int validationType)
        {
            switch ((ExcelInterop.XlDVType)validationType)
            {
                case ExcelInterop.XlDVType.xlValidateInputOnly:
                    return "input-only";
                case ExcelInterop.XlDVType.xlValidateWholeNumber:
                    return "whole-number";
                case ExcelInterop.XlDVType.xlValidateDecimal:
                    return "decimal";
                case ExcelInterop.XlDVType.xlValidateList:
                    return "list";
                case ExcelInterop.XlDVType.xlValidateDate:
                    return "date";
                case ExcelInterop.XlDVType.xlValidateTime:
                    return "time";
                case ExcelInterop.XlDVType.xlValidateTextLength:
                    return "text-length";
                case ExcelInterop.XlDVType.xlValidateCustom:
                    return "custom";
                default:
                    return "unknown(" + validationType + ")";
            }
        }

        private static string MapValidationOperator(int validationOperator)
        {
            switch ((ExcelInterop.XlFormatConditionOperator)validationOperator)
            {
                case ExcelInterop.XlFormatConditionOperator.xlBetween:
                    return "between";
                case ExcelInterop.XlFormatConditionOperator.xlNotBetween:
                    return "not-between";
                case ExcelInterop.XlFormatConditionOperator.xlEqual:
                    return "equal";
                case ExcelInterop.XlFormatConditionOperator.xlNotEqual:
                    return "not-equal";
                case ExcelInterop.XlFormatConditionOperator.xlGreater:
                    return "greater-than";
                case ExcelInterop.XlFormatConditionOperator.xlLess:
                    return "less-than";
                case ExcelInterop.XlFormatConditionOperator.xlGreaterEqual:
                    return "greater-than-or-equal";
                case ExcelInterop.XlFormatConditionOperator.xlLessEqual:
                    return "less-than-or-equal";
                default:
                    return string.Empty;
            }
        }

        private static string MapValidationAlertStyle(int alertStyle)
        {
            switch ((ExcelInterop.XlDVAlertStyle)alertStyle)
            {
                case ExcelInterop.XlDVAlertStyle.xlValidAlertStop:
                    return "stop";
                case ExcelInterop.XlDVAlertStyle.xlValidAlertWarning:
                    return "warning";
                case ExcelInterop.XlDVAlertStyle.xlValidAlertInformation:
                    return "information";
                default:
                    return "unknown(" + alertStyle + ")";
            }
        }

        private static List<string[]> BuildNumberFormatRows(ExcelInterop.Range range, int maxRows, int maxColumns)
        {
            var rows = new List<string[]>();
            if (range == null)
            {
                return rows;
            }

            var rowCount = Math.Min(Convert.ToInt32(range.Rows.Count), Math.Max(1, maxRows));
            var columnCount = Math.Min(Convert.ToInt32(range.Columns.Count), Math.Max(1, maxColumns));

            for (var row = 1; row <= rowCount; row++)
            {
                var rowValues = new string[columnCount];

                for (var column = 1; column <= columnCount; column++)
                {
                    ExcelInterop.Range cell = null;

                    try
                    {
                        cell = range.Cells[row, column] as ExcelInterop.Range;
                        rowValues[column - 1] = cell != null
                            ? (Convert.ToString(cell.NumberFormat) ?? string.Empty)
                            : string.Empty;
                    }
                    finally
                    {
                        ReleaseComObject(cell);
                    }
                }

                rows.Add(rowValues);
            }

            return rows;
        }

        public ExcelRangeFormulaWriteResult SetRangeFormulas(ExcelRangeFormulaWriteRequest request)
        {
            request = request ?? new ExcelRangeFormulaWriteRequest();

            var workbook = GetRequiredWorkbook();
            EnsureWorkbookWritable(workbook);

            var worksheet = ResolveWorksheet(workbook, request.WorksheetName);
            var range = ResolveWriteRange(worksheet, request.Address, request.UseSelectionWhenAddressEmpty);
            if (range == null)
            {
                throw new InvalidOperationException("No target range was found for the formula write.");
            }

            var hasMatrix = request.Formulas != null && request.Formulas.Count > 0;
            if (!hasMatrix && string.IsNullOrWhiteSpace(request.Formula))
            {
                throw new InvalidOperationException("The formula write request is missing 'formula' or 'formulas'.");
            }

            ExcelInterop.Range targetRange = null;
            var formulaKind = hasMatrix ? "matrix" : "single";
            var preview = BuildFormulaPreview(request);

            try
            {
                if (hasMatrix)
                {
                    var rowCount = request.Formulas.Count;
                    var columnCount = GetMatrixColumnCount(request.Formulas);
                    if (columnCount <= 0)
                    {
                        throw new InvalidOperationException("The 'formulas' matrix in the formula write request must not be empty.");
                    }

                    targetRange = ResizeRangeForWrite(range, rowCount, columnCount);
                }
                else
                {
                    targetRange = range;
                }

                try
                {
                    if (hasMatrix)
                    {
                        targetRange.Formula = BuildExcelFormulaMatrix(request.Formulas);
                    }
                    else
                    {
                        targetRange.Formula = NormalizeFormulaText(request.Formula);
                    }

                    return new ExcelRangeFormulaWriteResult
                    {
                        WorkbookName = workbook.Name ?? string.Empty,
                        WorksheetName = GetWorksheetName(worksheet),
                        Address = GetAddress(targetRange),
                        RowCount = Convert.ToInt32(targetRange.Rows.Count),
                        ColumnCount = Convert.ToInt32(targetRange.Columns.Count),
                        WrittenCellCount = Convert.ToInt32(targetRange.Rows.Count) * Convert.ToInt32(targetRange.Columns.Count),
                        FormulaKind = formulaKind,
                        Preview = preview,
                        AppliedBy = "interop",
                        FallbackUsed = false
                    };
                }
                catch (Exception primaryEx)
                {
                    if (!request.FallbackToVba)
                    {
                        throw;
                    }

                    try
                    {
                        var vbaResult = _vbaExecutor.ExecuteVba(new VbaExecutionRequest
                        {
                            Code = BuildSetRangeFormulasVbaCode(GetWorksheetName(worksheet), targetRange, request),
                            RemoveModuleAfterRun = true
                        });

                        return new ExcelRangeFormulaWriteResult
                        {
                            WorkbookName = workbook.Name ?? string.Empty,
                            WorksheetName = GetWorksheetName(worksheet),
                            Address = GetAddress(targetRange),
                            RowCount = Convert.ToInt32(targetRange.Rows.Count),
                            ColumnCount = Convert.ToInt32(targetRange.Columns.Count),
                            WrittenCellCount = Convert.ToInt32(targetRange.Rows.Count) * Convert.ToInt32(targetRange.Columns.Count),
                            FormulaKind = formulaKind,
                            Preview = preview,
                            AppliedBy = "vba",
                            FallbackUsed = true,
                            VbaModuleName = vbaResult == null ? string.Empty : vbaResult.ModuleName ?? string.Empty,
                            VbaEntryPoint = vbaResult == null ? string.Empty : vbaResult.EntryPoint ?? string.Empty
                        };
                    }
                    catch (Exception fallbackEx)
                    {
                        throw new InvalidOperationException(
                            "Failed to apply the formula, and the VBA fallback also failed. Interop: " + primaryEx.Message + "; VBA: " + fallbackEx.Message,
                            fallbackEx);
                    }
                }
            }
            finally
            {
                if (!ReferenceEquals(targetRange, range))
                {
                    ReleaseComObject(targetRange);
                }
            }
        }

        public ExcelRangeFormatWriteResult SetRangeFormat(ExcelRangeFormatWriteRequest request)
        {
            request = request ?? new ExcelRangeFormatWriteRequest();

            if (!HasRangeFormatPayload(request))
            {
                throw new InvalidOperationException("The format write request did not provide any applicable format properties.");
            }

            var workbook = GetRequiredWorkbook();
            EnsureWorkbookWritable(workbook);

            var worksheet = ResolveWorksheet(workbook, request.WorksheetName);
            var range = ResolveWriteRange(worksheet, request.Address, request.UseSelectionWhenAddressEmpty);
            if (range == null)
            {
                throw new InvalidOperationException("No target range was found for the format write.");
            }

            try
            {
                try
                {
                    return ApplyRangeFormatWithInterop(workbook, worksheet, range, request);
                }
                catch (Exception primaryEx)
                {
                    if (!request.FallbackToVba)
                    {
                        throw;
                    }

                    try
                    {
                        var vbaResult = _vbaExecutor.ExecuteVba(new VbaExecutionRequest
                        {
                            Code = BuildSetRangeFormatVbaCode(GetWorksheetName(worksheet), GetAddress(range), request),
                            RemoveModuleAfterRun = true
                        });

                        var summary = BuildRangeFormatSummary(request);
                        return new ExcelRangeFormatWriteResult
                        {
                            WorkbookName = workbook.Name ?? string.Empty,
                            WorksheetName = GetWorksheetName(worksheet),
                            Address = GetAddress(range),
                            RowCount = Convert.ToInt32(range.Rows.Count),
                            ColumnCount = Convert.ToInt32(range.Columns.Count),
                            AppliedPropertyCount = CountRangeFormatProperties(request),
                            Summary = summary,
                            AppliedBy = "vba",
                            FallbackUsed = true,
                            VbaModuleName = vbaResult == null ? string.Empty : vbaResult.ModuleName ?? string.Empty,
                            VbaEntryPoint = vbaResult == null ? string.Empty : vbaResult.EntryPoint ?? string.Empty
                        };
                    }
                    catch (Exception fallbackEx)
                    {
                        throw new InvalidOperationException(
                            "Failed to apply the format, and the VBA fallback also failed. Interop: " + primaryEx.Message + "; VBA: " + fallbackEx.Message,
                            fallbackEx);
                    }
                }
            }
            finally
            {
            }
        }

        private static ExcelRangeFormatWriteResult ApplyRangeFormatWithInterop(
            ExcelInterop.Workbook workbook,
            ExcelInterop.Worksheet worksheet,
            ExcelInterop.Range range,
            ExcelRangeFormatWriteRequest request)
        {
            ExcelInterop.Font font = null;
            ExcelInterop.Interior interior = null;

            try
            {
                if (!string.IsNullOrWhiteSpace(request.NumberFormat))
                {
                    range.NumberFormat = request.NumberFormat.Trim();
                }

                if (!string.IsNullOrWhiteSpace(request.StyleName))
                {
                    range.Style = request.StyleName.Trim();
                }

                if (!string.IsNullOrWhiteSpace(request.HorizontalAlignment))
                {
                    range.HorizontalAlignment = MapHorizontalAlignment(request.HorizontalAlignment);
                }

                if (!string.IsNullOrWhiteSpace(request.VerticalAlignment))
                {
                    range.VerticalAlignment = MapVerticalAlignment(request.VerticalAlignment);
                }

                if (request.WrapText.HasValue)
                {
                    range.WrapText = request.WrapText.Value;
                }

                if (request.ShrinkToFit.HasValue)
                {
                    range.ShrinkToFit = request.ShrinkToFit.Value;
                }

                if (NeedsFontMutation(request))
                {
                    font = range.Font;

                    if (!string.IsNullOrWhiteSpace(request.FontName))
                    {
                        font.Name = request.FontName.Trim();
                    }

                    if (request.FontSize.HasValue)
                    {
                        font.Size = request.FontSize.Value;
                    }

                    if (request.Bold.HasValue)
                    {
                        font.Bold = request.Bold.Value;
                    }

                    if (request.Italic.HasValue)
                    {
                        font.Italic = request.Italic.Value;
                    }

                    if (request.Underline.HasValue)
                    {
                        font.Underline = request.Underline.Value
                            ? ExcelInterop.XlUnderlineStyle.xlUnderlineStyleSingle
                            : ExcelInterop.XlUnderlineStyle.xlUnderlineStyleNone;
                    }

                    if (!string.IsNullOrWhiteSpace(request.FontColor))
                    {
                        font.Color = ParseExcelColor(request.FontColor);
                    }
                }

                if (!string.IsNullOrWhiteSpace(request.FillColor))
                {
                    interior = range.Interior;
                    interior.Color = ParseExcelColor(request.FillColor);
                }

                return new ExcelRangeFormatWriteResult
                {
                    WorkbookName = workbook.Name ?? string.Empty,
                    WorksheetName = GetWorksheetName(worksheet),
                    Address = GetAddress(range),
                    RowCount = Convert.ToInt32(range.Rows.Count),
                    ColumnCount = Convert.ToInt32(range.Columns.Count),
                    AppliedPropertyCount = CountRangeFormatProperties(request),
                    Summary = BuildRangeFormatSummary(request),
                    AppliedBy = "interop",
                    FallbackUsed = false
                };
            }
            finally
            {
                ReleaseComObject(interior);
                ReleaseComObject(font);
            }
        }

        private static bool HasRangeFormatPayload(ExcelRangeFormatWriteRequest request)
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

        private static bool NeedsFontMutation(ExcelRangeFormatWriteRequest request)
        {
            if (request == null)
            {
                return false;
            }

            return !string.IsNullOrWhiteSpace(request.FontName)
                || request.FontSize.HasValue
                || request.Bold.HasValue
                || request.Italic.HasValue
                || request.Underline.HasValue
                || !string.IsNullOrWhiteSpace(request.FontColor);
        }

        private static int CountRangeFormatProperties(ExcelRangeFormatWriteRequest request)
        {
            if (request == null)
            {
                return 0;
            }

            var count = 0;

            if (!string.IsNullOrWhiteSpace(request.NumberFormat))
            {
                count++;
            }

            if (!string.IsNullOrWhiteSpace(request.StyleName))
            {
                count++;
            }

            if (!string.IsNullOrWhiteSpace(request.HorizontalAlignment))
            {
                count++;
            }

            if (!string.IsNullOrWhiteSpace(request.VerticalAlignment))
            {
                count++;
            }

            if (request.WrapText.HasValue)
            {
                count++;
            }

            if (request.ShrinkToFit.HasValue)
            {
                count++;
            }

            if (!string.IsNullOrWhiteSpace(request.FontName))
            {
                count++;
            }

            if (request.FontSize.HasValue)
            {
                count++;
            }

            if (request.Bold.HasValue)
            {
                count++;
            }

            if (request.Italic.HasValue)
            {
                count++;
            }

            if (request.Underline.HasValue)
            {
                count++;
            }

            if (!string.IsNullOrWhiteSpace(request.FontColor))
            {
                count++;
            }

            if (!string.IsNullOrWhiteSpace(request.FillColor))
            {
                count++;
            }

            return count;
        }

        private static string BuildRangeFormatSummary(ExcelRangeFormatWriteRequest request)
        {
            var parts = new List<string>();

            if (!string.IsNullOrWhiteSpace(request.NumberFormat))
            {
                parts.Add("numberFormat=" + request.NumberFormat.Trim());
            }

            if (!string.IsNullOrWhiteSpace(request.StyleName))
            {
                parts.Add("styleName=" + request.StyleName.Trim());
            }

            if (!string.IsNullOrWhiteSpace(request.HorizontalAlignment))
            {
                parts.Add("horizontalAlignment=" + request.HorizontalAlignment.Trim());
            }

            if (!string.IsNullOrWhiteSpace(request.VerticalAlignment))
            {
                parts.Add("verticalAlignment=" + request.VerticalAlignment.Trim());
            }

            if (request.WrapText.HasValue)
            {
                parts.Add("wrapText=" + (request.WrapText.Value ? "true" : "false"));
            }

            if (request.ShrinkToFit.HasValue)
            {
                parts.Add("shrinkToFit=" + (request.ShrinkToFit.Value ? "true" : "false"));
            }

            if (!string.IsNullOrWhiteSpace(request.FontName))
            {
                parts.Add("fontName=" + request.FontName.Trim());
            }

            if (request.FontSize.HasValue)
            {
                parts.Add("fontSize=" + request.FontSize.Value.ToString(CultureInfo.InvariantCulture));
            }

            if (request.Bold.HasValue)
            {
                parts.Add("bold=" + (request.Bold.Value ? "true" : "false"));
            }

            if (request.Italic.HasValue)
            {
                parts.Add("italic=" + (request.Italic.Value ? "true" : "false"));
            }

            if (request.Underline.HasValue)
            {
                parts.Add("underline=" + (request.Underline.Value ? "true" : "false"));
            }

            if (!string.IsNullOrWhiteSpace(request.FontColor))
            {
                parts.Add("fontColor=" + request.FontColor.Trim());
            }

            if (!string.IsNullOrWhiteSpace(request.FillColor))
            {
                parts.Add("fillColor=" + request.FillColor.Trim());
            }

            return parts.Count == 0 ? string.Empty : string.Join("; ", parts.ToArray());
        }

        private static Array BuildExcelFormulaMatrix(IList<string[]> formulas)
        {
            var rowCount = formulas == null ? 0 : formulas.Count;
            var columnCount = GetMatrixColumnCount(formulas);
            var matrix = Array.CreateInstance(typeof(object), new[] { rowCount, columnCount }, new[] { 1, 1 });

            for (var rowIndex = 0; rowIndex < rowCount; rowIndex++)
            {
                var row = formulas[rowIndex];
                for (var columnIndex = 0; columnIndex < columnCount; columnIndex++)
                {
                    var formula = row != null && columnIndex < row.Length
                        ? NormalizeFormulaText(row[columnIndex])
                        : string.Empty;

                    matrix.SetValue(formula, rowIndex + 1, columnIndex + 1);
                }
            }

            return matrix;
        }

        private string BuildSetRangeFormulasVbaCode(
            string worksheetName,
            ExcelInterop.Range targetRange,
            ExcelRangeFormulaWriteRequest request)
        {
            var builder = new StringBuilder();
            builder.AppendLine("Public Sub Copilot_Run()");
            builder.AppendLine("    Dim ws As Worksheet");
            builder.AppendLine("    Set ws = ThisWorkbook.Worksheets(" + ToVbaStringLiteral(worksheetName) + ")");

            if (request != null && request.Formulas != null && request.Formulas.Count > 0)
            {
                for (var rowIndex = 0; rowIndex < request.Formulas.Count; rowIndex++)
                {
                    var row = request.Formulas[rowIndex];
                    var columnCount = row == null ? 0 : row.Length;

                    for (var columnIndex = 0; columnIndex < columnCount; columnIndex++)
                    {
                        ExcelInterop.Range cell = null;

                        try
                        {
                            cell = targetRange.Cells[rowIndex + 1, columnIndex + 1] as ExcelInterop.Range;
                            var cellAddress = GetAddress(cell);
                            builder.AppendLine(
                                "    ws.Range(" + ToVbaStringLiteral(cellAddress) + ").Formula = "
                                + ToVbaStringLiteral(NormalizeFormulaText(row[columnIndex])));
                        }
                        finally
                        {
                            ReleaseComObject(cell);
                        }
                    }
                }
            }
            else
            {
                builder.AppendLine(
                    "    ws.Range(" + ToVbaStringLiteral(GetAddress(targetRange)) + ").Formula = "
                    + ToVbaStringLiteral(NormalizeFormulaText(request == null ? string.Empty : request.Formula)));
            }

            builder.AppendLine("End Sub");
            return builder.ToString();
        }

        private static string BuildSetRangeFormatVbaCode(
            string worksheetName,
            string address,
            ExcelRangeFormatWriteRequest request)
        {
            var builder = new StringBuilder();
            builder.AppendLine("Public Sub Copilot_Run()");
            builder.AppendLine("    Dim ws As Worksheet");
            builder.AppendLine("    Dim target As Range");
            builder.AppendLine("    Set ws = ThisWorkbook.Worksheets(" + ToVbaStringLiteral(worksheetName) + ")");
            builder.AppendLine("    Set target = ws.Range(" + ToVbaStringLiteral(address) + ")");

            if (!string.IsNullOrWhiteSpace(request.NumberFormat))
            {
                builder.AppendLine("    target.NumberFormat = " + ToVbaStringLiteral(request.NumberFormat.Trim()));
            }

            if (!string.IsNullOrWhiteSpace(request.StyleName))
            {
                builder.AppendLine("    target.Style = " + ToVbaStringLiteral(request.StyleName.Trim()));
            }

            if (!string.IsNullOrWhiteSpace(request.HorizontalAlignment))
            {
                builder.AppendLine("    target.HorizontalAlignment = " + MapHorizontalAlignmentVbaConstant(request.HorizontalAlignment));
            }

            if (!string.IsNullOrWhiteSpace(request.VerticalAlignment))
            {
                builder.AppendLine("    target.VerticalAlignment = " + MapVerticalAlignmentVbaConstant(request.VerticalAlignment));
            }

            if (request.WrapText.HasValue)
            {
                builder.AppendLine("    target.WrapText = " + (request.WrapText.Value ? "True" : "False"));
            }

            if (request.ShrinkToFit.HasValue)
            {
                builder.AppendLine("    target.ShrinkToFit = " + (request.ShrinkToFit.Value ? "True" : "False"));
            }

            if (!string.IsNullOrWhiteSpace(request.FontName))
            {
                builder.AppendLine("    target.Font.Name = " + ToVbaStringLiteral(request.FontName.Trim()));
            }

            if (request.FontSize.HasValue)
            {
                builder.AppendLine(
                    "    target.Font.Size = " + request.FontSize.Value.ToString(CultureInfo.InvariantCulture));
            }

            if (request.Bold.HasValue)
            {
                builder.AppendLine("    target.Font.Bold = " + (request.Bold.Value ? "True" : "False"));
            }

            if (request.Italic.HasValue)
            {
                builder.AppendLine("    target.Font.Italic = " + (request.Italic.Value ? "True" : "False"));
            }

            if (request.Underline.HasValue)
            {
                builder.AppendLine(
                    "    target.Font.Underline = "
                    + (request.Underline.Value ? "xlUnderlineStyleSingle" : "xlUnderlineStyleNone"));
            }

            if (!string.IsNullOrWhiteSpace(request.FontColor))
            {
                builder.AppendLine("    target.Font.Color = " + BuildVbaColorExpression(request.FontColor));
            }

            if (!string.IsNullOrWhiteSpace(request.FillColor))
            {
                builder.AppendLine("    target.Interior.Color = " + BuildVbaColorExpression(request.FillColor));
            }

            builder.AppendLine("End Sub");
            return builder.ToString();
        }

        private static string ToVbaStringLiteral(string value)
        {
            return "\"" + ((value ?? string.Empty).Replace("\"", "\"\"")) + "\"";
        }

        private static int ParseExcelColor(string value)
        {
            var color = ParseColorValue(value);
            return ColorTranslator.ToOle(color);
        }

        private static string BuildVbaColorExpression(string value)
        {
            var color = ParseColorValue(value);
            return string.Format(
                CultureInfo.InvariantCulture,
                "RGB({0}, {1}, {2})",
                color.R,
                color.G,
                color.B);
        }

        /// <summary>
        /// 宽松解析颜色输入。兼容大模型常见的各种写法：
        /// "#2E7D32"、"2E7D32"、"0x2E7D32"、"&H2E7D32"、"#FFF"、"FF2E7D32"(带 alpha)、
        /// "rgb(46,125,50)"、"46,125,50"、"Red"/"LightGray" 等英文色名、"红色"/"浅灰" 等中文色名、
        /// 以及十进制 OLE 颜色值。解析失败时抛出带示例的错误信息，便于模型自我纠正。
        /// </summary>
        private static Color ParseColorValue(string value)
        {
            Color color;
            if (!TryParseColorValue(value, out color))
            {
                throw new InvalidOperationException(
                    "颜色格式无效：" + (value ?? string.Empty)
                    + "。请使用 6 位十六进制 RGB（推荐带 # 前缀，例如 \"#2E7D32\"、\"#FFFFFF\"）、"
                    + "\"rgb(46,125,50)\" 或英文/中文颜色名（如 \"Red\"、\"红色\"）。");
            }

            return color;
        }

        private static bool TryParseColorValue(string value, out Color color)
        {
            color = Color.Empty;
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            var text = value.Trim();

            // 去掉模型可能带上的引号
            if (text.Length >= 2
                && ((text[0] == '"' && text[text.Length - 1] == '"')
                    || (text[0] == '\'' && text[text.Length - 1] == '\'')))
            {
                text = text.Substring(1, text.Length - 2).Trim();
            }

            if (text.Length == 0)
            {
                return false;
            }

            // rgb(r,g,b) / rgba(r,g,b,a)
            if (text.StartsWith("rgb", StringComparison.OrdinalIgnoreCase))
            {
                var open = text.IndexOf('(');
                var close = text.LastIndexOf(')');
                if (open > 0 && close > open)
                {
                    return TryParseRgbComponents(text.Substring(open + 1, close - open - 1), out color);
                }
            }

            // 裸的 "46,125,50"
            if (text.IndexOf(',') >= 0 && TryParseRgbComponents(text, out color))
            {
                return true;
            }

            var hex = text;
            var hasExplicitHexPrefix = false;
            if (hex.StartsWith("#", StringComparison.Ordinal))
            {
                hex = hex.Substring(1);
                hasExplicitHexPrefix = true;
            }
            else if (hex.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            {
                hex = hex.Substring(2);
                hasExplicitHexPrefix = true;
            }
            else if (hex.StartsWith("&H", StringComparison.OrdinalIgnoreCase))
            {
                hex = hex.Substring(2);
                hasExplicitHexPrefix = true;
            }

            hex = hex.Trim();

            var isHexDigits = IsHexDigits(hex);
            var hasHexLetter = isHexDigits && ContainsHexLetter(hex);
            var hexLengthOk = hex.Length == 3 || hex.Length == 6 || hex.Length == 8;

            // 优先按十六进制解析的情况：显式前缀，或包含 A-F 字母，或以 0 开头（"003366" 这类必然是色值）
            if (isHexDigits
                && (hasExplicitHexPrefix || (hexLengthOk && (hasHexLetter || hex[0] == '0'))))
            {
                if (TryParseHexColor(hex, out color))
                {
                    return true;
                }
            }

            // 十进制 OLE / 长整型颜色值（例如 VBA 里的 3329330）
            int numeric;
            if (!hasExplicitHexPrefix
                && int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out numeric)
                && numeric >= 0
                && numeric <= 0xFFFFFF)
            {
                color = ColorTranslator.FromOle(numeric);
                return true;
            }

            // 纯数字但长度像色值的兜底（例如 "123456"）
            if (isHexDigits && (hex.Length == 6 || hex.Length == 8) && TryParseHexColor(hex, out color))
            {
                return true;
            }

            var named = TryParseNamedColor(text);
            if (named.HasValue)
            {
                color = named.Value;
                return true;
            }

            try
            {
                color = ColorTranslator.FromHtml(text);
                return true;
            }
            catch
            {
                color = Color.Empty;
                return false;
            }
        }

        private static bool TryParseHexColor(string hex, out Color color)
        {
            color = Color.Empty;

            if (hex.Length == 3)
            {
                hex = new string(new[] { hex[0], hex[0], hex[1], hex[1], hex[2], hex[2] });
            }
            else if (hex.Length == 8)
            {
                // 8 位按 AARRGGBB 处理，直接丢弃 alpha
                hex = hex.Substring(2);
            }
            else if (hex.Length < 6)
            {
                hex = hex.PadLeft(6, '0');
            }

            if (hex.Length != 6)
            {
                return false;
            }

            int rgb;
            if (!int.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out rgb))
            {
                return false;
            }

            color = Color.FromArgb((rgb >> 16) & 0xFF, (rgb >> 8) & 0xFF, rgb & 0xFF);
            return true;
        }

        private static bool TryParseRgbComponents(string text, out Color color)
        {
            color = Color.Empty;
            var parts = (text ?? string.Empty).Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 3)
            {
                return false;
            }

            var channels = new int[3];
            for (var i = 0; i < 3; i++)
            {
                double component;
                if (!double.TryParse(parts[i].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out component))
                {
                    return false;
                }

                // 兼容 0~1 归一化写法
                if (component > 0 && component <= 1 && parts[i].IndexOf('.') >= 0)
                {
                    component *= 255d;
                }

                var rounded = (int)Math.Round(component, MidpointRounding.AwayFromZero);
                if (rounded < 0 || rounded > 255)
                {
                    return false;
                }

                channels[i] = rounded;
            }

            color = Color.FromArgb(channels[0], channels[1], channels[2]);
            return true;
        }

        private static bool IsHexDigits(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return false;
            }

            for (var i = 0; i < text.Length; i++)
            {
                var c = text[i];
                var isHex = (c >= '0' && c <= '9')
                    || (c >= 'a' && c <= 'f')
                    || (c >= 'A' && c <= 'F');
                if (!isHex)
                {
                    return false;
                }
            }

            return true;
        }

        private static bool ContainsHexLetter(string text)
        {
            for (var i = 0; i < text.Length; i++)
            {
                var c = text[i];
                if ((c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F'))
                {
                    return true;
                }
            }

            return false;
        }

        private static Color? TryParseNamedColor(string text)
        {
            var key = text.Replace(" ", string.Empty).Replace("-", string.Empty).Replace("_", string.Empty);

            string chineseHex;
            if (ChineseColorNames.TryGetValue(key, out chineseHex))
            {
                Color chineseColor;
                if (TryParseHexColor(chineseHex, out chineseColor))
                {
                    return chineseColor;
                }
            }

            var known = Color.FromName(key);
            if (known.IsKnownColor)
            {
                return Color.FromArgb(known.R, known.G, known.B);
            }

            return null;
        }

        private static readonly Dictionary<string, string> ChineseColorNames =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "黑", "000000" }, { "黑色", "000000" },
                { "白", "FFFFFF" }, { "白色", "FFFFFF" },
                { "红", "FF0000" }, { "红色", "FF0000" }, { "深红", "C00000" }, { "浅红", "FF9999" },
                { "绿", "00B050" }, { "绿色", "00B050" }, { "深绿", "1E7145" }, { "浅绿", "C6EFCE" },
                { "蓝", "0070C0" }, { "蓝色", "0070C0" }, { "深蓝", "002060" }, { "浅蓝", "BDD7EE" },
                { "黄", "FFFF00" }, { "黄色", "FFFF00" }, { "浅黄", "FFEB9C" },
                { "橙", "ED7D31" }, { "橙色", "ED7D31" },
                { "紫", "7030A0" }, { "紫色", "7030A0" },
                { "青", "00B0F0" }, { "青色", "00B0F0" },
                { "粉", "FFC0CB" }, { "粉色", "FFC0CB" },
                { "灰", "808080" }, { "灰色", "808080" }, { "深灰", "404040" }, { "浅灰", "D9D9D9" },
                { "棕", "843C0C" }, { "棕色", "843C0C" },
                { "金", "FFD700" }, { "金色", "FFD700" },
                { "银", "C0C0C0" }, { "银色", "C0C0C0" },
                { "透明", "FFFFFF" }, { "无", "FFFFFF" }
            };

        private static int MapHorizontalAlignment(string value)
        {
            switch (NormalizeOption(value))
            {
                case "general":
                    return (int)ExcelInterop.XlHAlign.xlHAlignGeneral;
                case "left":
                    return (int)ExcelInterop.XlHAlign.xlHAlignLeft;
                case "center":
                    return (int)ExcelInterop.XlHAlign.xlHAlignCenter;
                case "right":
                    return (int)ExcelInterop.XlHAlign.xlHAlignRight;
                case "fill":
                    return (int)ExcelInterop.XlHAlign.xlHAlignFill;
                case "justify":
                    return (int)ExcelInterop.XlHAlign.xlHAlignJustify;
                case "center-across-selection":
                case "centercontinuous":
                    return (int)ExcelInterop.XlHAlign.xlHAlignCenterAcrossSelection;
                case "distributed":
                    return (int)ExcelInterop.XlHAlign.xlHAlignDistributed;
                default:
                    throw new InvalidOperationException("Unsupported horizontal alignment: " + value);
            }
        }

        private static int MapVerticalAlignment(string value)
        {
            switch (NormalizeOption(value))
            {
                case "top":
                    return (int)ExcelInterop.XlVAlign.xlVAlignTop;
                case "center":
                    return (int)ExcelInterop.XlVAlign.xlVAlignCenter;
                case "bottom":
                    return (int)ExcelInterop.XlVAlign.xlVAlignBottom;
                case "justify":
                    return (int)ExcelInterop.XlVAlign.xlVAlignJustify;
                case "distributed":
                    return (int)ExcelInterop.XlVAlign.xlVAlignDistributed;
                default:
                    throw new InvalidOperationException("Unsupported vertical alignment: " + value);
            }
        }

        private static string MapHorizontalAlignmentVbaConstant(string value)
        {
            switch (NormalizeOption(value))
            {
                case "general":
                    return "xlHAlignGeneral";
                case "left":
                    return "xlHAlignLeft";
                case "center":
                    return "xlHAlignCenter";
                case "right":
                    return "xlHAlignRight";
                case "fill":
                    return "xlHAlignFill";
                case "justify":
                    return "xlHAlignJustify";
                case "center-across-selection":
                case "centercontinuous":
                    return "xlHAlignCenterAcrossSelection";
                case "distributed":
                    return "xlHAlignDistributed";
                default:
                    throw new InvalidOperationException("Unsupported horizontal alignment: " + value);
            }
        }

        private static string MapVerticalAlignmentVbaConstant(string value)
        {
            switch (NormalizeOption(value))
            {
                case "top":
                    return "xlVAlignTop";
                case "center":
                    return "xlVAlignCenter";
                case "bottom":
                    return "xlVAlignBottom";
                case "justify":
                    return "xlVAlignJustify";
                case "distributed":
                    return "xlVAlignDistributed";
                default:
                    throw new InvalidOperationException("Unsupported vertical alignment: " + value);
            }
        }

        private static string NormalizeOption(string value)
        {
            return string.IsNullOrWhiteSpace(value)
                ? string.Empty
                : value.Trim().Replace("_", "-").Replace(" ", "-").ToLowerInvariant();
        }
    }
}
