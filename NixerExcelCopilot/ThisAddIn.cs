using System;
using System.Diagnostics;
using OfficeCopilot.Localization;
using OfficeCopilot.Diagnostics.Logging;
using OfficeCopilot.Host.Excel;
using Excel = Microsoft.Office.Interop.Excel;
using Microsoft.Office.Tools.Excel;

namespace NixerExcelCopilot
{
    public partial class ThisAddIn
    {
        internal ExcelCopilotTaskPaneManager TaskPaneManager { get; private set; }

        private UiResponsivenessProbe _uiProbe;
        private System.Windows.Forms.Control _uiProbeAnchor;

        private void ThisAddIn_Startup(object sender, System.EventArgs e)
        {
            try
            {
                StartupTimingLog.Mark("excel-startup-enter");
                OfficeCopilot.Settings.WinForms.LocalizationBootstrap.Initialize();
                _uiProbeAnchor = new System.Windows.Forms.Control();
                _uiProbeAnchor.CreateControl();
                _uiProbe = UiResponsivenessProbe.Start(action => _uiProbeAnchor.BeginInvoke(action));
                OfficeCopilot.Settings.WinForms.HostFormIcon.Initialize(typeof(ThisAddIn).Assembly);
                OfficeCopilot.Settings.WinForms.DevExpressSkinBootstrap.Apply();

                Application.WorkbookOpen += Application_WorkbookOpen;
                Application.WorkbookBeforeClose += Application_WorkbookBeforeClose;
                Application.WorkbookBeforeSave += Application_WorkbookBeforeSave;
                ((Excel.AppEvents_Event)Application).NewWorkbook += Application_NewWorkbook;
                Application.SheetChange += Application_SheetChange;
                Application.SheetBeforeDelete += Application_SheetBeforeDelete;
                SubscribeOpenWorkbookMapInvalidation();
                ShowProtectionRecoveryFailures(
                    ExcelStagedWorksheetProtection.RecoverOpenWorkbooks(Application));

                TaskPaneManager = new ExcelCopilotTaskPaneManager(this);
                StartupTimingLog.Mark("excel-startup-exit");

                Application.WindowActivate += Application_FirstWindowActivate;

                // 临时注释：先不要在 Startup 阶段自动显示面板
                // TaskPaneManager.ShowPane();
            }
            catch (Exception ex)
            {
                System.Windows.Forms.MessageBox.Show(
                    LocalizationService.Format(
                        "Shell.Startup.LoadFailure",
                        LocalizationService.Get("Shell.Host.Excel"),
                        ex),
                    LocalizationService.Get("Shell.Product.Excel"),
                    System.Windows.Forms.MessageBoxButtons.OK,
                    System.Windows.Forms.MessageBoxIcon.Error);
            }
        }

        private void Application_FirstWindowActivate(Excel.Workbook wb, Excel.Window win)
        {
            Application.WindowActivate -= Application_FirstWindowActivate;
            StartupTimingLog.Mark("excel-first-window-activate");
        }

        private void ThisAddIn_Shutdown(object sender, System.EventArgs e)
        {
            _uiProbe?.Dispose();
            _uiProbe = null;

            if (Application != null)
            {
                Application.WorkbookOpen -= Application_WorkbookOpen;
                Application.WorkbookBeforeClose -= Application_WorkbookBeforeClose;
                Application.WorkbookBeforeSave -= Application_WorkbookBeforeSave;
                ((Excel.AppEvents_Event)Application).NewWorkbook -= Application_NewWorkbook;
                Application.SheetChange -= Application_SheetChange;
                Application.SheetBeforeDelete -= Application_SheetBeforeDelete;
                UnsubscribeOpenWorkbookMapInvalidation();
            }

            ExcelHostAdapter.ClearWorkbookMapCache();

            if (TaskPaneManager != null)
            {
                TaskPaneManager.Dispose();
            }
        }

        private void Application_WorkbookOpen(Excel.Workbook workbook)
        {
            SubscribeWorkbookMapInvalidation(workbook);
            InvalidateWorkbookMapCache(workbook);
            ShowProtectionRecoveryFailures(
                ExcelStagedWorksheetProtection.RecoverWorkbook(workbook));
        }

