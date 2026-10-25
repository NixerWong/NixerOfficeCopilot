using DevExpress.XtraBars;
using OfficeCopilot.Localization;
using OfficeCopilot.Settings;
using OfficeCopilot.Settings.Abstractions;
using System;
using System.Diagnostics;
using System.Windows.Forms;

namespace OfficeCopilot.Settings.WinForms
{
    public partial class fmSysConfig : HostAwareForm
    {
        private readonly ISettingsStore _settingsStore;
        private readonly string _hostApplicationName;
        private OfficeCopilotSettings _settings;
        private bool _loadingSettings;

        public fmSysConfig(ISettingsStore settingsStore, string hostApplicationName)
        {
            InitializeComponent();
            _settingsStore = settingsStore ?? new JsonSettingsStore();
            _hostApplicationName = HostApplicationNames.Normalize(hostApplicationName);
            WireEvents();
            ApplyLocalization();
            LocalizationService.LanguageChanged += LocalizationService_LanguageChanged;
            Disposed += fmSysConfig_Disposed;
        }

        private void WireEvents()
        {
            this.Load += fmSysConfig_Load;
            this.FormClosing += fmSysConfig_FormClosing;

            spHttpRequestTimeOut.EditValueChanged += GeneralSetting_Changed;
            seAttachmentFileCountThreshold.EditValueChanged += GeneralSetting_Changed;
            seMaxVisualPages.EditValueChanged += GeneralSetting_Changed;
            seAttachmentInlineCharBudget.EditValueChanged += GeneralSetting_Changed;
            seNativeThresholdKB.EditValueChanged += GeneralSetting_Changed;
            seInlineTextThresholdMB.EditValueChanged += GeneralSetting_Changed;
            seToolFetchFileThresholdMB.EditValueChanged += GeneralSetting_Changed;
            ceAutoResizeBigImage.CheckedChanged += GeneralSetting_Changed;
            ceAutoCompactEnabled.CheckedChanged += GeneralSetting_Changed;
            cbeTranscriptLogMode.EditValueChanged += GeneralSetting_Changed;
            ceRuntimeLogEnabled.CheckedChanged += GeneralSetting_Changed;
            cbeRuntimeLogLevel.EditValueChanged += GeneralSetting_Changed;
            seMaxFileSizeMb.EditValueChanged += GeneralSetting_Changed;
            seRetainedFileCount.EditValueChanged += GeneralSetting_Changed;
            ceTranscriptIncludeToolCalls.CheckedChanged += GeneralSetting_Changed;
            ceTranscriptIncludeHostContext.CheckedChanged += GeneralSetting_Changed;
            seTranscriptMaxContentLength.EditValueChanged += GeneralSetting_Changed;
            ceToolCallLogEnabled.CheckedChanged += GeneralSetting_Changed;
            cbeTrustMode.EditValueChanged += GeneralSetting_Changed;
            cbeLanguage.EditValueChanged += GeneralSetting_Changed;

            btnSaveGenenalConfig.ItemClick += btnSaveGenenalConfig_ItemClick;
            btnCancelSaveGeneralConfig.ItemClick += btnCancelSaveGeneralConfig_ItemClick;

            btnEditAskModeSysPrompt.ItemClick += (s, e) => EditHostSystemPrompt(HostPromptMode.Ask);
            btnEditAgentModeSysPrompt.ItemClick += (s, e) => EditHostSystemPrompt(HostPromptMode.Agent);

            // Populate values that remain invariant across UI languages.
            cbeRuntimeLogLevel.Properties.Items.AddRange(new string[] { "Trace", "Debug", "Information", "Warning", "Error", "Critical", "None" });
        }

        // ========== 宿主系统提示词 ==========

