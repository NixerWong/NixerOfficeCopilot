using System;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;

namespace OfficeCopilot.Eval.Excel
{
    internal sealed class ExcelAutomationSession : IDisposable
    {
        private object _application;
        private bool _disposed;

        public ExcelAutomationSession()
        {
            var excelType = Type.GetTypeFromProgID("Excel.Application", false);
            if (excelType == null)
            {
                throw new InvalidOperationException(
                    "Microsoft Excel is not installed or its COM automation server is not registered.");
            }

            _application = Activator.CreateInstance(excelType);
            if (_application == null)
            {
                throw new InvalidOperationException("Excel COM automation did not create an Application object.");
            }

            dynamic application = _application;
            application.Visible = true;
            application.DisplayAlerts = false;
            application.AutomationSecurity = 3;

            var windowHandle = new IntPtr(Convert.ToInt64(application.Hwnd));
            uint processId;
            GetWindowThreadProcessId(windowHandle, out processId);
            if (processId == 0)
            {
                throw new InvalidOperationException(
                    "Could not determine the Excel process that owns the automation window.");
            }

            ProcessId = checked((int)processId);
        }

        public int ProcessId { get; private set; }

        public ExcelWorkbookSession OpenReadOnly(string path)
        {
            return OpenWorkbook(path, true);
        }

        public ExcelWorkbookSession OpenWorkbook(string path, bool readOnly)
        {
            ThrowIfDisposed();
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new ArgumentException("A workbook path is required.", nameof(path));
            }

            dynamic application = _application;
            dynamic workbooks = application.Workbooks;
            try
            {
                var missing = Type.Missing;
                object workbook = workbooks.Open(
                    path,
                    0,
                    readOnly,
                    missing,
                    missing,
                    missing,
                    true,
                    missing,
                    missing,
                    false,
                    false,
                    missing,
                    false,
                    true,
                    0);
                if (workbook == null)
                {
                    throw new InvalidOperationException("Excel returned no workbook when opening: " + path);
                }

                return new ExcelWorkbookSession(workbook);
            }
            finally
            {
                ReleaseComObject(workbooks);
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            if (_application == null)
            {
                return;
            }

            try
            {
                dynamic application = _application;
                application.Quit();
            }
            finally
            {
                ReleaseComObject(_application);
                _application = null;
            }
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(GetType().FullName);
            }
        }

        private static void ReleaseComObject(object value)
        {
            if (value != null && Marshal.IsComObject(value))
            {
                Marshal.FinalReleaseComObject(value);
            }
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint GetWindowThreadProcessId(IntPtr windowHandle, out uint processId);
    }

    internal sealed class ExcelWorkbookSession : IDisposable
    {
        private object _workbook;
        private bool _disposed;

        public ExcelWorkbookSession(object workbook)
        {
            _workbook = workbook ?? throw new ArgumentNullException(nameof(workbook));
        }

        internal object WorkbookObject
        {
            get
            {
                ThrowIfDisposed();
                return _workbook;
            }
        }

        public void SaveCopyAs(string path)
        {
            ThrowIfDisposed();
            dynamic workbook = _workbook;
            workbook.SaveCopyAs(path);
        }

        internal void ApplyFaultInjection(string faultInjection)
        {
            ThrowIfDisposed();
            switch (faultInjection)
            {
                case "corrupt-first-staged-total":
                    CorruptFirstStagedTotal();
                    return;
                case "omit-one-unmatched-order":
                    OmitLastUnmatchedOrder();
                    return;
                default:
                    throw new InvalidOperationException(
                        "Unsupported Excel Eval fault injection: " + (faultInjection ?? string.Empty));
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            if (_workbook == null)
            {
                return;
            }

            try
            {
                dynamic workbook = _workbook;
                workbook.Close(false);
            }
            finally
            {
                if (Marshal.IsComObject(_workbook))
                {
                    Marshal.FinalReleaseComObject(_workbook);
                }

                _workbook = null;
            }
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(GetType().FullName);
            }
        }

        private void CorruptFirstStagedTotal()
        {
            object worksheets = null;
            object worksheet = null;
            object cells = null;
            object cell = null;
            try
            {
                dynamic workbook = _workbook;
                worksheets = workbook.Worksheets;
                dynamic worksheetCollection = worksheets;
                worksheet = worksheetCollection.Item["_wip_01_summary"];
                dynamic targetWorksheet = worksheet;
                cells = targetWorksheet.Cells;
                dynamic cellCollection = cells;
                cell = cellCollection[2, 2];
                dynamic targetCell = cell;
                object rawValue = targetCell.Value2;
                double currentValue;
                if (!double.TryParse(
                    Convert.ToString(rawValue, CultureInfo.InvariantCulture),
                    NumberStyles.Float | NumberStyles.AllowThousands,
                    CultureInfo.InvariantCulture,
                    out currentValue))
                {
                    throw new InvalidOperationException(
                        "Fault injection could not find a numeric first staged total in _wip_01_summary!B2.");
                }

                targetCell.Value2 = currentValue + 1d;
            }
            finally
            {
                ReleaseComObject(cell);
                ReleaseComObject(cells);
                ReleaseComObject(worksheet);
                ReleaseComObject(worksheets);
            }
        }

        private void OmitLastUnmatchedOrder()
        {
            object worksheets = null;
            object worksheet = null;
            object cells = null;
            object rows = null;
            object lastCell = null;
            object lastKeyCell = null;
            object targetRow = null;
            try
            {
                dynamic workbook = _workbook;
                worksheets = workbook.Worksheets;
                dynamic worksheetCollection = worksheets;
                worksheet = worksheetCollection.Item["_wip_01_unmatched"];
                dynamic targetWorksheet = worksheet;
                cells = targetWorksheet.Cells;
                dynamic cellCollection = cells;
                rows = targetWorksheet.Rows;
                dynamic rowCollection = rows;
                var rowCount = Convert.ToInt32(rowCollection.Count, CultureInfo.InvariantCulture);
                lastCell = cellCollection[rowCount, 2];
                dynamic lastUsedCell = lastCell;
                lastKeyCell = lastUsedCell.End(-4162);
                dynamic lastKey = lastKeyCell;
                var lastRow = Convert.ToInt32(lastKey.Row, CultureInfo.InvariantCulture);
                if (lastRow < 2)
                {
                    throw new InvalidOperationException(
                        "Fault injection could not find an unmatched order data row in _wip_01_unmatched.");
                }

                targetRow = rowCollection[lastRow];
                dynamic row = targetRow;
                row.Delete();
            }
            finally
            {
                ReleaseComObject(targetRow);
                ReleaseComObject(lastKeyCell);
                ReleaseComObject(lastCell);
                ReleaseComObject(rows);
                ReleaseComObject(cells);
                ReleaseComObject(worksheet);
                ReleaseComObject(worksheets);
            }
        }

        private static void ReleaseComObject(object value)
        {
            if (value != null && Marshal.IsComObject(value))
            {
                Marshal.FinalReleaseComObject(value);
            }
        }
    }
}
