using Microsoft.Office.Tools.Ribbon;
using OfficeCopilot.Localization;
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
            LocalizationService.LanguageChanged -= LocalizationService_LanguageChanged;
            LocalizationService.LanguageChanged += LocalizationService_LanguageChanged;
            ApplyLocalization();
        }

        private void LocalizationService_LanguageChanged(object sender, EventArgs e)
        {
            ApplyLocalization();
        }

        private void ApplyLocalization()
        {
            groupCopilot.Label = LocalizationService.Get("Shell.Ribbon.ExcelGroup");
            btnTogglePane.Label = LocalizationService.Get("Shell.Ribbon.TogglePane");
            btnModelConfig.Label = LocalizationService.Get("Shell.Ribbon.ModelConfig");
            btnMcpConfig.Label = LocalizationService.Get("Shell.Ribbon.McpConfig");
            btnSkillsConfig.Label = LocalizationService.Get("Shell.Ribbon.SkillsConfig");
            btnMemoryConfig.Label = LocalizationService.Get("Shell.Ribbon.MemoryConfig");
            btnBuiltinToolsConfig.Label = LocalizationService.Get("Shell.Ribbon.BuiltinToolsConfig");
            btnConfig.Label = LocalizationService.Get("Shell.Ribbon.SystemSettings");
            btnAbout.Label = LocalizationService.Get("Shell.Ribbon.About");
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
                    throw new InvalidOperationException(LocalizationService.Get("Shell.Error.AddInUnavailable"));
                if (Globals.ThisAddIn.TaskPaneManager == null)
                    throw new InvalidOperationException(LocalizationService.Get("Shell.Error.TaskPaneUnavailable"));

                var isFirstCreation = !Globals.ThisAddIn.TaskPaneManager.IsPaneCreated;

                if (isFirstCreation)
                {
                    SetExcelWaitCursor(LocalizationService.Get("Shell.Status.CreatingPane"));
                    Application.DoEvents();
                }

                Globals.ThisAddIn.TaskPaneManager.TogglePane();
            }
            catch (Exception ex)
            {
                MessageBox.Show(LocalizationService.Format("Shell.Error.OperationFailed", ex),
                    LocalizationService.Get("Shell.Product.Excel"),
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
                SetExcelWaitCursor(LocalizationService.Get("Shell.Status.OpeningModelConfig"));
                Application.DoEvents();

                using (var form = new fmModelConfig(_settingsStore))
                {
                    form.ShowDialog();
                }

                RefreshContextIfNeeded();
            }
            catch (Exception ex)
            {
                MessageBox.Show(LocalizationService.Format("Shell.Error.OpenModelConfigFailed", ex.Message),
                    LocalizationService.Get("Shell.Error.ConfigurationTitle"), MessageBoxButtons.OK, MessageBoxIcon.Error);
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
                SetExcelWaitCursor(LocalizationService.Get("Shell.Status.OpeningMcpConfig"));
                Application.DoEvents();

                using (var form = new fmMcpConfig(_settingsStore))
                {
                    form.ShowDialog();
                }

                RefreshContextIfNeeded();
            }
            catch (Exception ex)
            {
                MessageBox.Show(LocalizationService.Format("Shell.Error.OpenMcpConfigFailed", ex.Message),
                    LocalizationService.Get("Shell.Error.ConfigurationTitle"), MessageBoxButtons.OK, MessageBoxIcon.Error);
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
                SetExcelWaitCursor(LocalizationService.Get("Shell.Status.OpeningSkillsConfig"));
                Application.DoEvents();

                using (var form = new fmSkillsConfig(_settingsStore, HostApplicationNames.Excel))
                {
                    form.ShowDialog();
                }

                RefreshContextIfNeeded();
            }
            catch (Exception ex)
            {
                MessageBox.Show(LocalizationService.Format("Shell.Error.OpenSkillsConfigFailed", ex.Message),
                    LocalizationService.Get("Shell.Error.ConfigurationTitle"), MessageBoxButtons.OK, MessageBoxIcon.Error);
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
                SetExcelWaitCursor(LocalizationService.Get("Shell.Status.OpeningMemoryConfig"));
                Application.DoEvents();

                using (var form = new fmMemoryConfig())
                {
                    form.ShowDialog();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(LocalizationService.Format("Shell.Error.OpenMemoryConfigFailed", ex.Message),
                    LocalizationService.Get("Shell.Error.ConfigurationTitle"), MessageBoxButtons.OK, MessageBoxIcon.Error);
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
                SetExcelWaitCursor(LocalizationService.Get("Shell.Status.OpeningBuiltinToolsConfig"));
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
                MessageBox.Show(LocalizationService.Format("Shell.Error.OpenBuiltinToolsConfigFailed", ex.Message),
                    LocalizationService.Get("Shell.Error.ConfigurationTitle"), MessageBoxButtons.OK, MessageBoxIcon.Error);
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
                SetExcelWaitCursor(LocalizationService.Get("Shell.Status.OpeningSystemSettings"));
                Application.DoEvents();

                using (var form = new fmSysConfig(_settingsStore, HostApplicationNames.Excel))
                {
                    form.ShowDialog();
                }

                RefreshContextIfNeeded();
            }
            catch (Exception ex)
            {
                MessageBox.Show(LocalizationService.Format("Shell.Error.OpenSystemSettingsFailed", ex.Message),
                    LocalizationService.Get("Shell.Error.ConfigurationTitle"), MessageBoxButtons.OK, MessageBoxIcon.Error);
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
                SetExcelWaitCursor(LocalizationService.Get("Shell.Status.OpeningAbout"));
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