        private void EditHostSystemPrompt(HostPromptMode mode)
        {
            try
            {
                // 文件不存在时以内置默认播种，用户打开就能看到完整默认提示词。
                var path = HostSystemPromptStore.EnsureFile(_hostApplicationName, mode);
                Process.Start(new ProcessStartInfo("notepad.exe", "\"" + path + "\"") { UseShellExecute = false });
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    LocalizationService.Format("Settings.SysConfig.Error.OpenPromptFailed", ex.Message),
                    LocalizationService.Get("Settings.SysConfig.Text"),
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }

        // ========== Load / Save ==========

        private void fmSysConfig_Load(object sender, EventArgs e)
        {
            LoadSettings();
        }

        private void fmSysConfig_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (_loadingSettings || e == null) return;
            try { SaveSettings(); }
            catch (Exception ex)
            {
                if (MessageBox.Show(this, LocalizationService.Format("Settings.Config.Error.SaveOnCloseFailed", ex.Message), LocalizationService.Get("Settings.SysConfig.Text"),
                    MessageBoxButtons.YesNo, MessageBoxIcon.Error) != DialogResult.Yes)
                    e.Cancel = true;
            }
        }

        private void LoadSettings()
        {
            _loadingSettings = true;
            try
            {
                _settings = OfficeCopilotSettingsFactory.Normalize(
                    _settingsStore.Load() ?? OfficeCopilotSettingsFactory.CreateDefault());
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, LocalizationService.Format("Settings.Config.Error.LoadSettings", ex.Message), LocalizationService.Get("Settings.SysConfig.Text"), MessageBoxButtons.OK, MessageBoxIcon.Error);
                _settings = OfficeCopilotSettingsFactory.CreateDefault();
            }
            LocalizationService.SetLanguage(_settings.UiLanguage);
            ApplyLocalization();
            BindGeneralSettings();
            _loadingSettings = false;
        }

        private void BindGeneralSettings()
        {
            if (_settings == null) return;
            if (_settings.ChatDefaults == null) _settings.ChatDefaults = new ChatDefaultsSettings();
            if (_settings.Logging == null) _settings.Logging = new LoggingSettings();
            if (_settings.Logging.ModelTranscripts == null) _settings.Logging.ModelTranscripts = new ModelTranscriptLoggingSettings();
            if (_settings.Logging.Runtime == null) _settings.Logging.Runtime = new RuntimeLoggingSettings();
            if (_settings.Logging.ToolCalls == null) _settings.Logging.ToolCalls = new ToolCallLoggingSettings();
            if (_settings.Runtime == null) _settings.Runtime = new RuntimeSettings();

            cbeLanguage.SelectedIndex = SupportedLanguages.GetIndex(_settings.UiLanguage);

            // ChatDefaults
            spHttpRequestTimeOut.EditValue = Math.Max(1, _settings.ChatDefaults.RequestTimeoutSeconds);
            seAttachmentFileCountThreshold.EditValue = Math.Max(1, _settings.ChatDefaults.AttachmentFileCountThreshold);
            seMaxVisualPages.EditValue = Math.Max(0, Math.Min(50, _settings.ChatDefaults.MaxVisualPages));
            seAttachmentInlineCharBudget.EditValue = Math.Max(1000, Math.Min(200000, _settings.ChatDefaults.AttachmentInlineCharBudget));
            seNativeThresholdKB.EditValue = Math.Max(1, _settings.ChatDefaults.NativeThresholdKb);
            seInlineTextThresholdMB.EditValue = Math.Max(1, _settings.ChatDefaults.InlineTextThresholdMb);
            seToolFetchFileThresholdMB.EditValue = Math.Max(1, _settings.ChatDefaults.ToolFetchFileThresholdMb);
            ceAutoResizeBigImage.Checked = _settings.ChatDefaults.AutoResizeBigImage;

            // Runtime
            ceAutoCompactEnabled.Checked = _settings.Runtime.AutoCompactEnabled;

            // Transcript
            switch (_settings.Logging.ModelTranscripts.Mode)
            {
                case ModelTranscriptLogMode.MetadataOnly: cbeTranscriptLogMode.SelectedIndex = 1; break;
                case ModelTranscriptLogMode.SanitizedContent: cbeTranscriptLogMode.SelectedIndex = 2; break;
                case ModelTranscriptLogMode.RawContent: cbeTranscriptLogMode.SelectedIndex = 3; break;
                default: cbeTranscriptLogMode.SelectedIndex = 0; break;
            }
            ceRuntimeLogEnabled.Checked = _settings.Logging.Runtime.IsEnabled;
            cbeRuntimeLogLevel.SelectedIndex = MapAppLogLevelToIndex(_settings.Logging.Runtime.MinimumLevel);
            seMaxFileSizeMb.EditValue = _settings.Logging.Runtime.MaxFileSizeMb;
            seRetainedFileCount.EditValue = _settings.Logging.Runtime.RetainedFileCountLimit;
            ceTranscriptIncludeToolCalls.Checked = _settings.Logging.ModelTranscripts.IncludeToolCalls;
            ceTranscriptIncludeHostContext.Checked = _settings.Logging.ModelTranscripts.IncludeHostContext;
            seTranscriptMaxContentLength.EditValue = _settings.Logging.ModelTranscripts.MaxContentLength;
            ceToolCallLogEnabled.Checked = _settings.Logging.ToolCalls.IsEnabled;

            // TrustMode（按宿主分别保存）
            cbeTrustMode.SelectedIndex = OfficeCopilotSettingsFactory.GetToolTrustMode(_settings, _hostApplicationName) == ToolTrustModeValues.AlwaysAllow ? 1 : 0;
        }

