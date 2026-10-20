using System;
using System.Diagnostics;
using OfficeCopilot.Diagnostics.Logging;
using Word = Microsoft.Office.Interop.Word;
using Office = Microsoft.Office.Core;
using Microsoft.Office.Tools.Word;

namespace NixerWordCopilot
{
    public partial class ThisAddIn
    {
        internal WordCopilotTaskPaneManager TaskPaneManager { get; private set; }

        private void ThisAddIn_Startup(object sender, System.EventArgs e)
        {
            try
            {
                StartupTimingLog.Mark("word-startup-enter");
                OfficeCopilot.Settings.WinForms.HostFormIcon.Initialize(typeof(ThisAddIn).Assembly);
                OfficeCopilot.Settings.WinForms.DevExpressSkinBootstrap.Apply();

                TaskPaneManager = new WordCopilotTaskPaneManager(this);
                StartupTimingLog.Mark("word-startup-exit");

                Application.WindowActivate += Application_FirstWindowActivate;

                // 临时注释：先不要在 Startup 阶段自动显示面板
                // TaskPaneManager.ShowPane();
            }
            catch (Exception ex)
            {
                System.Windows.Forms.MessageBox.Show(
                    "Word 加载加载项时发生异常：\r\n\r\n" + ex,
                    "NixerWordCopilot",
                    System.Windows.Forms.MessageBoxButtons.OK,
                    System.Windows.Forms.MessageBoxIcon.Error);
            }
        }

        private void Application_FirstWindowActivate(Word.Document doc, Word.Window win)
        {
            Application.WindowActivate -= Application_FirstWindowActivate;
            StartupTimingLog.Mark("word-first-window-activate");
        }

        private void ThisAddIn_Shutdown(object sender, System.EventArgs e)
        {
            if (TaskPaneManager != null)
            {
                TaskPaneManager.Dispose();
            }
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
