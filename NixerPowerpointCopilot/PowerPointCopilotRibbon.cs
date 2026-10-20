using Microsoft.Office.Tools.Ribbon;
using OfficeCopilot.Settings;
using OfficeCopilot.Settings.Abstractions;
using OfficeCopilot.Settings.WinForms;
using OfficeCopilot.Agent.Abstractions;
using System;
using System.Diagnostics;
using System.Windows.Forms;
using PowerPoint = Microsoft.Office.Interop.PowerPoint;

namespace NixerPowerpointCopilot
{
    public partial class PowerPointCopilotRibbon
    {
        private readonly ISettingsStore _settingsStore = new JsonSettingsStore();

        private void PowerPointCopilotRibbon_Load(object sender, RibbonUIEventArgs e)
        {
        }

        /// <summary>
        /// 设置 PowerPoint 沙漏光标 + 状态栏提示。
        /// 对话框打开后其自身消息泵会接管光标；对话框关闭后状态栏仍显示直到 RestorePowerPointCursor。
        /// </summary>
        private void SetPowerPointWaitCursor(string statusBarText = null)
        {
            // PowerPoint 没有直接的全局光标设置，通过 Windows 光标模拟
            Cursor.Current = Cursors.WaitCursor;
        }

        private void RestorePowerPointCursor()
        {
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
                    SetPowerPointWaitCursor("正在创建面板，请稍候...");
                    Application.DoEvents();
                }

                Globals.ThisAddIn.TaskPaneManager.TogglePane();
            }
            catch (Exception ex)
            {
                MessageBox.Show("操作失败：\r\n\r\n" + ex, "NixerPowerPointCopilot",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                RestorePowerPointCursor();
            }
        }

        // ========== 模型管理 ==========
        private void btnModelConfig_Click(object sender, RibbonControlEventArgs e)
        {
            try
            {
                SetPowerPointWaitCursor("正在打开模型配置...");
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
                RestorePowerPointCursor();
            }
        }

        // ========== MCP管理 ==========
        private void btnMcpConfig_Click(object sender, RibbonControlEventArgs e)
        {
            try
            {
                SetPowerPointWaitCursor("正在打开 MCP 配置...");
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
                RestorePowerPointCursor();
            }
        }

        // ========== Skills管理 ==========
        private void btnSkillsConfig_Click(object sender, RibbonControlEventArgs e)
        {
            try
            {
                SetPowerPointWaitCursor("正在打开 Skills 配置...");
                Application.DoEvents();

                using (var form = new fmSkillsConfig(_settingsStore, HostApplicationNames.PowerPoint))
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
                RestorePowerPointCursor();
            }
        }

        // ========== 记忆管理 ==========
        private void btnMemoryConfig_Click(object sender, RibbonControlEventArgs e)
        {
            try
            {
                SetPowerPointWaitCursor("正在打开记忆管理...");
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
                RestorePowerPointCursor();
            }
        }

        // ========== 内置工具 ==========
        private void btnBuiltinToolsConfig_Click(object sender, RibbonControlEventArgs e)
        {
            try
            {
                SetPowerPointWaitCursor("正在打开内置工具配置...");
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
                RestorePowerPointCursor();
            }
        }

        // ========== 系统设置 ==========
        private void btnConfig_Click(object sender, RibbonControlEventArgs e)
        {
            try
            {
                SetPowerPointWaitCursor("正在打开系统设置...");
                Application.DoEvents();

                using (var form = new fmSysConfig(_settingsStore, HostApplicationNames.PowerPoint))
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
                RestorePowerPointCursor();
            }
        }

        private void btnAbout_Click(object sender, RibbonControlEventArgs e)
        {
            try
            {
                SetPowerPointWaitCursor("正在打开关于...");
                Application.DoEvents();

                using (var dialog = new AboutDialog())
                {
                    dialog.ShowDialog();
                }
            }
            finally
            {
                RestorePowerPointCursor();
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