        private void SaveSettings()
        {
            ApplyGeneralSettings();
            OfficeCopilotSettingsFactory.RefreshToolTrustModes(_settings, _settingsStore.Load(), _hostApplicationName);
            _settingsStore.Save(_settings);
            LocalizationService.SetLanguage(_settings.UiLanguage);
            ApplyLocalization();
        }

        private void ApplyGeneralSettings()
        {
            if (_settings == null) return;
            if (_settings.ChatDefaults == null) _settings.ChatDefaults = new ChatDefaultsSettings();
            if (_settings.Logging == null) _settings.Logging = new LoggingSettings();
            if (_settings.Logging.ModelTranscripts == null) _settings.Logging.ModelTranscripts = new ModelTranscriptLoggingSettings();
            if (_settings.Logging.Runtime == null) _settings.Logging.Runtime = new RuntimeLoggingSettings();
            if (_settings.Logging.ToolCalls == null) _settings.Logging.ToolCalls = new ToolCallLoggingSettings();
            if (_settings.Runtime == null) _settings.Runtime = new RuntimeSettings();

            _settings.UiLanguage = SupportedLanguages.GetCodeAt(cbeLanguage.SelectedIndex);

            _settings.ChatDefaults.RequestTimeoutSeconds = Math.Max(1, Convert.ToInt32(spHttpRequestTimeOut.Value));
            _settings.ChatDefaults.AttachmentFileCountThreshold = Math.Max(1, Convert.ToInt32(seAttachmentFileCountThreshold.Value));
            _settings.ChatDefaults.MaxVisualPages = Math.Max(0, Math.Min(50, Convert.ToInt32(seMaxVisualPages.Value)));
            _settings.ChatDefaults.AttachmentInlineCharBudget = Math.Max(1000, Math.Min(200000, Convert.ToInt32(seAttachmentInlineCharBudget.Value)));
            _settings.ChatDefaults.NativeThresholdKb = Math.Max(1, Convert.ToInt32(seNativeThresholdKB.Value));
            _settings.ChatDefaults.InlineTextThresholdMb = Math.Max(1, Convert.ToInt32(seInlineTextThresholdMB.Value));
            _settings.ChatDefaults.ToolFetchFileThresholdMb = Math.Max(1, Convert.ToInt32(seToolFetchFileThresholdMB.Value));
            _settings.ChatDefaults.AutoResizeBigImage = ceAutoResizeBigImage.Checked;

            _settings.Runtime.AutoCompactEnabled = ceAutoCompactEnabled.Checked;

            _settings.Logging.ModelTranscripts.Mode = ModelTranscriptLogMode.Disabled;
            _settings.Logging.Runtime.IsEnabled = ceRuntimeLogEnabled.Checked;
            _settings.Logging.Runtime.MinimumLevel = MapIndexToAppLogLevel(cbeRuntimeLogLevel.SelectedIndex);
            _settings.Logging.Runtime.MaxFileSizeMb = Math.Max(1, Convert.ToInt32(seMaxFileSizeMb.Value));
            _settings.Logging.Runtime.RetainedFileCountLimit = Math.Max(1, Convert.ToInt32(seRetainedFileCount.Value));
            _settings.Logging.ModelTranscripts.IncludeToolCalls = ceTranscriptIncludeToolCalls.Checked;
            _settings.Logging.ModelTranscripts.IncludeHostContext = ceTranscriptIncludeHostContext.Checked;
            _settings.Logging.ModelTranscripts.MaxContentLength = Math.Max(0, Convert.ToInt32(seTranscriptMaxContentLength.Value));
            _settings.Logging.ToolCalls.IsEnabled = ceToolCallLogEnabled.Checked;

            OfficeCopilotSettingsFactory.SetToolTrustMode(
                _settings,
                _hostApplicationName,
                cbeTrustMode.SelectedIndex == 1 ? ToolTrustModeValues.AlwaysAllow : ToolTrustModeValues.NeedConfirm);

            _settings.UpdatedAtUtc = DateTime.UtcNow;
        }