        private void Application_NewWorkbook(Excel.Workbook workbook)
        {
            SubscribeWorkbookMapInvalidation(workbook);
            InvalidateWorkbookMapCache(workbook);
        }

        private void Application_WorkbookBeforeClose(Excel.Workbook workbook, ref bool cancel)
        {
            InvalidateWorkbookMapCache(workbook);
        }

        private void Application_WorkbookBeforeSave(
            Excel.Workbook workbook,
            bool saveAsUI,
            ref bool cancel)
        {
            InvalidateWorkbookMapCache(workbook);
        }

        private void Application_SheetChange(object sheet, Excel.Range target)
        {
            InvalidateWorkbookMapCacheForSheet(sheet);
        }

        private static void Workbook_NewSheet(object sheet)
        {
            InvalidateWorkbookMapCacheForSheet(sheet);
        }

        private void Application_SheetBeforeDelete(object sheet)
        {
            InvalidateWorkbookMapCacheForSheet(sheet);
        }

        private static void InvalidateWorkbookMapCacheForSheet(object sheet)
        {
            var worksheet = sheet as Excel.Worksheet;
            var workbook = worksheet == null ? null : worksheet.Parent as Excel.Workbook;
            InvalidateWorkbookMapCache(workbook);
        }

        private static void InvalidateWorkbookMapCache(Excel.Workbook workbook)
        {
            ExcelHostAdapter.InvalidateWorkbookMapCache(workbook);
        }

        private void SubscribeOpenWorkbookMapInvalidation()
        {
            Excel.Workbooks workbooks = null;
            Excel.Workbook workbook = null;
            try
            {
                workbooks = Application.Workbooks;
                for (var index = 1; index <= workbooks.Count; index++)
                {
                    workbook = workbooks.Item[index] as Excel.Workbook;
                    SubscribeWorkbookMapInvalidation(workbook);
                    workbook = null;
                }
            }
            finally
            {
                if (workbook != null)
                {
                    System.Runtime.InteropServices.Marshal.ReleaseComObject(workbook);
                }

                if (workbooks != null)
                {
                    System.Runtime.InteropServices.Marshal.ReleaseComObject(workbooks);
                }
            }
        }

        private static void SubscribeWorkbookMapInvalidation(Excel.Workbook workbook)
        {
            if (workbook != null)
            {
                workbook.NewSheet += Workbook_NewSheet;
            }
        }

        private void UnsubscribeOpenWorkbookMapInvalidation()
        {
            Excel.Workbooks workbooks = null;
            Excel.Workbook workbook = null;
            try
            {
                workbooks = Application.Workbooks;
                for (var index = 1; index <= workbooks.Count; index++)
                {
                    workbook = workbooks.Item[index] as Excel.Workbook;
                    if (workbook != null)
                    {
                        workbook.NewSheet -= Workbook_NewSheet;
                        System.Runtime.InteropServices.Marshal.ReleaseComObject(workbook);
                        workbook = null;
                    }
                }
            }
            finally
            {
                if (workbook != null)
                {
                    System.Runtime.InteropServices.Marshal.ReleaseComObject(workbook);
                }

                if (workbooks != null)
                {
                    System.Runtime.InteropServices.Marshal.ReleaseComObject(workbooks);
                }
            }
        }

        private static void ShowProtectionRecoveryFailures(
            System.Collections.Generic.IList<string> failures)
        {
            if (failures == null || failures.Count == 0)
            {
                return;
            }

            System.Windows.Forms.MessageBox.Show(
                LocalizationService.Format(
                    "Shell.Excel.ProtectionRecoveryFailure",
                    string.Join("\r\n", failures)),
                LocalizationService.Get("Shell.Product.Excel"),
                System.Windows.Forms.MessageBoxButtons.OK,
                System.Windows.Forms.MessageBoxIcon.Warning);
        }

        #region VSTO 生成的代码

        private void InternalStartup()
        {
            this.Startup += new System.EventHandler(ThisAddIn_Startup);
            this.Shutdown += new System.EventHandler(ThisAddIn_Shutdown);
        }

        #endregion
    }
}
