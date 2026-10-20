using System;
using System.Diagnostics;
using OfficeCopilot.Diagnostics.Logging;
using PowerPoint = Microsoft.Office.Interop.PowerPoint;
using Office = Microsoft.Office.Core;

namespace NixerPowerpointCopilot
{
    public partial class ThisAddIn
    {
        internal PowerPointCopilotTaskPaneManager TaskPaneManager { get; private set; }

        private void ThisAddIn_Startup(object sender, System.EventArgs e)
        {
            try
            {
                StartupTimingLog.Mark("ppt-startup-enter");
                OfficeCopilot.Settings.WinForms.HostFormIcon.Initialize(typeof(ThisAddIn).Assembly);
                OfficeCopilot.Slides.Rendering.DeckPreviewForm.WindowIcon = OfficeCopilot.Settings.WinForms.HostFormIcon.Current;
                OfficeCopilot.Settings.WinForms.DevExpressSkinBootstrap.Apply();

                TaskPaneManager = new PowerPointCopilotTaskPaneManager(this);
                StartupTimingLog.Mark("ppt-startup-exit");

                Application.WindowActivate += Application_FirstWindowActivate;

                // 临时注释：先不要在 Startup 阶段自动显示面板
                // TaskPaneManager.ShowPane();
            }
            catch (Exception ex)
            {
                System.Windows.Forms.MessageBox.Show(
                    "PowerPoint 加载加载项时发生异常：\r\n\r\n" + ex,
                    "NixerPowerPointCopilot",
                    System.Windows.Forms.MessageBoxButtons.OK,
                    System.Windows.Forms.MessageBoxIcon.Error);
            }
        }

        private void Application_FirstWindowActivate(PowerPoint.Presentation pres, PowerPoint.DocumentWindow win)
        {
            Application.WindowActivate -= Application_FirstWindowActivate;
            StartupTimingLog.Mark("ppt-first-window-activate");
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