        // ========== Events ==========

        private void GeneralSetting_Changed(object sender, EventArgs e)
        {
            if (_loadingSettings) return;
            try { SaveSettings(); }
            catch (Exception ex) { MessageBox.Show(this, LocalizationService.Format("Settings.Config.Error.SaveGeneralSettings", ex.Message), LocalizationService.Get("Settings.SysConfig.Text"), MessageBoxButtons.OK, MessageBoxIcon.Error); }
        }

        private void btnSaveGenenalConfig_ItemClick(object sender, ItemClickEventArgs e)
        {
            try { SaveSettings(); Close(); }
            catch (Exception ex) { MessageBox.Show(this, LocalizationService.Format("Settings.Config.Error.SaveSettings", ex.Message), LocalizationService.Get("Settings.SysConfig.Text"), MessageBoxButtons.OK, MessageBoxIcon.Error); }
        }

        private void btnCancelSaveGeneralConfig_ItemClick(object sender, ItemClickEventArgs e)
        {
            Close();
        }

        // ========== Helpers ==========

        private static int MapAppLogLevelToIndex(AppLogLevel level)
        {
            switch (level)
            {
                case AppLogLevel.Trace: return 0;
                case AppLogLevel.Debug: return 1;
                case AppLogLevel.Information: return 2;
                case AppLogLevel.Warning: return 3;
                case AppLogLevel.Error: return 4;
                case AppLogLevel.Critical: return 5;
                case AppLogLevel.None: return 6;
                default: return 2;
            }
        }

        private static AppLogLevel MapIndexToAppLogLevel(int index)
        {
            switch (index)
            {
                case 0: return AppLogLevel.Trace;
                case 1: return AppLogLevel.Debug;
                case 2: return AppLogLevel.Information;
                case 3: return AppLogLevel.Warning;
                case 4: return AppLogLevel.Error;
                case 5: return AppLogLevel.Critical;
                case 6: return AppLogLevel.None;
                default: return AppLogLevel.Information;
            }
        }

        private void LocalizationService_LanguageChanged(object sender, EventArgs e)
        {
            ApplyLocalization();
        }

        private void fmSysConfig_Disposed(object sender, EventArgs e)
        {
            LocalizationService.LanguageChanged -= LocalizationService_LanguageChanged;
        }

