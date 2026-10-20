using Microsoft.Office.Tools.Ribbon;
using OfficeCopilot.Settings;
using OfficeCopilot.Settings.Abstractions;
using OfficeCopilot.Settings.WinForms;
using OfficeCopilot.Agent.Abstractions;
using OfficeCopilot.Diagnostics.Logging;
using System;
using System.Diagnostics;
using System.Windows.Forms;
using Excel = Microsoft.Office.Interop.Excel;

namespace NixerExcelCopilot
{
    public partial class ExcelCopilotRibbon
    {
        private readonly ISettingsStore _settingsStore = new JsonSettingsStore();

        private void ExcelCopilotRibbon_Load(object sender, RibbonUIEventArgs e)
        {
            StartupTimingLog.Mark("ribbon-load");
        }

        /// <summary>
        /// 设置 Excel 沙漏光标 + 状态栏提示。
        /// 对话框打开后其自身消息泵会接管光标；对话框关闭后状态栏仍显示直到 RestoreExcelCursor。
        /// </summary>
        private void SetExcelWaitCursor(string statusBarText = null)
        {
            if (Globals.ThisAddIn?.Application != null)
            {
                Globals.ThisAddIn.Application.Cursor = Excel.XlMousePointer.xlWait;
                if (!string.IsNullOrEmpty(statusBarText))
                    Globals.ThisAddIn.Application.StatusBar = statusBarText;
            }
            Cursor.Current = Cursors.WaitCursor;
        }

        private void RestoreExcelCursor()
        {
            if (Globals.ThisAddIn?.Application != null)
            {
                Globals.ThisAddIn.Application.Cursor = Excel.XlMousePointer.xlDefault;
                Globals.ThisAddIn.Application.StatusBar = false;
            }
            Cursor.Current = Cursors.Default;
        }

        private void btnTogglePane_Click(object sender, RibbonControlEventArgs e)
        {
            try
            {
                if (Globals.ThisAddIn == null)
                    throw new InvalidOperationException("Globals.ThisAddIn 为空。");
                if (Globals.ThisAddIn.TaskPaneManager == null)
                    throw new InvalidOperationException("TaskPaneManager 未初始化。");

                var isFirstCreation = !Globals.ThisAddIn.TaskPaneManager.IsPaneCreated;

                if (isFirstCreation)
                {
                    SetExcelWaitCursor("正在创建面板，请稍候...");
                    Application.DoEvents();
                }

                Globals.ThisAddIn.TaskPaneManager.TogglePane();
            }
            catch (Exception ex)
            {
                MessageBox.Show("操作失败：\r\n\r\n" + ex, "NixerExcelCopilot",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                RestoreExcelCursor();
            }
        }

        // ========== 模型管理 ==========
        private void btnModelConfig_Click(object sender, RibbonControlEventArgs e)
        {
            try
            {
                SetExcelWaitCursor("正在打开模型配置...");
                Application.DoEvents();

                using (var form = new fmModelConfig(_settingsStore))
                {
                    form.ShowDialog();
                }

                RefreshContextIfNeeded();
            }
            catch (Exception ex)
            {
                MessageBox.Show("打开模型管理失败：" + ex.Message, "配置错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                RestoreExcelCursor();
            }
        }

        // ========== MCP管理 ==========
        private void btnMcpConfig_Click(object sender, RibbonControlEventArgs e)
        {
            try
            {
                SetExcelWaitCursor("正在打开 MCP 配置...");
                Application.DoEvents();

                using (var form = new fmMcpConfig(_settingsStore))
                {
                    form.ShowDialog();
                }

                RefreshContextIfNeeded();
            }
            catch (Exception ex)
            {
                MessageBox.Show("打开MCP管理失败：" + ex.Message, "配置错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                RestoreExcelCursor();
            }
        }

        // ========== Skills管理 ==========
        private void btnSkillsConfig_Click(object sender, RibbonControlEventArgs e)
        {
            try
            {
                SetExcelWaitCursor("正在打开 Skills 配置...");
                Application.DoEvents();

                using (var form = new fmSkillsConfig(_settingsStore, HostApplicationNames.Excel))
                {
                    form.ShowDialog();
                }

                RefreshContextIfNeeded();
            }
            catch (Exception ex)
            {
                MessageBox.Show("打开Skills管理失败：" + ex.Message, "配置错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                RestoreExcelCursor();
            }
        }

        // ========== 记忆管理 ==========
        private void btnMemoryConfig_Click(object sender, RibbonControlEventArgs e)
        {
            try
            {
                SetExcelWaitCursor("正在打开记忆管理...");
                Application.DoEvents();

                using (var form = new fmMemoryConfig())
                {
                    form.ShowDialog();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("打开记忆管理失败：" + ex.Message, "配置错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                RestoreExcelCursor();
            }
        }

        // ========== 内置工具 ==========
        private void btnBuiltinToolsConfig_Click(object sender, RibbonControlEventArgs e)
        {
            try
            {
                SetExcelWaitCursor("正在打开内置工具配置...");
                Application.DoEvents();

                IToolRegistry toolRegistry = null;
                IServiceProvider toolServices = null;
                var toolHostApplicationName = string.Empty;

                if (Globals.ThisAddIn?.TaskPaneManager != null)
                {
                    Globals.ThisAddIn.TaskPaneManager.TryGetToolRuntime(
                        out toolRegistry, out toolServices, out toolHostApplicationName);
                }

                using (var form = new fmBuiltinToolConfig(
                    _settingsStore, toolRegistry, toolServices, toolHostApplicationName))
                {
                    form.ShowDialog();
                }

                RefreshContextIfNeeded();
            }
            catch (Exception ex)
            {
                MessageBox.Show("打开内置工具管理失败：" + ex.Message, "配置错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                RestoreExcelCursor();
            }
        }

        // ========== 系统设置 ==========
        private void btnConfig_Click(object sender, RibbonControlEventArgs e)
        {
            try
            {
                SetExcelWaitCursor("正在打开系统设置...");
                Application.DoEvents();

                using (var form = new fmSysConfig(_settingsStore, HostApplicationNames.Excel))
                {
                    form.ShowDialog();
                }

                RefreshContextIfNeeded();
            }
            catch (Exception ex)
            {
                MessageBox.Show("打开系统设置失败：" + ex.Message, "配置错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                RestoreExcelCursor();
            }
        }

        private void btnAbout_Click(object sender, RibbonControlEventArgs e)
        {
            try
            {
                SetExcelWaitCursor("正在打开关于...");
                Application.DoEvents();

                using (var dialog = new AboutDialog())
                {
                    dialog.ShowDialog();
                }
            }
            finally
            {
                RestoreExcelCursor();
            }
        }

        private void RefreshContextIfNeeded()
        {
            if (Globals.ThisAddIn != null && Globals.ThisAddIn.TaskPaneManager != null)
            {
                Globals.ThisAddIn.TaskPaneManager.RefreshContext();
            }
        }

    }
}