        private void ApplyLocalization()
        {
            var selectedLanguageIndex = cbeLanguage.SelectedIndex;
            var selectedTranscriptModeIndex = cbeTranscriptLogMode.SelectedIndex;
            var selectedRuntimeLogLevelIndex = cbeRuntimeLogLevel.SelectedIndex;
            var selectedTrustModeIndex = cbeTrustMode.SelectedIndex;
            var wasLoadingSettings = _loadingSettings;
            _loadingSettings = true;
            try
            {
                Text = LocalizationService.Get("Settings.SysConfig.Text");
                grpLanguage.Text = LocalizationService.Get("Settings.SysConfig.grpLanguage.Text");
                lbLanguage.Text = LocalizationService.Get("Settings.SysConfig.lbLanguage.Text");
                cbeLanguage.Properties.Items.Clear();
                cbeLanguage.Properties.Items.AddRange(new[]
                {
                    LocalizationService.Get("Language.English"),
                    LocalizationService.Get("Language.SimplifiedChinese"),
                    LocalizationService.Get("Language.Japanese"),
                    LocalizationService.Get("Language.German"),
                    LocalizationService.Get("Language.Spanish")
                });
                cbeLanguage.SelectedIndex = selectedLanguageIndex < 0 ? 0 : selectedLanguageIndex;

                grpModelParams.Text = LocalizationService.Get("Settings.Config.General.ModelParameters");
                lbHttpRequestTimeOut.Text = LocalizationService.Get("Settings.Config.General.RequestTimeout");
                lbNativeThresholdKB.Text = LocalizationService.Get("Settings.Config.General.NativeFileSizeLimit");
                lbInlineTextThresholdMB.Text = LocalizationService.Get("Settings.Config.General.InlineTextFileSizeLimit");
                lbAttachmentInlineCharBudget.Text = LocalizationService.Get("Settings.Config.General.AttachmentInlineCharacterBudget");
                lbMaxVisualPages.Text = LocalizationService.Get("Settings.Config.General.MaxVisualPages");
                lbToolFetchFileThresholdMB.Text = LocalizationService.Get("Settings.Config.General.ToolFetchFileSizeLimit");
                lbAttachmentFileCountThreshold.Text = LocalizationService.Get("Settings.Config.General.AttachmentFileCountLimit");
                grpRuntime.Text = LocalizationService.Get("Settings.Config.General.Runtime");
                ceAutoCompactEnabled.Properties.Caption = LocalizationService.Get("Settings.Config.General.AutoCompact");
                ceAutoResizeBigImage.Properties.Caption = LocalizationService.Get("Settings.Config.General.AutoResizeImages");
                grpLogging.Text = LocalizationService.Get("Settings.Config.General.Logging");
                lbTranscriptLogMode.Text = LocalizationService.Get("Settings.Config.General.TranscriptLogMode");
                ceRuntimeLogEnabled.Properties.Caption = LocalizationService.Get("Settings.Config.General.RuntimeLogEnabled");
                lbRuntimeLogLevel.Text = LocalizationService.Get("Settings.Config.General.RuntimeLogLevel");
                lbMaxFileSizeMb.Text = LocalizationService.Get("Settings.Config.General.MaxFileSize");
                lbRetainedFileCount.Text = LocalizationService.Get("Settings.Config.General.RetainedFileCount");
                lbTranscriptMaxContentLength.Text = LocalizationService.Get("Settings.Config.General.TranscriptMaxLength");
                seMaxFileSizeMb.Properties.NullValuePrompt = LocalizationService.Get("Settings.Config.General.MaxFileSize");
                seRetainedFileCount.Properties.NullValuePrompt = LocalizationService.Get("Settings.Config.General.RetainedFileCount");
                ceTranscriptIncludeToolCalls.Properties.Caption = LocalizationService.Get("Settings.Config.General.TranscriptToolCalls");
                ceTranscriptIncludeHostContext.Properties.Caption = LocalizationService.Get("Settings.Config.General.TranscriptHostContext");
                seTranscriptMaxContentLength.Properties.NullValuePrompt = LocalizationService.Get("Settings.Config.General.TranscriptMaxLength");
                ceToolCallLogEnabled.Properties.Caption = LocalizationService.Get("Settings.Config.General.ToolCallLogEnabled");
                ceAutoMemoryRecorder.Properties.Caption = LocalizationService.Get("Settings.Config.General.AutoMemoryRecorder");
                lbTrustMode.Text = LocalizationService.Get("Settings.SysConfig.TrustMode");

                cbeTranscriptLogMode.Properties.Items.Clear();
                cbeTranscriptLogMode.Properties.Items.AddRange(new[]
                {
                    LocalizationService.Get("Settings.Config.General.TranscriptMode.Disabled"),
                    LocalizationService.Get("Settings.Config.General.TranscriptMode.Metadata"),
                    LocalizationService.Get("Settings.Config.General.TranscriptMode.Redacted"),
                    LocalizationService.Get("Settings.Config.General.TranscriptMode.Raw")
                });
                cbeTranscriptLogMode.SelectedIndex = selectedTranscriptModeIndex < 0 ? 0 : selectedTranscriptModeIndex;

                cbeRuntimeLogLevel.Properties.Items.Clear();
                cbeRuntimeLogLevel.Properties.Items.AddRange(new[] { "Trace", "Debug", "Information", "Warning", "Error", "Critical", "None" });
                cbeRuntimeLogLevel.SelectedIndex = selectedRuntimeLogLevelIndex < 0 ? 2 : selectedRuntimeLogLevelIndex;
                cbeTrustMode.Properties.Items.Clear();
                cbeTrustMode.Properties.Items.AddRange(new[]
                {
                    LocalizationService.Get("Settings.SysConfig.TrustMode.Confirm"),
                    LocalizationService.Get("Settings.SysConfig.TrustMode.AlwaysAllow")
                });
                cbeTrustMode.SelectedIndex = selectedTrustModeIndex < 0 ? 0 : selectedTrustModeIndex;

                btnEditAskModeSysPrompt.Caption = LocalizationService.Format("Settings.SysConfig.EditAskPrompt", HostApplicationNames.GetDisplayName(_hostApplicationName));
                btnEditAgentModeSysPrompt.Caption = LocalizationService.Format("Settings.SysConfig.EditAgentPrompt", HostApplicationNames.GetDisplayName(_hostApplicationName));
                btnAddModel.Caption = LocalizationService.Get("Settings.Config.Models.Add");
                btnDeleteModel.Caption = LocalizationService.Get("Settings.Config.Models.Delete");
                btnExportListToJson.Caption = LocalizationService.Get("Settings.Config.Common.ExportList");
                btnImportList.Caption = LocalizationService.Get("Settings.Config.Models.Import");
                btnShowPreview.Caption = LocalizationService.Get("Settings.Config.Models.ShowPromptPreview");
                btnEditModel.Caption = LocalizationService.Get("Settings.Config.Models.Edit");
                btnNewMcp.Caption = LocalizationService.Get("Settings.Config.Mcp.Add");
                btnEditMcp.Caption = LocalizationService.Get("Settings.Config.Mcp.Edit");
                btnDelMcp.Caption = LocalizationService.Get("Settings.Config.Mcp.Delete");
                btnShowDesc.Caption = LocalizationService.Get("Settings.Config.Tools.Details");
                btnImportMcp.Caption = LocalizationService.Get("Settings.Config.Mcp.Import");
                btnExportMcp.Caption = LocalizationService.Get("Settings.Config.Common.ExportList");
                btnNewSkill.Caption = LocalizationService.Get("Settings.Config.Skills.Add");
                btnEditSkill.Caption = LocalizationService.Get("Settings.Config.Skills.Edit");
                btnDeleteSkill.Caption = LocalizationService.Get("Settings.Config.Skills.Delete");
                btnExportSkills.Caption = LocalizationService.Get("Settings.Config.Common.ExportList");
                btnImportSkills.Caption = LocalizationService.Get("Settings.Config.Skills.Import");
                btnSaveMem.Caption = LocalizationService.Get("Settings.Config.Common.Save");
                btnSaveGenenalConfig.Caption = LocalizationService.Get("Settings.Config.Common.Save");
                btnCancelSaveGeneralConfig.Caption = LocalizationService.Get("Settings.Config.Common.Cancel");
            }
            finally
            {
                _loadingSettings = wasLoadingSettings;
            }
        }
    }
}
