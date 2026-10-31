using DevExpress.XtraBars;
using DevExpress.XtraEditors.Repository;
using DevExpress.XtraEditors.Controls;
using DevExpress.XtraGrid.Views.Base;
using DevExpress.XtraGrid.Views.Grid;
using Markdig;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OfficeCopilot.Agent.Abstractions;
using OfficeCopilot.Host.Excel.Tools;
using OfficeCopilot.Host.PowerPoint.Tools;
using OfficeCopilot.Host.Word.Tools;
using OfficeCopilot.Localization;
using OfficeCopilot.Memory;
using OfficeCopilot.Settings;
using OfficeCopilot.Settings.Abstractions;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;
using TreeList = DevExpress.XtraTreeList.TreeList;
using TreeListNode = DevExpress.XtraTreeList.Nodes.TreeListNode;
using FocusedNodeChangedEventArgs = DevExpress.XtraTreeList.FocusedNodeChangedEventArgs;

namespace OfficeCopilot.Settings.WinForms
{
    public partial class fmConfig : Form
    {
        private readonly ISettingsStore _settingsStore;
        private readonly IToolRegistry _toolRegistry;
        private readonly IServiceProvider _toolServices;
        private readonly string _toolHostApplicationName;
        private OfficeCopilotSettings _settings;
        private BindingList<ModelProviderSettings> _models;
        private BindingList<ToolGroupRow> _toolGroups;
        private List<ToolRow> _allToolRows;
        private BindingList<ToolRow> _visibleToolRows;
        private BindingList<McpServerRow> _mcpServers;
        private BindingList<SkillRow> _skillRows;
        private BarButtonItem _btnCreateFromSession;
        private RepositoryItemCheckEdit _groupRegistrationEditor;
        private RepositoryItemCheckEdit _toolRegistrationEditor;
        private RepositoryItemCheckEdit _toolAuthorizationEditor;
        private RepositoryItemButtonEdit _toolTestButtonEditor;
        private RepositoryItemCheckEdit _mcpServerRegistrationEditor;
        private RepositoryItemCheckEdit _skillEnabledEditor;
        private System.Windows.Forms.TextBox _pptDefaultStyle;
        private System.Windows.Forms.ComboBox _pptDefaultDensity;
        private System.Windows.Forms.ComboBox _pptDefaultBuildMode;
        private System.Windows.Forms.NumericUpDown _pptRepairIterationCap;
        private System.Windows.Forms.NumericUpDown _pptWorkspaceRetentionDays;
        private System.Windows.Forms.CheckBox _pptEmbedFontsByDefault;
        private System.Windows.Forms.Button _pptSelfTestButton;
        private System.Windows.Forms.GroupBox _pptSlidesGroup;
        private System.Windows.Forms.Label _pptDefaultStyleLabel;
        private System.Windows.Forms.Label _pptDefaultDensityLabel;
        private System.Windows.Forms.Label _pptDefaultBuildModeLabel;
        private System.Windows.Forms.Label _pptRepairIterationCapLabel;
        private System.Windows.Forms.Label _pptWorkspaceRetentionDaysLabel;
        private ToolStripMenuItem _modelEditMenuItem;
        private ToolStripMenuItem _modelDeleteMenuItem;
        private ToolStripMenuItem _mcpEditMenuItem;
        private ToolStripMenuItem _mcpDeleteMenuItem;
        private ToolStripMenuItem _skillEditMenuItem;
        private ToolStripMenuItem _skillDeleteMenuItem;
        private ToolStripMenuItem _toolEditMenuItem;
        private ToolStripMenuItem _memoryDeleteMenuItem;
        private bool _loadingSettings;

        // Memory manager fields
        private bool _loadingMemoryTree;
        private MemoryManager _memoryManager;
        private Timer _memoryPreviewTimer;
        private MemoryNodeType _currentMemNodeType;
        private string _currentMemNodeId;
        private bool _memoryDirty;
        private bool _skipSaveOnClose;

        public fmConfig()
            : this(null, null, null, null)
        {
        }

        public fmConfig(
            ISettingsStore settingsStore,
            IToolRegistry toolRegistry,
            IServiceProvider toolServices,
            string toolHostApplicationName)
        {
            InitializeComponent();

            _settingsStore = settingsStore ?? new JsonSettingsStore();
            _toolRegistry = toolRegistry;
            _toolServices = toolServices;
            _toolHostApplicationName = toolHostApplicationName ?? string.Empty;
            InitializePowerPointSlidesSettingsControls();

            // Phase P3: Add "从会话创建" button to Skill toolbar
            _btnCreateFromSession = new BarButtonItem(barMan, LocalizationService.Get("Settings.Config.Skills.CreateFromSession"));
            _btnCreateFromSession.Caption = LocalizationService.Get("Settings.Config.Skills.CreateFromSession");
            _btnCreateFromSession.Hint = LocalizationService.Get("Settings.Config.Skills.CreateFromSessionHint");
            _btnCreateFromSession.ItemClick += btnCreateFromSession_ItemClick;
            _btnCreateFromSession.PaintStyle = BarItemPaintStyle.CaptionGlyph;
            _btnCreateFromSession.Glyph = null; // No glyph needed
            barSkillTop.AddItem(_btnCreateFromSession);

            InitializeToolViews();
            WireGeneralTabEvents();
            InitializeMemoryTab();
            WireEvents();
            ApplyLocalization();
            LocalizationService.LanguageChanged += LocalizationService_LanguageChanged;
            Disposed += fmConfig_Disposed;
        }

        private void WireEvents()
        {
            this.Load += fmConfig_Load;
            this.FormClosing += fmConfig_FormClosing;

            btnAddModel.ItemClick += btnAddModel_ItemClick;
            btnEditModel.ItemClick += gvModels_DoubleClick;
            btnDeleteModel.ItemClick += btnDeleteModel_ItemClick;
            btnExportListToJson.ItemClick += btnExportListToJson_ItemClick;
            btnImportList.ItemClick += btnImportList_ItemClick;
            btnShowPreview.ItemClick += btnShowPreview_ItemClick;
            btnNewMcp.ItemClick += btnNewMcp_ItemClick;
            btnEditMcp.ItemClick += btnEditMcp_ItemClick;
            btnDelMcp.ItemClick += btnDelMcp_ItemClick;
            btnExportMcp.ItemClick += btnExportMcp_ItemClick;
            btnImportMcp.ItemClick += btnImportMcp_ItemClick;
            btnNewSkill.ItemClick += btnNewSkill_ItemClick;
            btnEditSkill.ItemClick += btnEditSkill_ItemClick;
            btnDeleteSkill.ItemClick += btnDeleteSkill_ItemClick;
            btnExportSkills.ItemClick += btnExportSkills_ItemClick;
            btnImportSkills.ItemClick += btnImportSkills_ItemClick;

            gvModels.DoubleClick += gvModels_DoubleClick;
            gvModels.CustomColumnDisplayText += gvModels_CustomColumnDisplayText;

            spHttpRequestTimeOut.EditValueChanged += spHttpRequestTimeOut_EditValueChanged;
            //ceLogChat.CheckedChanged += ceLogChat_CheckedChanged;
            seAttachmentFileCountThreshold.EditValueChanged += GeneralThreshold_EditValueChanged;
            seMaxVisualPages.EditValueChanged += GeneralThreshold_EditValueChanged;
            seAttachmentInlineCharBudget.EditValueChanged += GeneralThreshold_EditValueChanged;
            seNativeThresholdKB.EditValueChanged += GeneralThreshold_EditValueChanged;
            seInlineTextThresholdMB.EditValueChanged += GeneralThreshold_EditValueChanged;
            seToolFetchFileThresholdMB.EditValueChanged += GeneralThreshold_EditValueChanged;
            ceAutoResizeBigImage.CheckedChanged += ceAutoResizeBigImage_CheckedChanged;

            gvToolsGroup.FocusedRowChanged += gvToolsGroup_FocusedRowChanged;
            gvToolsGroup.CellValueChanged += gvToolsGroup_CellValueChanged;
            gvTools.CellValueChanged += gvTools_CellValueChanged;
            gvTools.DoubleClick += gvTools_DoubleClick;
            gvMcpServer.FocusedRowChanged += gvMcpServer_FocusedRowChanged;
            gvMcpServer.CellValueChanged += gvMcpServer_CellValueChanged;
            gvMcpServer.DoubleClick += gvMcpServer_DoubleClick;
            gvSkills.FocusedRowChanged += gvSkills_FocusedRowChanged;
            gvSkills.CellValueChanged += gvSkills_CellValueChanged;
            gvSkills.DoubleClick += gvSkills_DoubleClick;

            // Memory tab events
            tlMemFileTree.FocusedNodeChanged += tlMemFileTree_FocusedNodeChanged;
            btnSaveMem.ItemClick += btnSaveMem_ItemClick;
            txtMarkDown.TextChanged += txtMarkDown_TextChanged;
            xtcMemMarkDown.SelectedPageChanged += xtcMemMarkDown_SelectedPageChanged;

            // General tab save/cancel buttons
            btnSaveGenenalConfig.ItemClick += btnSaveGenenalConfig_ItemClick;
            btnCancelSaveGeneralConfig.ItemClick += btnCancelSaveGeneralConfig_ItemClick;

            // ---- 右键菜单：模型列表 ----
            var ctxModels = new ContextMenuStrip();
            _modelEditMenuItem = new ToolStripMenuItem(LocalizationService.Get("Settings.Config.Context.Edit"), Properties.Resources.loadpagesetup_16x16, (s, ev) => EditFocusedModel());
            _modelDeleteMenuItem = new ToolStripMenuItem(LocalizationService.Get("Settings.Config.Context.Delete"), Properties.Resources.deletelist_16x16, (s, ev) => btnDeleteModel_ItemClick(s, null));
            ctxModels.Items.AddRange(new ToolStripItem[] { _modelEditMenuItem, _modelDeleteMenuItem });
            ctxModels.Opening += (s, ev) =>
            {
                ev.Cancel = GetFocusedModel() == null;
            };
            GridContextMenuHelper.AttachToDataRows(gcModels, gvModels, ctxModels);

            // ---- 右键菜单：MCP 服务列表 ----
            var ctxMcp = new ContextMenuStrip();
            _mcpEditMenuItem = new ToolStripMenuItem(LocalizationService.Get("Settings.Config.Context.Edit"), Properties.Resources.edit_16x16, (s, ev) => EditFocusedMcpServer());
            _mcpDeleteMenuItem = new ToolStripMenuItem(LocalizationService.Get("Settings.Config.Context.Delete"), Properties.Resources.deletelist_16x16, (s, ev) => btnDelMcp_ItemClick(s, null));
            ctxMcp.Items.AddRange(new ToolStripItem[] { _mcpEditMenuItem, _mcpDeleteMenuItem });
            ctxMcp.Opening += (s, ev) =>
            {
                ev.Cancel = GetFocusedMcpServer() == null;
            };
            GridContextMenuHelper.AttachToDataRows(gcMcpServer, gvMcpServer, ctxMcp);

            // ---- 右键菜单：Skill 列表 ----
            var ctxSkills = new ContextMenuStrip();
            _skillEditMenuItem = new ToolStripMenuItem(LocalizationService.Get("Settings.Config.Context.Edit"), Properties.Resources.edit_16x16, (s, ev) => EditFocusedSkill());
            _skillDeleteMenuItem = new ToolStripMenuItem(LocalizationService.Get("Settings.Config.Context.Delete"), Properties.Resources.deletelist_16x16, (s, ev) => btnDeleteSkill_ItemClick(s, null));
            ctxSkills.Items.AddRange(new ToolStripItem[] { _skillEditMenuItem, _skillDeleteMenuItem });
            ctxSkills.Opening += (s, ev) =>
            {
                ev.Cancel = GetFocusedSkillRow() == null;
            };
            GridContextMenuHelper.AttachToDataRows(gcSkills, gvSkills, ctxSkills);

            // ---- 右键菜单：工具列表 ----
            var ctxTools = new ContextMenuStrip();
            _toolEditMenuItem = new ToolStripMenuItem(LocalizationService.Get("Settings.Config.Context.Edit"), Properties.Resources.edit_16x16, (s, ev) => OpenToolEditor(false));
            ctxTools.Items.Add(_toolEditMenuItem);
            ctxTools.Opening += (s, ev) =>
            {
                ev.Cancel = GetFocusedTool() == null;
            };
            GridContextMenuHelper.AttachToDataRows(gcTools, gvTools, ctxTools);
        }

        private void fmConfig_Load(object sender, EventArgs e)
        {
            ApplyLocalization();
            LoadSettings();

            // Load memory tree (lightweight, file-based)
            LoadMemoryTree();

            gvModels.OptionsBehavior.Editable = true;
            gvModels.OptionsSelection.EnableAppearanceFocusedCell = false;
            gvModels.OptionsView.ShowPreview = false;
            RefreshPreviewButtonCaption();
        }

        private void fmConfig_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (_loadingSettings || e == null)
            {
                return;
            }

            FinishFormClosing(e);
        }

        private void LocalizationService_LanguageChanged(object sender, EventArgs e)
        {
            ApplyLocalization();
            UpdateMemoryInfoBar();
        }

        private void fmConfig_Disposed(object sender, EventArgs e)
        {
            LocalizationService.LanguageChanged -= LocalizationService_LanguageChanged;
        }

        private void ApplyLocalization()
        {
            var wasLoadingSettings = _loadingSettings;
            _loadingSettings = true;
            try
            {
                Text = LocalizationService.Get("Settings.Config.Title");
                xtpModels.Text = LocalizationService.Get("Settings.Config.Tab.Models");
                xtpTools.Text = LocalizationService.Get("Settings.Config.Tab.Tools");
                xtpMcp.Text = LocalizationService.Get("Settings.Config.Tab.Mcp");
                xtpSkills.Text = LocalizationService.Get("Settings.Config.Tab.Skills");
                xtpMemoryMan.Text = LocalizationService.Get("Settings.Config.Tab.Memory");
                xtpGeneral.Text = LocalizationService.Get("Settings.Config.Tab.General");

                colId.Caption = LocalizationService.Get("Settings.Config.Models.Id");
                colName.Caption = LocalizationService.Get("Settings.Config.Models.Name");
                colProviderKind.Caption = LocalizationService.Get("Settings.Config.Models.ProviderKind");
                colEndpoint.Caption = LocalizationService.Get("Settings.Config.Models.Endpoint");
                colApiKey.Caption = LocalizationService.Get("Settings.Config.Models.ApiKey");
                colModelId.Caption = LocalizationService.Get("Settings.Config.Models.ModelId");
                colSupportsImageInput.Caption = LocalizationService.Get("Settings.Config.Models.Multimodal");
                colIsEnabled.Caption = LocalizationService.Get("Settings.Config.Common.Enabled");
                btnAddModel.Caption = LocalizationService.Get("Settings.Config.Models.Add");
                btnEditModel.Caption = LocalizationService.Get("Settings.Config.Models.Edit");
                btnDeleteModel.Caption = LocalizationService.Get("Settings.Config.Models.Delete");
                btnExportListToJson.Caption = LocalizationService.Get("Settings.Config.Common.ExportList");
                btnImportList.Caption = LocalizationService.Get("Settings.Config.Models.Import");

                btnNewMcp.Caption = LocalizationService.Get("Settings.Config.Mcp.Add");
                btnEditMcp.Caption = LocalizationService.Get("Settings.Config.Mcp.Edit");
                btnDelMcp.Caption = LocalizationService.Get("Settings.Config.Mcp.Delete");
                btnExportMcp.Caption = LocalizationService.Get("Settings.Config.Common.ExportList");
                btnImportMcp.Caption = LocalizationService.Get("Settings.Config.Mcp.Import");
                colMcpId.Caption = LocalizationService.Get("Settings.Config.Mcp.Id");
                colMcpName.Caption = LocalizationService.Get("Settings.Config.Mcp.Name");
                colMcpDesc.Caption = LocalizationService.Get("Settings.Config.Mcp.Description");
                colEnableMcp.Caption = LocalizationService.Get("Settings.Config.Common.Enabled");

                btnNewSkill.Caption = LocalizationService.Get("Settings.Config.Skills.Add");
                btnEditSkill.Caption = LocalizationService.Get("Settings.Config.Skills.Edit");
                btnDeleteSkill.Caption = LocalizationService.Get("Settings.Config.Skills.Delete");
                btnExportSkills.Caption = LocalizationService.Get("Settings.Config.Common.ExportList");
                btnImportSkills.Caption = LocalizationService.Get("Settings.Config.Skills.Import");
                colSkillId.Caption = LocalizationService.Get("Settings.Config.Skills.Id");
                colSkillName.Caption = LocalizationService.Get("Settings.Config.Skills.Name");
                colSkillDesc.Caption = LocalizationService.Get("Settings.Config.Skills.Description");
                colSkillEnabled.Caption = LocalizationService.Get("Settings.Config.Common.Enabled");

                colToolsGroupName.Caption = LocalizationService.Get("Settings.Config.Tools.Group");
                colToolsGroupEnable.Caption = LocalizationService.Get("Settings.Config.Common.Enabled");
                colToolName.Caption = LocalizationService.Get("Settings.Config.Tools.Name");
                colDescription.Caption = LocalizationService.Get("Settings.Config.Tools.Description");
                colEnableTool.Caption = LocalizationService.Get("Settings.Config.Common.Enabled");
                colNeedConfirm.Caption = LocalizationService.Get("Settings.Config.Tools.NoConfirmation");
                colTestTool.Caption = LocalizationService.Get("Settings.Config.Tools.Details");
                btnShowDesc.Caption = LocalizationService.Get("Settings.Config.Tools.Details");

                btnSaveMem.Caption = LocalizationService.Get("Settings.Config.Common.Save");
                xtpMemWebPreview.Text = LocalizationService.Get("Settings.Config.Memory.Preview");
                xtpMemMarkDown.Text = LocalizationService.Get("Settings.Config.Memory.Markdown");
                if (_memoryDeleteMenuItem != null)
                {
                    _memoryDeleteMenuItem.Text = LocalizationService.Get("Settings.Config.Memory.DeleteFragment");
                }

                grpLogging.Text = LocalizationService.Get("Settings.Config.General.Logging");
                lbTranscriptMaxContentLength.Text = LocalizationService.Get("Settings.Config.General.TranscriptMaxLength");
                ceToolCallLogEnabled.Properties.Caption = LocalizationService.Get("Settings.Config.General.ToolCallLogEnabled");
                ceTranscriptIncludeHostContext.Properties.Caption = LocalizationService.Get("Settings.Config.General.TranscriptHostContext");
                ceTranscriptIncludeToolCalls.Properties.Caption = LocalizationService.Get("Settings.Config.General.TranscriptToolCalls");
                lbRetainedFileCount.Text = LocalizationService.Get("Settings.Config.General.RetainedFileCount");
                lbMaxFileSizeMb.Text = LocalizationService.Get("Settings.Config.General.MaxFileSize");
                lbRuntimeLogLevel.Text = LocalizationService.Get("Settings.Config.General.RuntimeLogLevel");
                ceRuntimeLogEnabled.Properties.Caption = LocalizationService.Get("Settings.Config.General.RuntimeLogEnabled");
                lbTranscriptLogMode.Text = LocalizationService.Get("Settings.Config.General.TranscriptLogMode");
                grpRuntime.Text = LocalizationService.Get("Settings.Config.General.Runtime");
                ceAutoMemoryRecorder.Properties.Caption = LocalizationService.Get("Settings.Config.General.AutoMemoryRecorder");
                ceAutoCompactEnabled.Properties.Caption = LocalizationService.Get("Settings.Config.General.AutoCompact");
                grpModelParams.Text = LocalizationService.Get("Settings.Config.General.ModelParameters");
                lbHttpRequestTimeOut.Text = LocalizationService.Get("Settings.Config.General.RequestTimeout");
                lbNativeThresholdKB.Text = LocalizationService.Get("Settings.Config.General.NativeFileSizeLimit");
                lbInlineTextThresholdMB.Text = LocalizationService.Get("Settings.Config.General.InlineTextFileSizeLimit");
                ceAutoResizeBigImage.Properties.Caption = LocalizationService.Get("Settings.Config.General.AutoResizeImages");
                lbToolFetchFileThresholdMB.Text = LocalizationService.Get("Settings.Config.General.ToolFetchFileSizeLimit");
                lbAttachmentFileCountThreshold.Text = LocalizationService.Get("Settings.Config.General.AttachmentFileCountLimit");
                lbAttachmentInlineCharBudget.Text = LocalizationService.Get("Settings.Config.General.AttachmentInlineCharacterBudget");
                lbMaxVisualPages.Text = LocalizationService.Get("Settings.Config.General.MaxVisualPages");
                btnSaveGenenalConfig.Caption = LocalizationService.Get("Settings.Config.Common.Save");
                btnCancelSaveGeneralConfig.Caption = LocalizationService.Get("Settings.Config.Common.Cancel");

                if (_btnCreateFromSession != null)
                {
                    _btnCreateFromSession.Caption = LocalizationService.Get("Settings.Config.Skills.CreateFromSession");
                    _btnCreateFromSession.Hint = LocalizationService.Get("Settings.Config.Skills.CreateFromSessionHint");
                }

                if (_modelEditMenuItem != null)
                {
                    _modelEditMenuItem.Text = LocalizationService.Get("Settings.Config.Context.Edit");
                    _modelDeleteMenuItem.Text = LocalizationService.Get("Settings.Config.Context.Delete");
                    _mcpEditMenuItem.Text = LocalizationService.Get("Settings.Config.Context.Edit");
                    _mcpDeleteMenuItem.Text = LocalizationService.Get("Settings.Config.Context.Delete");
                    _skillEditMenuItem.Text = LocalizationService.Get("Settings.Config.Context.Edit");
                    _skillDeleteMenuItem.Text = LocalizationService.Get("Settings.Config.Context.Delete");
                    _toolEditMenuItem.Text = LocalizationService.Get("Settings.Config.Context.Edit");
                }

                var transcriptLogModeIndex = cbeTranscriptLogMode.SelectedIndex;
                cbeTranscriptLogMode.Properties.Items.Clear();
                cbeTranscriptLogMode.Properties.Items.AddRange(new[]
                {
                    LocalizationService.Get("Settings.Config.General.TranscriptMode.Disabled"),
                    LocalizationService.Get("Settings.Config.General.TranscriptMode.Metadata"),
                    LocalizationService.Get("Settings.Config.General.TranscriptMode.Redacted"),
                    LocalizationService.Get("Settings.Config.General.TranscriptMode.Raw")
                });
                if (transcriptLogModeIndex >= 0)
                {
                    cbeTranscriptLogMode.SelectedIndex = Math.Min(
                        transcriptLogModeIndex,
                        cbeTranscriptLogMode.Properties.Items.Count - 1);
                }

                if (_pptSlidesGroup != null)
                {
                    _pptSlidesGroup.Text = LocalizationService.Get("Settings.Config.PowerPoint.Group");
                    _pptDefaultStyleLabel.Text = LocalizationService.Get("Settings.Config.PowerPoint.DefaultStyle");
                    _pptDefaultDensityLabel.Text = LocalizationService.Get("Settings.Config.PowerPoint.DefaultDensity");
                    _pptDefaultBuildModeLabel.Text = LocalizationService.Get("Settings.Config.PowerPoint.DefaultBuildMode");
                    _pptRepairIterationCapLabel.Text = LocalizationService.Get("Settings.Config.PowerPoint.RepairIterationCap");
                    _pptWorkspaceRetentionDaysLabel.Text = LocalizationService.Get("Settings.Config.PowerPoint.WorkspaceRetentionDays");
                    _pptEmbedFontsByDefault.Text = LocalizationService.Get("Settings.Config.PowerPoint.EmbedFontsByDefault");
                    _pptSelfTestButton.Text = LocalizationService.Get("Settings.Config.PowerPoint.RunSelfTest");
                }

                if (_toolGroups != null)
                {
                    foreach (var group in _toolGroups)
                    {
                        group.DisplayName = GetGroupDisplayName(group.GroupName);
                    }

                    gvToolsGroup.RefreshData();
                }

                RefreshPreviewButtonCaption();
            }
            finally
            {
                _loadingSettings = wasLoadingSettings;
            }
        }

        private void FinishFormClosing(FormClosingEventArgs e)
        {
            if (_skipSaveOnClose)
            {
                return;
            }

            try
            {
                CommitPendingEditors();
                ApplyToolSettings();
                SaveSettings();
                RebuildToolRegistry();
            }
            catch (Exception ex)
            {
                var result = MessageBox.Show(
                    this,
                    LocalizationService.Format("Settings.Config.Error.SaveOnCloseFailed", ex.Message),
                    LocalizationService.Get("Settings.Config.DialogTitle.Settings"),
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Error);
                if (result == DialogResult.Yes)
                {
                    // 放弃保存，允许关闭
                    _skipSaveOnClose = true;
                }
                else
                {
                    e.Cancel = true;
                }
            }
        }

        private void InitializeToolViews()
        {
            colToolsGroupName.FieldName = "DisplayName";
            colToolsGroupEnable.FieldName = "IsRegistered";
            colToolName.FieldName = "DisplayName";
            colDescription.FieldName = "Description";
            colEnableTool.FieldName = "IsRegistered";
            colNeedConfirm.FieldName = "IsAutomaticAuthorization";
            colTestTool.FieldName = "TestActionText";
            colMcpName.FieldName = "DisplayName";
            colEnableMcp.FieldName = "IsEnabled";
            colSkillName.FieldName = "Name";
            colSkillDesc.FieldName = "Description";
            colSkillEnabled.FieldName = "IsEnabled";

            gvToolsGroup.OptionsBehavior.Editable = true;
            gvToolsGroup.OptionsSelection.EnableAppearanceFocusedCell = false;
            gvTools.OptionsBehavior.Editable = true;
            gvTools.OptionsSelection.EnableAppearanceFocusedCell = false;
            gvMcpServer.OptionsBehavior.Editable = true;
            gvMcpServer.OptionsSelection.EnableAppearanceFocusedCell = false;

            colToolsGroupName.OptionsColumn.AllowEdit = false;
            colToolName.OptionsColumn.AllowEdit = false;
            colDescription.OptionsColumn.AllowEdit = false;
            colToolsGroupEnable.OptionsColumn.AllowEdit = true;
            colEnableTool.OptionsColumn.AllowEdit = true;
            colNeedConfirm.OptionsColumn.AllowEdit = true;
            colTestTool.OptionsColumn.AllowEdit = true;
            colMcpName.OptionsColumn.AllowEdit = false;
            colMcpDesc.OptionsColumn.AllowEdit = false;
            colEnableMcp.OptionsColumn.AllowEdit = true;
            colSkillName.OptionsColumn.AllowEdit = false;
            colSkillDesc.OptionsColumn.AllowEdit = false;
            colSkillEnabled.OptionsColumn.AllowEdit = true;
            gvSkills.OptionsBehavior.Editable = true;
            gvSkills.OptionsSelection.EnableAppearanceFocusedCell = false;

            _groupRegistrationEditor = CreateRegistrationEditor();
            _toolRegistrationEditor = CreateRegistrationEditor();
            _toolAuthorizationEditor = CreateAuthorizationEditor();
            _toolTestButtonEditor = CreateToolTestButtonEditor();
            _mcpServerRegistrationEditor = CreateRegistrationEditor();
            _skillEnabledEditor = CreateRegistrationEditor();

            gcToolsGroup.RepositoryItems.Add(_groupRegistrationEditor);
            gcTools.RepositoryItems.Add(_toolRegistrationEditor);
            gcTools.RepositoryItems.Add(_toolAuthorizationEditor);
            gcTools.RepositoryItems.Add(_toolTestButtonEditor);
            gcMcpServer.RepositoryItems.Add(_mcpServerRegistrationEditor);
            gcSkills.RepositoryItems.Add(_skillEnabledEditor);

            colToolsGroupEnable.ColumnEdit = _groupRegistrationEditor;
            colEnableTool.ColumnEdit = _toolRegistrationEditor;
            colNeedConfirm.ColumnEdit = _toolAuthorizationEditor;
            colTestTool.ColumnEdit = _toolTestButtonEditor;
            colEnableMcp.ColumnEdit = _mcpServerRegistrationEditor;
            colSkillEnabled.ColumnEdit = _skillEnabledEditor;
        }

        private static RepositoryItemCheckEdit CreateRegistrationEditor()
        {
            var editor = new RepositoryItemCheckEdit();
            editor.ValueChecked = true;
            editor.ValueUnchecked = false;
            editor.NullStyle = DevExpress.XtraEditors.Controls.StyleIndeterminate.Unchecked;
            return editor;
        }

        private static RepositoryItemCheckEdit CreateAuthorizationEditor()
        {
            var editor = new RepositoryItemCheckEdit();
            editor.ValueChecked = true;
            editor.ValueUnchecked = false;
            editor.NullStyle = DevExpress.XtraEditors.Controls.StyleIndeterminate.Unchecked;
            return editor;
        }

        private RepositoryItemButtonEdit CreateToolTestButtonEditor()
        {
            var editor = new RepositoryItemButtonEdit();
            editor.TextEditStyle = TextEditStyles.HideTextEditor;
            editor.Buttons.Clear();
            editor.Buttons.Add(new EditorButton(ButtonPredefines.Glyph)
            {
                Caption = LocalizationService.Get("Settings.Config.Tools.Details"),
                Kind = ButtonPredefines.Glyph,
                IsDefaultButton = true
            });
            editor.ButtonClick += ToolTestButtonEditor_ButtonClick;
            return editor;
        }

        /// <summary>
        /// 为 Designer 中新增的系统设置控件绑定事件处理。
        /// </summary>
        private void WireGeneralTabEvents()
        {
            cbeRuntimeLogLevel.Properties.Items.AddRange(new string[] {
                "Trace", "Debug", "Information", "Warning", "Error", "Critical", "None"
            });

            // Folder browser for JSONL directory
            //beJsonlRootDirectory.ButtonClick += (s, e) =>
            //{
            //    using (var dlg = new System.Windows.Forms.FolderBrowserDialog())
            //    {
            //        dlg.Description = "选择 JSONL 会话存储目录";
            //        if (!string.IsNullOrWhiteSpace(beJsonlRootDirectory.Text) &&
            //            System.IO.Directory.Exists(beJsonlRootDirectory.Text))
            //        {
            //            dlg.SelectedPath = beJsonlRootDirectory.Text;
            //        }
            //        if (dlg.ShowDialog(this) == System.Windows.Forms.DialogResult.OK)
            //        {
            //            beJsonlRootDirectory.Text = dlg.SelectedPath;
            //            SaveGeneralSettings();
            //        }
            //    }
            //};

            // Enable scrolling for the general tab (content exceeds visible area)
            xtpGeneral.AutoScroll = true;
            //ceAutoRestoreConversation.CheckedChanged += GeneralSetting_Changed;
            //// Runtime params
            //ceEnableSkillPlanning.CheckedChanged += GeneralSetting_Changed;
            //seMaxToolRoundCount.EditValueChanged += GeneralSetting_Changed;
            //cbeDefaultSkillName.EditValueChanged += GeneralSetting_Changed;
            //seContextWindowSize.EditValueChanged += GeneralSetting_Changed;
            seContextWindowSize.EditValueChanged += GeneralSetting_Changed;
            ceAutoCompactEnabled.CheckedChanged += GeneralSetting_Changed;
            //ceMicroCompactEnabled.CheckedChanged += GeneralSetting_Changed;
            //seMicroCompactCharThreshold.EditValueChanged += GeneralSetting_Changed;
            //// Storage
            //cbeStorageMode.EditValueChanged += GeneralSetting_Changed;
            //beJsonlRootDirectory.EditValueChanged += GeneralSetting_Changed;
            // Logging
            cbeTranscriptLogMode.EditValueChanged += GeneralSetting_Changed;
            ceRuntimeLogEnabled.CheckedChanged += GeneralSetting_Changed;
            cbeRuntimeLogLevel.EditValueChanged += GeneralSetting_Changed;
            //ceEnableDebugTrace.CheckedChanged += GeneralSetting_Changed;
            //ceEnableFileLog.CheckedChanged += GeneralSetting_Changed;
            //ceIncludeScopes.CheckedChanged += GeneralSetting_Changed;
            seMaxFileSizeMb.EditValueChanged += GeneralSetting_Changed;
            seRetainedFileCount.EditValueChanged += GeneralSetting_Changed;
            ceTranscriptIncludeToolCalls.CheckedChanged += GeneralSetting_Changed;
            ceTranscriptIncludeHostContext.CheckedChanged += GeneralSetting_Changed;
            //ceTranscriptMaskSensitive.CheckedChanged += GeneralSetting_Changed;
            seTranscriptMaxContentLength.EditValueChanged += GeneralSetting_Changed;
            ceToolCallLogEnabled.CheckedChanged += GeneralSetting_Changed;
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
                MessageBox.Show(this, LocalizationService.Format("Settings.Config.Error.LoadSettings", ex.Message),
                    LocalizationService.Get("Settings.Config.DialogTitle.Model"), MessageBoxButtons.OK, MessageBoxIcon.Error);
                _settings = OfficeCopilotSettingsFactory.CreateDefault();
            }

            if (_settings.ModelProviders == null)
            {
                _settings.ModelProviders = new List<ModelProviderSettings>();
            }

            RebindModels(_settings.ModelProviders);
            BindGeneralSettings();
            LoadToolSettings();
            LoadMcpSettings();
            LoadSkillSettings();
            //gvModels.BestFitColumns();

            _loadingSettings = false;
        }

        private void RebindModels(IEnumerable<ModelProviderSettings> models)
        {
            var list = models == null
                ? new List<ModelProviderSettings>()
                : models.Where(p => p != null).OrderBy(p => p.Id).ToList();

            _models = new BindingList<ModelProviderSettings>(list);
            gcModels.DataSource = _models;
        }

        private void LoadToolSettings()
        {
            var toolRows = BuildToolRows();
            var groupRows = BuildToolGroupRows(toolRows);

            _toolGroups = new BindingList<ToolGroupRow>(groupRows);
            _allToolRows = toolRows;

            gcToolsGroup.DataSource = _toolGroups;

            if (_toolGroups.Count > 0)
            {
                gvToolsGroup.FocusedRowHandle = 0;
            }

            RefreshVisibleToolRows();
        }

        private void LoadMcpSettings()
        {
            var servers = _settings == null || _settings.McpServers == null
                ? new List<McpServerSettings>()
                : _settings.McpServers.Select(OfficeCopilotSettingsFactory.NormalizeMcpServer).ToList();

            _mcpServers = new BindingList<McpServerRow>(BuildMcpServerRows(servers));

            gcMcpServer.DataSource = _mcpServers;

            if (_mcpServers.Count > 0)
            {
                gvMcpServer.FocusedRowHandle = 0;
            }
        }

        private static List<McpServerRow> BuildMcpServerRows(IEnumerable<McpServerSettings> servers)
        {
            return servers == null
                ? new List<McpServerRow>()
                : servers.Where(p => p != null)
                    .Select(p => new McpServerRow
                    {
                        ServerId = p.Id,
                        DisplayName = string.IsNullOrWhiteSpace(p.Name) ? p.Id : p.Name,
                        Description = p.Description ?? string.Empty,
                        IsEnabled = p.IsEnabled,
                        ServerType = p.McpType
                    })
                    .OrderBy(p => p.DisplayName, StringComparer.CurrentCultureIgnoreCase)
                    .ToList();
        }

        // ========== Skills Tab ==========

        private static readonly string SkillsDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "NixerOfficeCopilot", "skills");

        /// <summary>
        /// 内置 Skill 根目录。优先查找 EntryAssembly 同级目录，
        /// 再降级到当前 Assembly 目录。
        /// </summary>
        private static readonly string BuiltInSkillsDirectory = GetBuiltInSkillsDirectory();

        private static string GetBuiltInSkillsDirectory()
        {
            // Office Add-in 场景下：
            // - AppDomain.BaseDirectory 通常是 Office 主程序目录（如 EXCEL.EXE 所在目录）
            // - EntryAssembly 是 Office 主程序
            // 因此当前 DLL (Settings.WinForms.dll) 的 Location 才是真正的安装目录。
            var searchDirs = new List<string>();

            // 1. AppDomain.BaseDirectory
            var baseDir = AppDomain.CurrentDomain.BaseDirectory;
            if (!string.IsNullOrWhiteSpace(baseDir))
                searchDirs.Add(baseDir);

            // 2. EntryAssembly 目录（独立 .exe 场景 / 开发调试场景）
            var entryAsm = System.Reflection.Assembly.GetEntryAssembly();
            if (entryAsm != null)
            {
                var dir = Path.GetDirectoryName(entryAsm.Location);
                if (!string.IsNullOrWhiteSpace(dir))
                    searchDirs.Add(dir);
            }

            // 3. 当前 Assembly (Settings.WinForms.dll) 所在目录 —— VSTO 下这是安装目录
            var thisAsm = System.Reflection.Assembly.GetExecutingAssembly();
            if (thisAsm != null)
            {
                var dir = Path.GetDirectoryName(thisAsm.Location);
                if (!string.IsNullOrWhiteSpace(dir))
                    searchDirs.Add(dir);
            }

            // 收集所有候选目录及其父目录
            var allCandidates = new List<string>(searchDirs);

            // 4. 添加上级目录 —— 处理 Broker 在子目录、BuiltInSkills 在父目录的情况
            foreach (var dir in searchDirs)
            {
                try
                {
                    var parent = Path.GetDirectoryName(dir);
                    if (!string.IsNullOrWhiteSpace(parent) &&
                        !allCandidates.Exists(c => string.Equals(c, parent, StringComparison.OrdinalIgnoreCase)))
                    {
                        allCandidates.Add(parent);
                    }
                }
                catch
                {
                    // ignore invalid paths
                }
            }

            foreach (var dir in allCandidates)
            {
                var path = Path.Combine(dir, "BuiltInSkills");
                if (Directory.Exists(path))
                {
                    FmConfigLog.Write($"[fmConfig] FOUND at: {path}");
                    return path;
                }
            }

            FmConfigLog.Write($"[fmConfig] NOT FOUND. Searched {allCandidates.Count}: {string.Join("; ", allCandidates)}");
            return null;
        }

        private static class FmConfigLog
        {
            public static void Write(string message)
            {
            OfficeCopilot.Diagnostics.Logging.OfficeCopilotLog.Write("Settings.WinForms", message);
            }
        }

        private void LoadSkillSettings()
        {
            var rows = new List<SkillRow>();
            var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // Layer 1: 内置 Skill（只读，随安装包分发）
            if (!string.IsNullOrWhiteSpace(BuiltInSkillsDirectory) && Directory.Exists(BuiltInSkillsDirectory))
            {
                foreach (var subDir in Directory.GetDirectories(BuiltInSkillsDirectory))
                {
                    try
                    {
                        var skillMdPath = Path.Combine(subDir, "SKILL.md");
                        if (!File.Exists(skillMdPath))
                        {
                            continue;
                        }

                        var (name, description, category) = ParseSkillFrontmatter(skillMdPath);
                        if (!string.IsNullOrWhiteSpace(name))
                        {
                            var dirName = Path.GetFileName(subDir);

                            rows.Add(new SkillRow
                            {
                                Id = dirName,
                                Name = name,
                                Description = description ?? string.Empty,
                                Category = category ?? string.Empty,
                                SourceKind = "内置",
                                FilePath = skillMdPath,
                                IsEnabled = true,
                                IsReadOnly = true
                            });

                            seenNames.Add(name);
                        }
                    }
                    catch
                    {
                        // skip invalid built-in skill directories
                    }
                }
            }

            // Layer 2: 文件系统 Skill（用户自定义）
            if (Directory.Exists(SkillsDirectory))
            {
                foreach (var subDir in Directory.GetDirectories(SkillsDirectory))
                {
                    try
                    {
                        var skillMdPath = Path.Combine(subDir, "SKILL.md");
                        if (!File.Exists(skillMdPath))
                        {
                            continue;
                        }

                        var (name, description, category) = ParseSkillFrontmatter(skillMdPath);
                        if (!string.IsNullOrWhiteSpace(name))
                        {
                            var dirName = Path.GetFileName(subDir);

                            // 如果用户创建了与内置 Skill 同名的文件系统 Skill，
                            // 移除之前的内置条目，用文件系统条目替代（可编辑）
                            if (seenNames.Contains(name))
                            {
                                var existing = rows.Find(r =>
                                    string.Equals(r.Name, name, StringComparison.OrdinalIgnoreCase) &&
                                    r.IsReadOnly);
                                if (existing != null)
                                {
                                    rows.Remove(existing);
                                }
                            }

                            rows.Add(new SkillRow
                            {
                                Id = dirName,
                                Name = name,
                                Description = description ?? string.Empty,
                                Category = category ?? string.Empty,
                                SourceKind = "文件系统",
                                FilePath = skillMdPath,
                                IsEnabled = true
                            });

                            seenNames.Add(name);
                        }
                    }
                    catch
                    {
                        // skip invalid skill directories
                    }
                }
            }

            // 启用状态以各 SKILL.md 的 enabled 字段为准
            foreach (var row in rows)
            {
                row.IsEnabled = SkillFrontmatterText.ReadEnabled(row.FilePath);
            }

            _skillRows = new BindingList<SkillRow>(rows.OrderBy(r => r.Name, StringComparer.CurrentCultureIgnoreCase).ToList());
            gcSkills.DataSource = _skillRows;

            if (_skillRows.Count > 0)
            {
                gvSkills.FocusedRowHandle = 0;
            }
        }

        /// <summary>
        /// 简单解析 SKILL.md 的 YAML frontmatter，提取 name、description、category。
        /// </summary>
        private static (string name, string description, string category) ParseSkillFrontmatter(string filePath)
        {
            var content = File.ReadAllText(filePath, System.Text.Encoding.UTF8);
            if (content.Length > 0 && content[0] == '\uFEFF')
                content = content.Substring(1);

            var match = Regex.Match(content, @"^---\s*\r?\n(.*?)\r?\n---", RegexOptions.Singleline);
            if (!match.Success)
            {
                return (null, null, null);
            }

            var yaml = match.Groups[1].Value;
            var nameMatch = Regex.Match(yaml, @"^name\s*:\s*(.+)$", RegexOptions.Multiline);
            var descMatch = Regex.Match(yaml, @"^description\s*:\s*(.+)$", RegexOptions.Multiline);
            var catMatch = Regex.Match(yaml, @"^category\s*:\s*(.+)$", RegexOptions.Multiline);

            var name = nameMatch.Success ? nameMatch.Groups[1].Value.Trim() : null;
            var desc = descMatch.Success ? descMatch.Groups[1].Value.Trim() : null;
            var cat = catMatch.Success ? catMatch.Groups[1].Value.Trim() : null;

            return (name, desc, cat);
        }

        /// <summary>
        /// 检测鼠标双击位置是否命中 GridView 的数据行单元格（排除列标题、空白区域等）。
        /// </summary>
        private static bool IsDoubleClickOnRowCell(object sender)
        {
            var view = sender as GridView;
            if (view == null) return false;
            var hitInfo = view.CalcHitInfo(view.GridControl.PointToClient(Control.MousePosition));
            return hitInfo.InRowCell;
        }

        private SkillRow GetFocusedSkillRow()
        {
            if (gvSkills == null || gvSkills.FocusedRowHandle < 0)
            {
                return null;
            }

            return gvSkills.GetRow(gvSkills.FocusedRowHandle) as SkillRow;
        }

        private void gvSkills_FocusedRowChanged(object sender, FocusedRowChangedEventArgs e)
        {
        }

        private void gvSkills_CellValueChanged(object sender, CellValueChangedEventArgs e)
        {
            if (_loadingSettings)
            {
                return;
            }

            if (e.Column == colSkillEnabled)
            {
                var row = gvSkills.GetRow(e.RowHandle) as SkillRow;
                if (row != null)
                {
                    SaveSkillEnabledState(row);
                }
            }
        }

        private void gvSkills_DoubleClick(object sender, EventArgs e)
        {
            if (!IsDoubleClickOnRowCell(sender)) return;
            EditFocusedSkill();
        }

        private void SaveSkillEnabledState(SkillRow row)
        {
            if (row == null)
            {
                return;
            }

            try
            {
                SkillFrontmatterText.SetEnabled(row.FilePath, row.IsEnabled);
            }
            catch (Exception ex)
            {
                MessageBox.Show(LocalizationService.Format("Settings.Config.Error.SaveSkillEnabledState", ex.Message),
                    LocalizationService.Get("Settings.Config.DialogTitle.Skills"), MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void btnNewSkill_ItemClick(object sender, ItemClickEventArgs e)
        {
            using (var editor = new fmSkillEditor(SkillsDirectory))
            {
                if (editor.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }

                LoadSkillSettings();
            }
        }

        /// <summary>
        /// Phase P3: "从会话创建" — 提示用户在 Copilot 面板中使用 skillify。
        /// </summary>
        private void btnCreateFromSession_ItemClick(object sender, ItemClickEventArgs e)
        {
            var result = MessageBox.Show(
                this,
                LocalizationService.Get("Settings.Config.Skills.CreateFromSessionPrompt"),
                LocalizationService.Get("Settings.Config.DialogTitle.CreateSkillFromSession"),
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Information);

            if (result == DialogResult.Yes)
            {
                using (var editor = new fmSkillEditor(SkillsDirectory))
                {
                    if (editor.ShowDialog(this) == DialogResult.OK)
                    {
                        LoadSkillSettings();
                    }
                }
            }
        }

        private void btnEditSkill_ItemClick(object sender, ItemClickEventArgs e)
        {
            EditFocusedSkill();
        }

        private void EditFocusedSkill()
        {
            var row = GetFocusedSkillRow();
            if (row == null)
            {
                MessageBox.Show(this, LocalizationService.Get("Settings.Config.Skills.SelectionRequired"),
                    LocalizationService.Get("Settings.Config.DialogTitle.Skills"), MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            if (row.IsReadOnly)
            {
                MessageBox.Show(this,
                    LocalizationService.Format("Settings.Config.Skills.BuiltinEditNotAllowed", row.Name),
                    LocalizationService.Get("Settings.Config.DialogTitle.Skills"), MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            if (!File.Exists(row.FilePath))
            {
                MessageBox.Show(this, LocalizationService.Format("Settings.Config.Skills.FileMissing", row.FilePath),
                    LocalizationService.Get("Settings.Config.DialogTitle.Skills"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            using (var editor = new fmSkillEditor(row.FilePath, isNew: false))
            {
                if (editor.ShowDialog(this) == DialogResult.OK)
                {
                    LoadSkillSettings();
                }
            }
        }

        private void btnDeleteSkill_ItemClick(object sender, ItemClickEventArgs e)
        {
            var row = GetFocusedSkillRow();
            if (row == null)
            {
                MessageBox.Show(this, LocalizationService.Get("Settings.Config.Skills.SelectionRequired"),
                    LocalizationService.Get("Settings.Config.DialogTitle.Skills"), MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            if (row.IsReadOnly)
            {
                MessageBox.Show(this,
                    LocalizationService.Format("Settings.Config.Skills.BuiltinDeleteNotAllowed", row.Name),
                    LocalizationService.Get("Settings.Config.DialogTitle.Skills"), MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            if (string.IsNullOrWhiteSpace(row.FilePath))
            {
                MessageBox.Show(this, LocalizationService.Get("Settings.Config.Skills.FilePathMissing"),
                    LocalizationService.Get("Settings.Config.DialogTitle.Skills"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            var result = MessageBox.Show(
                this,
                LocalizationService.Format("Settings.Config.Skills.ConfirmDelete", row.Name, Path.GetDirectoryName(row.FilePath)),
                LocalizationService.Get("Settings.Config.DialogTitle.Skills"),
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);

            if (result != DialogResult.Yes)
            {
                return;
            }

            try
            {
                var dir = Path.GetDirectoryName(row.FilePath);
                if (Directory.Exists(dir))
                {
                    Directory.Delete(dir, true);
                }

                LoadSkillSettings();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, LocalizationService.Format("Settings.Config.Error.DeleteSkill", ex.Message),
                    LocalizationService.Get("Settings.Config.DialogTitle.Skills"), MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void btnExportSkills_ItemClick(object sender, ItemClickEventArgs e)
        {
            using (var dialog = new SaveFileDialog())
            {
                dialog.Title = LocalizationService.Get("Settings.Config.Skills.ExportDialogTitle");
                dialog.Filter = LocalizationService.Get("Settings.Config.Skills.ExportFilter");
                dialog.FileName = "skills-export.nsk";
                dialog.FilterIndex = 1;

                if (dialog.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }

                try
                {
                    var isNsk = dialog.FileName.EndsWith(".nsk", StringComparison.OrdinalIgnoreCase);

                    if (isNsk)
                    {
                        ExportAsNskPackage(dialog.FileName);
                    }
                    else
                    {
                        ExportAsJson(dialog.FileName);
                    }

                    MessageBox.Show(this, LocalizationService.Get("Settings.Config.Common.ExportComplete"),
                        LocalizationService.Get("Settings.Config.DialogTitle.Skills"), MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, LocalizationService.Format("Settings.Config.Common.ExportFailed", ex.Message),
                        LocalizationService.Get("Settings.Config.DialogTitle.Skills"), MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        /// <summary>
        /// 以 .nsk (ZIP) 格式导出：包含 manifest.json 和每个 Skill 的完整目录。
        /// </summary>
        private void ExportAsNskPackage(string filePath)
        {
            using (var zip = ZipFile.Open(filePath, ZipArchiveMode.Create))
            {
                // 1. 写入 manifest.json
                var exportList = _skillRows.Select(r => new
                {
                    r.Id,
                    r.Name,
                    r.Description,
                    r.Category,
                    r.SourceKind,
                    r.IsEnabled
                }).ToList();

                var manifestJson = JsonConvert.SerializeObject(exportList, Formatting.Indented);
                var manifestEntry = zip.CreateEntry("manifest.json");
                using (var writer = new StreamWriter(manifestEntry.Open()))
                {
                    writer.Write(manifestJson);
                }

                // 2. 打包每个 Skill 的目录
                foreach (var row in _skillRows)
                {
                    if (row.IsReadOnly)
                    {
                        continue; // 跳过内置只读 Skill（已随安装包分发）
                    }

                    if (string.IsNullOrWhiteSpace(row.FilePath) || !File.Exists(row.FilePath))
                    {
                        continue;
                    }

                    var skillDir = Path.GetDirectoryName(row.FilePath);
                    if (!Directory.Exists(skillDir))
                    {
                        continue;
                    }

                    var dirName = Path.GetFileName(skillDir);
                    var entryPrefix = "skills/" + dirName + "/";

                    // 递归添加目录下所有文件
                    foreach (var file in Directory.GetFiles(skillDir, "*", SearchOption.AllDirectories))
                    {
                        var relativePath = entryPrefix + GetRelativePath(skillDir, file);
                        zip.CreateEntryFromFile(file, relativePath.Replace('\\', '/'));
                    }
                }
            }
        }

        /// <summary>
        /// 仅导出 JSON 元数据（兼容旧格式，不含文件内容）。
        /// </summary>
        private void ExportAsJson(string filePath)
        {
            var exportList = _skillRows.Select(r => new
            {
                r.Id,
                r.Name,
                r.Description,
                r.FilePath,
                r.IsEnabled
            }).ToList();

            var json = JsonConvert.SerializeObject(exportList, Formatting.Indented);
            File.WriteAllText(filePath, json, System.Text.Encoding.UTF8);
        }

        private void btnImportSkills_ItemClick(object sender, ItemClickEventArgs e)
        {
            using (var dialog = new OpenFileDialog())
            {
                dialog.Title = LocalizationService.Get("Settings.Config.Skills.ImportDialogTitle");
                dialog.Filter = LocalizationService.Get("Settings.Config.Skills.ImportFilter");
                dialog.FilterIndex = 1;

                if (dialog.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }

                try
                {
                    if (dialog.FileName.EndsWith(".nsk", StringComparison.OrdinalIgnoreCase))
                    {
                        ImportFromNskPackage(dialog.FileName);
                    }
                    else
                    {
                        ImportFromJson(dialog.FileName);
                    }

                    LoadSkillSettings();
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, LocalizationService.Format("Settings.Config.Common.ImportFailed", ex.Message),
                        LocalizationService.Get("Settings.Config.DialogTitle.Skills"), MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        /// <summary>
        /// 从 .nsk (ZIP) 包导入 Skill：解包到 SkillsDirectory。
        /// </summary>
        private void ImportFromNskPackage(string filePath)
        {
            var manifestEntries = new List<SkillRow>();
            int extractedCount = 0;
            int skippedCount = 0;

            using (var zip = ZipFile.OpenRead(filePath))
            {
                // 1. 读取 manifest.json
                var manifestEntry = zip.GetEntry("manifest.json");
                if (manifestEntry != null)
                {
                    using (var reader = new StreamReader(manifestEntry.Open()))
                    {
                        var json = reader.ReadToEnd();
                        manifestEntries = JsonConvert.DeserializeObject<List<SkillRow>>(json) ?? new List<SkillRow>();
                    }
                }

                // 2. 提取 skills/ 下的所有文件
                var skillsDir = SkillsDirectory;

                if (!Directory.Exists(skillsDir))
                {
                    Directory.CreateDirectory(skillsDir);
                }

                foreach (var entry in zip.Entries)
                {
                    // 跳过目录条目和 manifest.json
                    if (string.IsNullOrWhiteSpace(entry.Name) || entry.FullName == "manifest.json")
                    {
                        continue;
                    }

                    // 只处理 skills/ 下的文件
                    if (!entry.FullName.StartsWith("skills/", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    // 计算目标路径: skills/<skillName>/... → %APPDATA%/NixerOfficeCopilot/skills/<skillName>/...
                    var relativePath = entry.FullName.Substring("skills/".Length);
                    var targetPath = Path.Combine(skillsDir, relativePath.Replace('/', Path.DirectorySeparatorChar));

                    // 安全检查：防止目录遍历攻击
                    if (!targetPath.StartsWith(skillsDir, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    // 检查冲突
                    if (File.Exists(targetPath))
                    {
                        // 检查是否同名 Skill 已存在
                        var skillName = relativePath.Split(new[] { '/', '\\' }, 2)[0];
                        var existingSkillEntry = manifestEntries.Find(
                            m => string.Equals(m.Id, skillName, StringComparison.OrdinalIgnoreCase));
                        if (existingSkillEntry != null)
                        {
                            skippedCount++;
                            continue; // 跳过已存在的
                        }
                    }

                    // 确保目标目录存在
                    var targetDir = Path.GetDirectoryName(targetPath);
                    if (!Directory.Exists(targetDir))
                    {
                        Directory.CreateDirectory(targetDir);
                    }

                    // 解压
                    entry.ExtractToFile(targetPath, overwrite: true);
                    extractedCount++;
                }
            }

            // 3. 恢复 enabled 状态
            foreach (var item in manifestEntries)
            {
                if (string.IsNullOrWhiteSpace(item.Id)) continue;
                var localSkillMd = Path.Combine(SkillsDirectory, item.Id, "SKILL.md");
                if (!File.Exists(localSkillMd)) continue;
                item.FilePath = localSkillMd;
                SaveSkillEnabledState(item);
            }

            MessageBox.Show(this,
                LocalizationService.Format("Settings.Config.Skills.NskImportComplete",
                    extractedCount,
                    skippedCount > 0
                        ? LocalizationService.Format("Settings.Config.Skills.ExistingFilesSkipped", skippedCount)
                        : ""),
                LocalizationService.Get("Settings.Config.DialogTitle.Skills"),
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }

        /// <summary>
        /// 从旧格式 JSON 导入 Skill 配置（仅更新 IsEnabled 状态，不恢复文件）。
        /// </summary>
        private void ImportFromJson(string filePath)
        {
            var confirm = MessageBox.Show(
                this,
                LocalizationService.Get("Settings.Config.Skills.JsonImportWarning"),
                LocalizationService.Get("Settings.Config.DialogTitle.Skills"),
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);

            if (confirm != DialogResult.Yes)
            {
                return;
            }

            var json = File.ReadAllText(filePath, System.Text.Encoding.UTF8);
            var importList = JsonConvert.DeserializeObject<List<SkillRow>>(json);

            if (importList != null)
            {
                int count = 0;
                foreach (var item in importList)
                {
                    if (!string.IsNullOrWhiteSpace(item.FilePath) && File.Exists(item.FilePath))
                    {
                        SaveSkillEnabledState(item);
                        count++;
                    }
                }
                MessageBox.Show(this,
                    LocalizationService.Format("Settings.Config.Skills.JsonImportComplete", count),
                    LocalizationService.Get("Settings.Config.DialogTitle.Skills"),
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }
        }

        /// <summary>
        /// 获取相对于基础目录的相对路径。
        /// </summary>
        private static string GetRelativePath(string basePath, string fullPath)
        {
            if (!basePath.EndsWith(Path.DirectorySeparatorChar.ToString()))
                basePath += Path.DirectorySeparatorChar;

            var baseUri = new Uri(basePath);
            var fullUri = new Uri(fullPath);
            return Uri.UnescapeDataString(baseUri.MakeRelativeUri(fullUri).ToString());
        }

        // ========== End Skills Tab ==========

        // ========== Memory Manager Tab ==========

        private void InitializeMemoryTab()
        {
            _memoryManager = new MemoryManager(new FileSystemMemoryStore());

            _memoryPreviewTimer = new Timer { Interval = 500 };
            _memoryPreviewTimer.Tick += MemoryPreviewTimer_Tick;

            // Configure TreeList — 单列纯树视图，不要表格格式
            tlMemFileTree.OptionsBehavior.Editable = false;
            tlMemFileTree.OptionsSelection.EnableAppearanceFocusedCell = false;
            tlMemFileTree.OptionsView.ShowColumns = false;        // 隐藏列头
            tlMemFileTree.OptionsView.ShowIndicator = false;      // 隐藏行指示器
            tlMemFileTree.OptionsView.ShowVertLines = false;      // 隐藏竖线
            tlMemFileTree.OptionsView.ShowHorzLines = false;      // 隐藏横线
            tlMemFileTree.OptionsView.ShowCheckBoxes = false;
            tlMemFileTree.OptionsView.FocusRectStyle = DevExpress.XtraTreeList.DrawFocusRectStyle.None;
            tlMemFileTree.OptionsView.TreeLineStyle = DevExpress.XtraTreeList.LineStyle.Solid;
            tlMemFileTree.KeyFieldName = "Id";
            tlMemFileTree.ParentFieldName = "ParentId";

            // 节点图标：入口用 bookmark，片段用 richeditbookmark
            var imgCollection = new DevExpress.Utils.ImageCollection();
            imgCollection.AddImage(Properties.Resources.bookmark_16x16);         // index 0: MEMORY.md
            imgCollection.AddImage(Properties.Resources.richeditbookmark_16x16); // index 1: fragment
            tlMemFileTree.StateImageList = imgCollection;

            var colName = tlMemFileTree.Columns.Add();
            colName.Caption = "";
            colName.FieldName = "Name";
            colName.VisibleIndex = 0;

            // 右侧编辑器配置（与 fmSkillEditor 一致）
            txtMarkDown.Options.Behavior.Open = DevExpress.XtraRichEdit.DocumentCapability.Hidden;
            txtMarkDown.Options.Behavior.Save = DevExpress.XtraRichEdit.DocumentCapability.Hidden;
            txtMarkDown.Options.Behavior.SaveAs = DevExpress.XtraRichEdit.DocumentCapability.Hidden;
            txtMarkDown.Options.Behavior.CreateNew = DevExpress.XtraRichEdit.DocumentCapability.Hidden;
            txtMarkDown.Options.HorizontalRuler.Visibility = DevExpress.XtraRichEdit.RichEditRulerVisibility.Hidden;

            webPreview.Navigate("about:blank");

            // 右键菜单：仅在 Fragment/IndexEntry 节点显示
            var ctxMenu = new ContextMenuStrip();
            _memoryDeleteMenuItem = new ToolStripMenuItem(
                LocalizationService.Get("Settings.Config.Memory.DeleteFragment"),
                Properties.Resources.deletelist_16x16,
                OnDeleteMemFragment);
            ctxMenu.Items.Add(_memoryDeleteMenuItem);
            ctxMenu.Opening += (s, args) =>
            {
                var node = tlMemFileTree.FocusedNode;
                if (node?.Tag is MemTreeNodeTag tag
                    && (tag.NodeType == MemoryNodeType.Fragment || tag.NodeType == MemoryNodeType.IndexEntry))
                {
                    args.Cancel = false;
                }
                else
                {
                    args.Cancel = true;
                }
            };
            tlMemFileTree.ContextMenuStrip = ctxMenu;

            // 右键时自动选中鼠标下的节点
            tlMemFileTree.MouseDown += (s, args) =>
            {
                if (args.Button == MouseButtons.Right)
                {
                    var hitInfo = tlMemFileTree.CalcHitInfo(args.Location);
                    if (hitInfo.Node != null)
                    {
                        tlMemFileTree.FocusedNode = hitInfo.Node;
                    }
                }
            };

            UpdateMemoryInfoBar();
        }

        private void LoadMemoryTree()
        {
            _loadingMemoryTree = true;
            try
            {
                // Ensure MEMORY.md exists and is not empty
                Task.Run(() => _memoryManager.EnsureEntrypointExistsAsync()).Wait();

                // 保存展开状态
                var expandedIds = new HashSet<string>();
                foreach (TreeListNode node in tlMemFileTree.Nodes)
                    CollectExpandedNodes(node, expandedIds);

                tlMemFileTree.BeginUnboundLoad();
                tlMemFileTree.Nodes.Clear();

                var entryNode = Task.Run(() => _memoryManager.BuildTreeAsync()).Result;

                // MEMORY.md 直接作为根节点
                var rootNode = tlMemFileTree.AppendNode(
                    new object[] { entryNode.Name },
                    null);
                rootNode.Tag = new MemTreeNodeTag
                {
                    NodeType = MemoryNodeType.Entrypoint,
                    NodeId = entryNode.Id
                };
                rootNode.StateImageIndex = 0; // bookmark 图标

                AppendTreeChildren(rootNode, entryNode.Children);

                tlMemFileTree.EndUnboundLoad();

                // 展开状态必须在 EndUnboundLoad 之后设置
                rootNode.Expanded = true;
                RestoreExpandedNodes(rootNode, expandedIds);

                // 默认选中根节点 MEMORY.md
                tlMemFileTree.FocusedNode = rootNode;

                // Load root content directly to the right panel
                try
                {
                    var rootContent = Task.Run(() => _memoryManager.LoadContentAsync(
                        MemoryNodeType.Entrypoint, entryNode.Id)).Result;
                    txtMarkDown.Text = rootContent ?? string.Empty;
                    _currentMemNodeType = MemoryNodeType.Entrypoint;
                    _currentMemNodeId = entryNode.Id;
                    _memoryDirty = false;
                    UpdateMemoryPreview();
                }
                catch (Exception)
                {
                }

                UpdateMemoryInfoBar();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, LocalizationService.Format("Settings.Config.Error.LoadMemoryTree", ex.Message),
                    LocalizationService.Get("Settings.Config.DialogTitle.Memory"), MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                _loadingMemoryTree = false;
            }
        }

        private static void CollectExpandedNodes(TreeListNode node, HashSet<string> expandedIds)
        {
            if (node.Expanded && node.Tag is MemTreeNodeTag tag)
                expandedIds.Add(tag.NodeId);
            foreach (TreeListNode child in node.Nodes)
                CollectExpandedNodes(child, expandedIds);
        }

        private static void RestoreExpandedNodes(TreeListNode node, HashSet<string> expandedIds)
        {
            if (node.Tag is MemTreeNodeTag tag && expandedIds.Contains(tag.NodeId))
                node.Expanded = true;
            foreach (TreeListNode child in node.Nodes)
                RestoreExpandedNodes(child, expandedIds);
        }

        private void AppendTreeChildren(TreeListNode parentNode, List<MemoryTreeNode> children)
        {
            foreach (var child in children)
            {
                // IndexEntry 节点显示摘要名称，Fragment 节点显示文件名+预览
                var displayName = child.NodeType == MemoryNodeType.IndexEntry
                    ? child.Name
                    : child.NodeType == MemoryNodeType.Fragment
                        ? child.Name  // 未索引片段，name 已是截断的预览
                        : child.Name;

                var node = tlMemFileTree.AppendNode(
                    new object[] { displayName },
                    parentNode);
                node.Tag = new MemTreeNodeTag
                {
                    NodeType = child.NodeType,
                    NodeId = child.Id
                };

                // 设置图标：Entrypoint → 0 (bookmark)，Fragment/IndexEntry → 1 (richeditbookmark)
                node.StateImageIndex = child.NodeType == MemoryNodeType.Entrypoint ? 0 : 1;

                AppendTreeChildren(node, child.Children);
            }
        }

        private void tlMemFileTree_FocusedNodeChanged(object sender, FocusedNodeChangedEventArgs e)
        {
            // Auto-save previous node if dirty (skip during initial tree load)
            if (_memoryDirty && !_loadingMemoryTree)
            {
                SaveCurrentMemoryNode();
            }

            var node = e.Node;
            if (node?.Tag is MemTreeNodeTag tag)
            {
                LoadMemoryNodeContent(tag.NodeType, tag.NodeId);
            }
        }

        private void LoadMemoryNodeContent(MemoryNodeType nodeType, string nodeId)
        {
            try
            {
                // LoadContentAsync 内部实现均为同步（Task.FromResult），直接 .Result 安全
                var content = _memoryManager.LoadContentAsync(nodeType, nodeId).Result;

                _currentMemNodeType = nodeType;
                _currentMemNodeId = nodeId;

                // 先设置文本（会触发 TextChanged → _memoryDirty=true），再重置 _memoryDirty
                txtMarkDown.Text = content ?? string.Empty;
                _memoryDirty = false;

                UpdateMemoryPreview();
                UpdateMemoryInfoBar();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, LocalizationService.Format("Settings.Config.Error.LoadMemoryContent", ex.Message),
                    LocalizationService.Get("Settings.Config.DialogTitle.Memory"), MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void txtMarkDown_TextChanged(object sender, EventArgs e)
        {
            _memoryDirty = true;
            _memoryPreviewTimer.Stop();
            _memoryPreviewTimer.Start();

            UpdateMemoryInfoBar();
        }

        private void MemoryPreviewTimer_Tick(object sender, EventArgs e)
        {
            _memoryPreviewTimer.Stop();
            UpdateMemoryPreview();
        }

        private void xtcMemMarkDown_SelectedPageChanged(object sender, DevExpress.XtraTab.TabPageChangedEventArgs e)
        {
            if (e.Page == xtpMemWebPreview)
            {
                UpdateMemoryPreview();
            }
        }

        private void UpdateMemoryPreview()
        {
            try
            {
                var markdown = txtMarkDown.Text ?? string.Empty;
                if (string.IsNullOrWhiteSpace(markdown))
                {
                    webPreview.DocumentText = "<html><body style='font-family:Segoe UI,sans-serif;padding:16px;color:#999;'><p>"
                        + LocalizationService.Get("Settings.Config.Memory.EmptyPreview")
                        + "</p></body></html>";
                    return;
                }

                var pipeline = new MarkdownPipelineBuilder().UseAdvancedExtensions().Build();
                var html = Markdown.ToHtml(markdown, pipeline);
                var fullHtml = @"<!DOCTYPE html><html><head><meta charset=""utf-8""><style>
body{font-family:'Segoe UI','Microsoft YaHei',sans-serif;padding:16px;line-height:1.6;color:#333;}
h1,h2,h3{color:#1a5276;}h1{border-bottom:2px solid #1a5276;padding-bottom:8px;}
h2{border-bottom:1px solid #ddd;padding-bottom:4px;}
code{background:#f4f4f4;padding:2px 6px;border-radius:3px;font-family:Consolas,'Courier New',monospace;}
pre{background:#2d2d2d;color:#f8f8f2;padding:16px;border-radius:6px;overflow-x:auto;}
pre code{background:none;padding:0;color:inherit;}
blockquote{border-left:4px solid #1a5276;padding-left:16px;margin-left:0;color:#666;}
table{border-collapse:collapse;width:100%;}
th,td{border:1px solid #ddd;padding:8px;text-align:left;}th{background:#f0f0f0;}
a{color:#2980b9;}
</style></head><body>" + html + "</body></html>";
                webPreview.DocumentText = fullHtml;
            }
            catch { /* preview failure is non-critical */ }
        }

        private void btnSaveMem_ItemClick(object sender, ItemClickEventArgs e)
        {
            SaveCurrentMemoryNode();
            LoadMemoryTree();
        }

        private void SaveCurrentMemoryNode()
        {
            if (!_memoryDirty)
                return;

            if (_currentMemNodeType != MemoryNodeType.Entrypoint
                && _currentMemNodeType != MemoryNodeType.Fragment
                && _currentMemNodeType != MemoryNodeType.IndexEntry)
                return;

            try
            {
                var content = txtMarkDown.Text ?? string.Empty;
                Task.Run(() => _memoryManager.SaveContentAsync(_currentMemNodeType, _currentMemNodeId, content)).Wait();

                _memoryDirty = false;
                UpdateMemoryInfoBar();

                // 不再自动重建树 — 由手动保存按钮或删除操作负责刷新
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, LocalizationService.Format("Settings.Config.Error.SaveMemory", ex.Message),
                    LocalizationService.Get("Settings.Config.DialogTitle.Memory"), MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void UpdateMemoryInfoBar()
        {
            try
            {
                var stats = Task.Run(() => _memoryManager.GetStatsAsync()).Result;

                var dirtyMarker = _memoryDirty
                    ? LocalizationService.Get("Settings.Config.Memory.ModifiedMarker")
                    : "";
                var nodeLabel = _currentMemNodeType switch
                {
                    MemoryNodeType.Entrypoint => "MEMORY.md",
                    MemoryNodeType.Fragment => _currentMemNodeId + ".md",
                    MemoryNodeType.IndexEntry => _currentMemNodeId.StartsWith("index:")
                        ? _currentMemNodeId.Substring(6) + ".md"
                        : _currentMemNodeId + ".md",
                    _ => ""
                };

                if (!string.IsNullOrEmpty(nodeLabel))
                {
                    bsiMemInfo.Caption = LocalizationService.Format(
                        "Settings.Config.Memory.InfoWithNode",
                        nodeLabel,
                        dirtyMarker,
                        stats.EntrypointLineCount,
                        FormatBytes(stats.EntrypointSizeBytes),
                        stats.FragmentCount,
                        FormatBytes(stats.TotalFragmentsSizeBytes),
                        stats.EntrypointTruncated
                            ? LocalizationService.Get("Settings.Config.Memory.EntrypointTruncated")
                            : "");
                }
                else
                {
                    bsiMemInfo.Caption = LocalizationService.Format(
                        "Settings.Config.Memory.Info",
                        stats.EntrypointLineCount,
                        FormatBytes(stats.EntrypointSizeBytes),
                        stats.FragmentCount,
                        FormatBytes(stats.TotalFragmentsSizeBytes),
                        stats.EntrypointTruncated
                            ? LocalizationService.Get("Settings.Config.Memory.EntrypointTruncated")
                            : "");
                }
            }
            catch
            {
                bsiMemInfo.Caption = LocalizationService.Get("Settings.Config.Memory.Manager");
            }
        }

        private void OnDeleteMemFragment(object sender, EventArgs e)
        {
            var node = tlMemFileTree.FocusedNode;
            if (!(node?.Tag is MemTreeNodeTag tag)
                || (tag.NodeType != MemoryNodeType.Fragment && tag.NodeType != MemoryNodeType.IndexEntry))
                return;

            var fragmentId = tag.NodeType == MemoryNodeType.IndexEntry
                ? (tag.NodeId.StartsWith("index:") ? tag.NodeId.Substring(6) : tag.NodeId)
                : tag.NodeId;

            if (string.IsNullOrWhiteSpace(fragmentId))
            {
                MessageBox.Show(this, LocalizationService.Get("Settings.Config.Memory.FragmentIdMissing"),
                    LocalizationService.Get("Settings.Config.DialogTitle.DeleteMemoryFragment"), MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            var result = MessageBox.Show(
                this,
                LocalizationService.Format("Settings.Config.Memory.ConfirmDeleteFragment", fragmentId + ".md"),
                LocalizationService.Get("Settings.Config.DialogTitle.DeleteMemoryFragment"),
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);

            if (result != DialogResult.Yes)
                return;

            try
            {
                Task.Run(() => _memoryManager.DeleteFragmentAndIndexAsync(fragmentId)).Wait();

                // 清空编辑器（如果当前正在编辑被删除的节点）
                if (_currentMemNodeId == tag.NodeId
                    || (_currentMemNodeId.StartsWith("index:") && _currentMemNodeId.Substring(6) == fragmentId))
                {
                    txtMarkDown.Text = string.Empty;
                    _memoryDirty = false;
                }

                // 刷新树
                LoadMemoryTree();

                // 焦点回到 MEMORY.md 根节点
                if (tlMemFileTree.Nodes.Count > 0)
                    tlMemFileTree.FocusedNode = tlMemFileTree.Nodes[0];

                UpdateMemoryInfoBar();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, LocalizationService.Format("Settings.Config.Error.DeleteMemory", ex.Message),
                    LocalizationService.Get("Settings.Config.DialogTitle.Memory"), MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private static string FormatBytes(long bytes)
        {
            if (bytes < 1024) return bytes + "B";
            if (bytes < 1024 * 1024) return (bytes / 1024.0).ToString("F1") + "KB";
            return (bytes / (1024.0 * 1024.0)).ToString("F1") + "MB";
        }

        /// <summary>TreeList 节点 Tag，存储节点类型和 ID。</summary>
        private sealed class MemTreeNodeTag
        {
            public MemoryNodeType NodeType { get; set; }
            public string NodeId { get; set; } = string.Empty;
        }

        // ========== End Memory Manager Tab ==========

        private McpServerRow GetFocusedMcpServer()
        {
            if (gvMcpServer == null || gvMcpServer.FocusedRowHandle < 0)
            {
                return null;
            }

            return gvMcpServer.GetRow(gvMcpServer.FocusedRowHandle) as McpServerRow;
        }

        private McpServerSettings FindMcpServer(string serverId)
        {
            if (string.IsNullOrWhiteSpace(serverId) || _settings == null || _settings.McpServers == null)
            {
                return null;
            }

            var server = _settings.McpServers.FirstOrDefault(p =>
                p != null && string.Equals(p.Id, serverId, StringComparison.OrdinalIgnoreCase));
            return server == null ? null : CloneMcpServer(server);
        }

        private void CommitPendingEditors()
        {
            CommitGridView(gvModels);
            CommitGridView(gvToolsGroup);
            CommitGridView(gvTools);
            CommitGridView(gvMcpServer);
        }

        private static void CommitGridView(GridView view)
        {
            if (view == null)
            {
                return;
            }

            if (view.ActiveEditor != null)
            {
                view.CloseEditor();
            }

            view.PostEditor();
            view.UpdateCurrentRow();
        }

        private List<ToolRow> BuildToolRows()
        {
            // Host-level tools (Excel/Word/PowerPoint built-in tools)
            var excelDefinitions = ExcelBuiltInTools.CreateAllAvailableDefinitions(CreateExcelToolCatalogOptions(_settings));
            var wordDefinitions = WordBuiltInToolCatalog.CreateAllAvailableDefinitions(CreateWordToolCatalogOptions(_settings));
            var powerPointDefinitions = PowerPointBuiltInTools.CreateAllAvailableDefinitions(CreatePowerPointToolCatalogOptions(_settings));

            // Runtime-level tools (Broker built-in tools: PowerShell, TodoWrite, etc.)
            var runtimeDefinitions = RuntimeBuiltInTools.GetAllDefinitions();

            var allDefinitions = new List<ToolDefinition>();
            allDefinitions.AddRange(excelDefinitions ?? System.Array.Empty<ToolDefinition>());
            allDefinitions.AddRange(wordDefinitions ?? System.Array.Empty<ToolDefinition>());
            allDefinitions.AddRange(powerPointDefinitions ?? System.Array.Empty<ToolDefinition>());
            allDefinitions.AddRange(runtimeDefinitions ?? System.Array.Empty<ToolDefinition>());

            var rows = new List<ToolRow>();

            // OpenXml 文件级工具（openxml_file_* 等）被三个宿主的目录共用，会以不同的
            // GroupName 重复出现。注册开关是按**工具名**存取的，所以这里按名字去重，
            // 只保留首次出现的那一行，避免同一开关在界面上出现多份。
            var seenToolNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            for (var i = 0; i < allDefinitions.Count; i++)
            {
                var definition = allDefinitions[i];
                if (definition == null || string.IsNullOrWhiteSpace(definition.Name))
                {
                    continue;
                }

                if (!seenToolNames.Add(definition.Name))
                {
                    continue;
                }

                var groupName = GetToolGroupName(definition);
                var defaultRegistered = IsGroupRegisteredByDefault(groupName);
                var explicitRegistered = _settings.GetToolRegistration(definition.Name);

                rows.Add(new ToolRow
                {
                    GroupName = groupName,
                    ToolName = definition.Name,
                    DisplayName = string.IsNullOrWhiteSpace(definition.DisplayName) ? definition.Name : definition.DisplayName,
                    Description = definition.Description ?? string.Empty,
                    IsRegistered = explicitRegistered ?? defaultRegistered,
                    IsAutomaticAuthorization = _settings.IsToolAutoApproved(definition.Name)
                });
            }

            return rows
                .OrderBy(p => GetGroupOrder(p.GroupName))
                .ThenBy(p => p.DisplayName, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        }

        private List<ToolGroupRow> BuildToolGroupRows(IEnumerable<ToolRow> toolRows)
        {
            var rows = new List<ToolGroupRow>();
            var groupNames = toolRows == null
                ? Enumerable.Empty<string>()
                : toolRows.Where(p => p != null)
                    .Select(p => p.GroupName ?? string.Empty)
                    .Distinct(StringComparer.OrdinalIgnoreCase);

            foreach (var groupName in groupNames.OrderBy(GetGroupOrder).ThenBy(p => p, StringComparer.OrdinalIgnoreCase))
            {
                var explicitRegistered = _settings.GetToolGroupRegistration(groupName);
                rows.Add(new ToolGroupRow
                {
                    GroupName = groupName,
                    DisplayName = GetGroupDisplayName(groupName),
                    IsRegistered = explicitRegistered ?? IsGroupRegisteredByDefault(groupName)
                });
            }

            return rows;
        }

        private void RefreshVisibleToolRows()
        {
            var group = GetFocusedToolGroup();
            var rows = _allToolRows == null
                ? new List<ToolRow>()
                : _allToolRows.Where(p => p != null)
                    .Where(p => group == null || string.Equals(p.GroupName, group.GroupName, StringComparison.OrdinalIgnoreCase))
                    .OrderBy(p => p.DisplayName, StringComparer.CurrentCultureIgnoreCase)
                    .ToList();

            _visibleToolRows = new BindingList<ToolRow>(rows);
            gcTools.DataSource = _visibleToolRows;
        }

        private ToolGroupRow GetFocusedToolGroup()
        {
            if (gvToolsGroup == null || gvToolsGroup.FocusedRowHandle < 0)
            {
                return null;
            }

            return gvToolsGroup.GetRow(gvToolsGroup.FocusedRowHandle) as ToolGroupRow;
        }

        private void gvToolsGroup_FocusedRowChanged(object sender, FocusedRowChangedEventArgs e)
        {
            RefreshVisibleToolRows();
        }

        private void gvToolsGroup_CellValueChanged(object sender, CellValueChangedEventArgs e)
        {
            if (_loadingSettings || e == null || e.Column != colToolsGroupEnable)
            {
                return;
            }

            SaveToolSettings();
        }

        private void ToolTestButtonEditor_ButtonClick(object sender, ButtonPressedEventArgs e)
        {
            OpenToolEditor(true);
        }

        private void gvTools_CellValueChanged(object sender, CellValueChangedEventArgs e)
        {
            if (_loadingSettings || e == null)
            {
                return;
            }

            if (e.Column != colEnableTool && e.Column != colNeedConfirm)
            {
                return;
            }

            SaveToolSettings();
        }

        private void gvTools_DoubleClick(object sender, EventArgs e)
        {
            if (!IsDoubleClickOnRowCell(sender)) return;
            OpenToolEditor(false);
        }

        private void gvMcpServer_FocusedRowChanged(object sender, FocusedRowChangedEventArgs e)
        {
        }

        private void gvMcpServer_CellValueChanged(object sender, CellValueChangedEventArgs e)
        {
            if (_loadingSettings || e == null || e.Column != colEnableMcp)
            {
                return;
            }

            SaveMcpSettings();
        }

        private void SaveToolSettings()
        {
            if (_loadingSettings)
            {
                return;
            }

            try
            {
                ApplyToolSettings();
                _settingsStore.Save(_settings);
                RebuildToolRegistry();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, LocalizationService.Format("Settings.Config.Error.SaveBuiltInTools", ex.Message),
                    LocalizationService.Get("Settings.Config.DialogTitle.Tools"), MessageBoxButtons.OK, MessageBoxIcon.Error);
                LoadSettings();
            }
        }

        private void SaveMcpSettings()
        {
            if (_loadingSettings)
            {
                return;
            }

            try
            {
                ApplyMcpSettings();
                _settingsStore.Save(_settings);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, LocalizationService.Format("Settings.Config.Error.SaveMcp", ex.Message),
                    LocalizationService.Get("Settings.Config.DialogTitle.Mcp"), MessageBoxButtons.OK, MessageBoxIcon.Error);
                LoadSettings();
            }
        }

        private void ApplyToolSettings()
        {
            if (_settings == null)
            {
                _settings = OfficeCopilotSettingsFactory.CreateDefault();
            }

            _settings.ToolGroupRegistrations = new List<ToolGroupRegistrationSettings>();
            _settings.ToolRegistrations = new List<ToolRegistrationSettings>();

            if (_toolGroups != null)
            {
                foreach (var group in _toolGroups.Where(p => p != null))
                {
                    _settings.SetToolGroupRegistration(group.GroupName, group.IsRegistered);
                }
            }

            if (_allToolRows != null)
            {
                foreach (var tool in _allToolRows.Where(p => p != null))
                {
                    _settings.SetToolRegistration(tool.GroupName, tool.ToolName, tool.IsRegistered);
                    _settings.SetToolConfirmationMode(tool.ToolName, tool.IsAutomaticAuthorization);
                }
            }

            _settings.UpdatedAtUtc = DateTime.UtcNow;
            _settings = OfficeCopilotSettingsFactory.Normalize(_settings);
        }

        private void ApplyMcpSettings()
        {
            if (_settings == null)
            {
                _settings = OfficeCopilotSettingsFactory.CreateDefault();
            }

            var servers = new List<McpServerSettings>();
            if (_mcpServers != null)
            {
                foreach (var row in _mcpServers.Where(p => p != null))
                {
                    var server = FindMcpServer(row.ServerId);
                    if (server == null)
                    {
                        continue;
                    }

                    server.Name = row.DisplayName ?? string.Empty;
                    server.Description = row.Description ?? string.Empty;
                    server.IsEnabled = row.IsEnabled;
                    servers.Add(OfficeCopilotSettingsFactory.NormalizeMcpServer(server));
                }
            }

            _settings.McpServers = servers
                .OrderBy(p => p.Name ?? string.Empty, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
            _settings.UpdatedAtUtc = DateTime.UtcNow;
            _settings = OfficeCopilotSettingsFactory.Normalize(_settings);
        }

        private void RebuildToolRegistry()
        {
            var registry = _toolRegistry as InMemoryToolRegistry;
            if (registry == null)
            {
                return;
            }

            registry.Clear();

            var options = CreateExcelToolCatalogOptions(_settings);
            if (options == null)
            {
                registry.RegisterExcelBuiltInTools();
                return;
            }

            registry.RegisterExcelBuiltInTools(options);
        }

        private ToolRow GetFocusedTool()
        {
            if (gvTools == null || gvTools.FocusedRowHandle < 0)
            {
                return null;
            }

            return gvTools.GetRow(gvTools.FocusedRowHandle) as ToolRow;
        }

        private void OpenToolEditor(bool openTesterTab)
        {
            var tool = GetFocusedTool();
            if (tool == null)
            {
                return;
            }

            var baseDefinition = ExcelBuiltInTools.CreateAllAvailableDefinitions()
                .FirstOrDefault(p => p != null && string.Equals(p.Name, tool.ToolName, StringComparison.OrdinalIgnoreCase));

            // Word 内置工具与 Excel 分属不同目录，需要单独查找。
            if (baseDefinition == null)
            {
                baseDefinition = WordBuiltInToolCatalog.CreateAllAvailableDefinitions()
                    .FirstOrDefault(p => p != null && string.Equals(p.Name, tool.ToolName, StringComparison.OrdinalIgnoreCase));
            }

            if (baseDefinition == null)
            {
                baseDefinition = PowerPointBuiltInTools.CreateAllAvailableDefinitions()
                    .FirstOrDefault(p => p != null && string.Equals(p.Name, tool.ToolName, StringComparison.OrdinalIgnoreCase));
            }

            // Runtime tools (PowerShell, TodoWrite, etc.) aren't in ExcelBuiltInTools —
            // look them up in RuntimeBuiltInTools instead.
            if (baseDefinition == null)
            {
                baseDefinition = RuntimeBuiltInTools.GetAllDefinitions()
                    .FirstOrDefault(p => p != null && string.Equals(p.Name, tool.ToolName, StringComparison.OrdinalIgnoreCase));
            }

            if (baseDefinition == null)
            {
                MessageBox.Show(this, LocalizationService.Get("Settings.Config.Tools.BaseDefinitionMissing"),
                    LocalizationService.Get("Settings.Config.DialogTitle.Tools"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            var currentOverride = _settings == null ? null : _settings.GetToolDefinitionOverride(tool.ToolName);

            // 工具清单同时列出三个宿主的工具，编辑器需要按该行自身的宿主去解析运行时实例；
            // 若与当前进程所在宿主不一致，则只允许查看/编辑定义，禁用调用测试。
            // 注意共享的 OpenXML 工具会同时出现在多个宿主目录里，因此以"当前宿主目录是否包含该工具"为准。
            var toolHostName = _toolHostApplicationName;
            var testBlockedReason = string.Empty;
            if (!IsToolInHostCatalog(tool.ToolName, _toolHostApplicationName))
            {
                var ownerHostName = GetToolHostApplicationName(tool.GroupName);
                if (!string.IsNullOrEmpty(ownerHostName))
                {
                    toolHostName = ownerHostName;
                    testBlockedReason = LocalizationService.Format(
                        "Settings.Config.Tools.TestBlockedReason",
                        ownerHostName,
                        string.IsNullOrWhiteSpace(_toolHostApplicationName)
                            ? LocalizationService.Get("Settings.Config.Tools.Host")
                            : _toolHostApplicationName);
                }
            }

            using (var form = new fmToolEditor(baseDefinition, currentOverride, _toolServices, toolHostName, openTesterTab))
            {
                form.TestBlockedReason = testBlockedReason;
                if (form.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }

                try
                {
                    if (_settings == null)
                    {
                        _settings = OfficeCopilotSettingsFactory.CreateDefault();
                    }

                    if (form.DefinitionOverride == null)
                    {
                        _settings.RemoveToolDefinitionOverride(tool.ToolName);
                    }
                    else
                    {
                        _settings.SetToolDefinitionOverride(form.DefinitionOverride);
                    }

                    _settings.UpdatedAtUtc = DateTime.UtcNow;
                    _settings = OfficeCopilotSettingsFactory.Normalize(_settings);
                    _settingsStore.Save(_settings);
                    RebuildToolRegistry();
                    LoadToolSettings();
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, LocalizationService.Format("Settings.Config.Error.SaveToolDefinition", ex.Message),
                        LocalizationService.Get("Settings.Config.DialogTitle.Tools"), MessageBoxButtons.OK, MessageBoxIcon.Error);
                    LoadSettings();
                }
            }
        }

        private static PowerPointToolCatalogOptions CreatePowerPointToolCatalogOptions(OfficeCopilotSettings settings)
        {
            if (settings == null)
            {
                return null;
            }

            if (!settings.HasToolRegistrationConfiguration()
                && !settings.HasToolDefinitionOverrideConfiguration())
            {
                return null;
            }

            var options = new PowerPointToolCatalogOptions();
            if (settings.HasToolRegistrationConfiguration())
            {
                options.Registration = PowerPointBuiltInTools.CreateRegistrationOptions(
                    settings.GetToolGroupRegistration,
                    settings.GetToolRegistration);
            }

            var overrides = settings.GetToolDefinitionOverrides();
            for (var i = 0; i < overrides.Count; i++)
            {
                var definitionOverride = overrides[i];
                if (definitionOverride == null)
                {
                    continue;
                }

                options.DefinitionOverrides.Add(new ToolDefinitionOverride
                {
                    ToolName = PowerPointToolNames.MapLegacy(definitionOverride.ToolName),
                    DisplayName = definitionOverride.DisplayName,
                    Description = definitionOverride.Description,
                    RequiresConfirmation = definitionOverride.RequiresConfirmation,
                    RequiresBackup = definitionOverride.RequiresBackup,
                    InputSchemaJson = definitionOverride.InputSchemaJson,
                    OutputSchemaJson = definitionOverride.OutputSchemaJson,
                    UpdatedAtUtc = definitionOverride.UpdatedAtUtc
                });
            }

            return options;
        }

        private static WordToolCatalogOptions CreateWordToolCatalogOptions(OfficeCopilotSettings settings)
        {
            if (settings == null)
            {
                return null;
            }

            if (!settings.HasToolRegistrationConfiguration()
                && !settings.HasToolDefinitionOverrideConfiguration())
            {
                return null;
            }

            var options = new WordToolCatalogOptions();
            if (settings.HasToolRegistrationConfiguration())
            {
                options.Registration = new WordToolRegistrationOptions(
                    settings.GetRegisteredToolGroupNames(),
                    settings.GetRegisteredToolNames());
            }

            var overrides = settings.GetToolDefinitionOverrides();
            for (var i = 0; i < overrides.Count; i++)
            {
                var definitionOverride = overrides[i];
                if (definitionOverride == null)
                {
                    continue;
                }

                options.DefinitionOverrides.Add(new ToolDefinitionOverride
                {
                    ToolName = definitionOverride.ToolName,
                    DisplayName = definitionOverride.DisplayName,
                    Description = definitionOverride.Description,
                    RequiresConfirmation = definitionOverride.RequiresConfirmation,
                    RequiresBackup = definitionOverride.RequiresBackup,
                    InputSchemaJson = definitionOverride.InputSchemaJson,
                    OutputSchemaJson = definitionOverride.OutputSchemaJson,
                    UpdatedAtUtc = definitionOverride.UpdatedAtUtc
                });
            }

            return options;
        }

        private static ExcelToolCatalogOptions CreateExcelToolCatalogOptions(OfficeCopilotSettings settings)
        {
            if (settings == null)
            {
                return null;
            }

            if (!settings.HasToolRegistrationConfiguration()
                && !settings.HasToolDefinitionOverrideConfiguration())
            {
                return null;
            }

            var options = new ExcelToolCatalogOptions();
            if (settings.HasToolRegistrationConfiguration())
            {
                options.Registration = new ExcelToolRegistrationOptions(
                    settings.GetRegisteredToolGroupNames(),
                    settings.GetRegisteredToolNames());
            }

            var overrides = settings.GetToolDefinitionOverrides();
            for (var i = 0; i < overrides.Count; i++)
            {
                var definitionOverride = overrides[i];
                if (definitionOverride == null)
                {
                    continue;
                }

                options.DefinitionOverrides.Add(new ToolDefinitionOverride
                {
                    ToolName = definitionOverride.ToolName,
                    DisplayName = definitionOverride.DisplayName,
                    Description = definitionOverride.Description,
                    RequiresConfirmation = definitionOverride.RequiresConfirmation,
                    RequiresBackup = definitionOverride.RequiresBackup,
                    InputSchemaJson = definitionOverride.InputSchemaJson,
                    OutputSchemaJson = definitionOverride.OutputSchemaJson,
                    UpdatedAtUtc = definitionOverride.UpdatedAtUtc
                });
            }

            return options;
        }

        private static string GetToolGroupName(ToolDefinition definition)
        {
            if (definition == null || definition.Metadata == null)
            {
                return string.Empty;
            }

            string groupName;
            return definition.Metadata.TryGetValue(ToolMetadataKeys.GroupName, out groupName)
                ? (groupName ?? string.Empty)
                : string.Empty;
        }

        private static bool IsGroupRegisteredByDefault(string groupName)
        {
            return !string.Equals(groupName, ExcelToolGroups.Advanced, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(groupName, WordToolGroups.Advanced, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// 判断指定工具是否存在于某个宿主的内置工具目录中。运行时工具（PowerShell 等）
        /// 在三个宿主中都可用，因此一并视为"存在"。
        /// </summary>
        private static bool IsToolInHostCatalog(string toolName, string hostApplicationName)
        {
            if (string.IsNullOrWhiteSpace(toolName))
            {
                return false;
            }

            if (RuntimeBuiltInTools.GetAllDefinitions()
                .Any(p => p != null && string.Equals(p.Name, toolName, StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }

            System.Collections.Generic.IEnumerable<ToolDefinition> definitions;
            if (string.Equals(hostApplicationName, "Word", StringComparison.OrdinalIgnoreCase))
            {
                definitions = WordBuiltInToolCatalog.CreateAllAvailableDefinitions();
            }
            else if (string.Equals(hostApplicationName, "PowerPoint", StringComparison.OrdinalIgnoreCase))
            {
                definitions = PowerPointBuiltInTools.CreateAllAvailableDefinitions();
            }
            else if (string.Equals(hostApplicationName, "Excel", StringComparison.OrdinalIgnoreCase))
            {
                definitions = ExcelBuiltInTools.CreateAllAvailableDefinitions();
            }
            else
            {
                return false;
            }

            return definitions.Any(p => p != null && string.Equals(p.Name, toolName, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// 由工具分组名（形如 excel.context / word.navigation / powerpoint.file / runtime.command）
        /// 推断所属宿主应用名；运行时工具与未知分组返回空串表示"与当前宿主一致"。
        /// </summary>
        private static string GetToolHostApplicationName(string groupName)
        {
            if (string.IsNullOrWhiteSpace(groupName))
            {
                return string.Empty;
            }

            if (groupName.StartsWith("excel.", StringComparison.OrdinalIgnoreCase))
            {
                return "Excel";
            }

            if (groupName.StartsWith("word.", StringComparison.OrdinalIgnoreCase))
            {
                return "Word";
            }

            if (groupName.StartsWith("powerpoint.", StringComparison.OrdinalIgnoreCase)
                || groupName.StartsWith("ppt.", StringComparison.OrdinalIgnoreCase))
            {
                return "PowerPoint";
            }

            return string.Empty;
        }

        private static int GetGroupOrder(string groupName)
        {
            if (string.Equals(groupName, ExcelToolGroups.Context, StringComparison.OrdinalIgnoreCase))
            {
                return 0;
            }

            if (string.Equals(groupName, ExcelToolGroups.Read, StringComparison.OrdinalIgnoreCase))
            {
                return 1;
            }

            if (string.Equals(groupName, ExcelToolGroups.Write, StringComparison.OrdinalIgnoreCase))
            {
                return 2;
            }

            if (string.Equals(groupName, ExcelToolGroups.Advanced, StringComparison.OrdinalIgnoreCase))
            {
                return 3;
            }

            if (string.Equals(groupName, ExcelToolGroups.File, StringComparison.OrdinalIgnoreCase))
            {
                return 4;
            }

            // Word tool groups
            if (string.Equals(groupName, WordToolGroups.Context, StringComparison.OrdinalIgnoreCase))
            {
                return 5;
            }

            if (string.Equals(groupName, WordToolGroups.Read, StringComparison.OrdinalIgnoreCase))
            {
                return 6;
            }

            if (string.Equals(groupName, WordToolGroups.Write, StringComparison.OrdinalIgnoreCase))
            {
                return 7;
            }

            if (string.Equals(groupName, WordToolGroups.Navigation, StringComparison.OrdinalIgnoreCase))
            {
                return 8;
            }

            if (string.Equals(groupName, WordToolGroups.Advanced, StringComparison.OrdinalIgnoreCase))
            {
                return 9;
            }

            if (string.Equals(groupName, WordToolGroups.File, StringComparison.OrdinalIgnoreCase))
            {
                return 10;
            }

            // PowerPoint tool groups
            if (string.Equals(groupName, PowerPointToolGroups.Context, StringComparison.OrdinalIgnoreCase))
            {
                return 11;
            }

            if (string.Equals(groupName, PowerPointToolGroups.Read, StringComparison.OrdinalIgnoreCase))
            {
                return 12;
            }

            if (string.Equals(groupName, PowerPointToolGroups.Backup, StringComparison.OrdinalIgnoreCase))
            {
                return 13;
            }

            if (string.Equals(groupName, PowerPointToolGroups.Style, StringComparison.OrdinalIgnoreCase))
            {
                return 14;
            }

            if (string.Equals(groupName, PowerPointToolGroups.Deck, StringComparison.OrdinalIgnoreCase))
            {
                return 15;
            }

            if (string.Equals(groupName, PowerPointToolGroups.File, StringComparison.OrdinalIgnoreCase))
            {
                return 16;
            }

            // Runtime tool groups — displayed after host groups
            if (string.Equals(groupName, RuntimeToolGroups.Shell, StringComparison.OrdinalIgnoreCase))
            {
                return 20;
            }

            if (string.Equals(groupName, RuntimeToolGroups.Interaction, StringComparison.OrdinalIgnoreCase))
            {
                return 21;
            }

            if (string.Equals(groupName, RuntimeToolGroups.Planning, StringComparison.OrdinalIgnoreCase))
            {
                return 22;
            }

            if (string.Equals(groupName, RuntimeToolGroups.Notification, StringComparison.OrdinalIgnoreCase))
            {
                return 23;
            }

            return int.MaxValue;
        }

        private static string GetGroupDisplayName(string groupName)
        {
            return string.IsNullOrWhiteSpace(groupName)
                ? LocalizationService.Get("Settings.Config.Tools.Group.Ungrouped")
                : groupName.Trim();
        }

        private void SaveSettings()
        {
            if (_settings == null)
            {
                _settings = OfficeCopilotSettingsFactory.CreateDefault();
            }

            ApplyGeneralSettings();
            ApplyMcpSettings();

            _settings.ModelProviders = _models == null
                ? new List<ModelProviderSettings>()
                : _models.Where(p => p != null)
                    .Select(OfficeCopilotSettingsFactory.NormalizeModelProvider)
                    .OrderBy(p => p.Id)
                    .ToList();

            if (_settings.DefaultModelProviderId.HasValue)
            {
                var exists = _settings.ModelProviders.Any(m => m.Id == _settings.DefaultModelProviderId.Value);
                if (!exists)
                {
                    _settings.DefaultModelProviderId = null;
                }
            }

            _settingsStore.Save(_settings);
        }

        private void BindGeneralSettings()
        {
            if (_settings == null)
            {
                return;
            }

            if (_settings.ChatDefaults == null)
            {
                _settings.ChatDefaults = new ChatDefaultsSettings();
            }

            if (_settings.Logging == null)
            {
                _settings.Logging = new LoggingSettings();
            }

            if (_settings.Logging.ModelTranscripts == null)
            {
                _settings.Logging.ModelTranscripts = new ModelTranscriptLoggingSettings();
            }

            if (_settings.Logging.Runtime == null)
            {
                _settings.Logging.Runtime = new RuntimeLoggingSettings();
            }

            if (_settings.Logging.ToolCalls == null)
            {
                _settings.Logging.ToolCalls = new ToolCallLoggingSettings();
            }

            if (_settings.Runtime == null)
            {
                _settings.Runtime = new RuntimeSettings();
            }

            if (_settings.PowerPointSlides == null)
            {
                _settings.PowerPointSlides = new PowerPointSlidesSettings();
            }

            // --- Existing controls ---
            spHttpRequestTimeOut.EditValue = Math.Max(1, _settings.ChatDefaults.RequestTimeoutSeconds);
            //ceLogChat.Checked = _settings.Logging.ModelTranscripts.IsEnabled;
            seAttachmentFileCountThreshold.EditValue = Math.Max(1, _settings.ChatDefaults.AttachmentFileCountThreshold);
            seMaxVisualPages.EditValue = Math.Max(0, Math.Min(50, _settings.ChatDefaults.MaxVisualPages));
            seAttachmentInlineCharBudget.EditValue = Math.Max(1000, Math.Min(200000, _settings.ChatDefaults.AttachmentInlineCharBudget));
            seNativeThresholdKB.EditValue = Math.Max(1, _settings.ChatDefaults.NativeThresholdKb);
            seInlineTextThresholdMB.EditValue = Math.Max(1, _settings.ChatDefaults.InlineTextThresholdMb);
            seToolFetchFileThresholdMB.EditValue = Math.Max(1, _settings.ChatDefaults.ToolFetchFileThresholdMb);
            ceAutoResizeBigImage.Checked = _settings.ChatDefaults.AutoResizeBigImage;

            //ceAutoRestoreConversation.Checked = _settings.ChatDefaults.AutoRestoreConversation;

            // --- Runtime params ---
            //ceEnableSkillPlanning.Checked = _settings.Runtime.EnableSkillPlanning;
            //seMaxToolRoundCount.EditValue = _settings.Runtime.MaxToolRoundCount;
            //cbeDefaultSkillName.EditValue = _settings.Runtime.DefaultSkillName ?? "read-analysis";
            //seContextWindowSize.EditValue = _settings.Runtime.ContextWindowSize;
            seContextWindowSize.EditValue = _settings.Runtime.ContextWindowSize;
            ceAutoCompactEnabled.Checked = _settings.Runtime.AutoCompactEnabled;
            //ceMicroCompactEnabled.Checked = _settings.Runtime.MicroCompactEnabled;
            //seMicroCompactCharThreshold.EditValue = _settings.Runtime.MicroCompactCharThreshold;

            //// --- Storage ---
            //cbeStorageMode.SelectedIndex = _settings.Runtime.StorageMode == ConversationStorageMode.Jsonl ? 1 : 0;
            //beJsonlRootDirectory.Text = _settings.Runtime.JsonlRootDirectory ?? string.Empty;

            // --- Logging ---
            // Transcript mode
            switch (_settings.Logging.ModelTranscripts.Mode)
            {
                case ModelTranscriptLogMode.MetadataOnly:
                    cbeTranscriptLogMode.SelectedIndex = 1; break;
                case ModelTranscriptLogMode.SanitizedContent:
                    cbeTranscriptLogMode.SelectedIndex = 2; break;
                case ModelTranscriptLogMode.RawContent:
                    cbeTranscriptLogMode.SelectedIndex = 3; break;
                default:
                    cbeTranscriptLogMode.SelectedIndex = 0; break;
            }
            ceRuntimeLogEnabled.Checked = _settings.Logging.Runtime.IsEnabled;
            cbeRuntimeLogLevel.SelectedIndex = MapAppLogLevelToIndex(_settings.Logging.Runtime.MinimumLevel);
            //ceEnableDebugTrace.Checked = _settings.Logging.Runtime.EnableDebugTrace;
            //ceEnableFileLog.Checked = _settings.Logging.Runtime.EnableFile;
            //ceIncludeScopes.Checked = _settings.Logging.Runtime.IncludeScopes;
            seMaxFileSizeMb.EditValue = _settings.Logging.Runtime.MaxFileSizeMb;
            seRetainedFileCount.EditValue = _settings.Logging.Runtime.RetainedFileCountLimit;
            ceTranscriptIncludeToolCalls.Checked = _settings.Logging.ModelTranscripts.IncludeToolCalls;
            ceTranscriptIncludeHostContext.Checked = _settings.Logging.ModelTranscripts.IncludeHostContext;
            //ceTranscriptMaskSensitive.Checked = _settings.Logging.ModelTranscripts.MaskSensitiveData;
            seTranscriptMaxContentLength.EditValue = _settings.Logging.ModelTranscripts.MaxContentLength;
            ceToolCallLogEnabled.Checked = _settings.Logging.ToolCalls.IsEnabled;
            _pptDefaultStyle.Text = _settings.PowerPointSlides.DefaultStyle ?? string.Empty;
            _pptDefaultDensity.SelectedItem = _settings.PowerPointSlides.DefaultDensity;
            _pptDefaultBuildMode.SelectedItem = _settings.PowerPointSlides.DefaultBuildMode;
            _pptRepairIterationCap.Value = Math.Max(_pptRepairIterationCap.Minimum, Math.Min(_pptRepairIterationCap.Maximum, _settings.PowerPointSlides.RepairIterationCap));
            _pptWorkspaceRetentionDays.Value = Math.Max(_pptWorkspaceRetentionDays.Minimum, Math.Min(_pptWorkspaceRetentionDays.Maximum, _settings.PowerPointSlides.WorkspaceRetentionDays));
            _pptEmbedFontsByDefault.Checked = _settings.PowerPointSlides.EmbedFontsByDefault;
        }

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

        private void ApplyGeneralSettings()
        {
            if (_settings == null)
            {
                return;
            }

            if (_settings.ChatDefaults == null)
            {
                _settings.ChatDefaults = new ChatDefaultsSettings();
            }

            if (_settings.Logging == null)
            {
                _settings.Logging = new LoggingSettings();
            }

            if (_settings.Logging.ModelTranscripts == null)
            {
                _settings.Logging.ModelTranscripts = new ModelTranscriptLoggingSettings();
            }

            if (_settings.Logging.Runtime == null)
            {
                _settings.Logging.Runtime = new RuntimeLoggingSettings();
            }

            if (_settings.Logging.ToolCalls == null)
            {
                _settings.Logging.ToolCalls = new ToolCallLoggingSettings();
            }

            if (_settings.Runtime == null)
            {
                _settings.Runtime = new RuntimeSettings();
            }

            if (_settings.PowerPointSlides == null)
            {
                _settings.PowerPointSlides = new PowerPointSlidesSettings();
            }

            // --- Existing ---
            _settings.ChatDefaults.RequestTimeoutSeconds = Math.Max(1, Convert.ToInt32(spHttpRequestTimeOut.Value));
            _settings.ChatDefaults.AttachmentFileCountThreshold = Math.Max(1, Convert.ToInt32(seAttachmentFileCountThreshold.Value));
            _settings.ChatDefaults.MaxVisualPages = Math.Max(0, Math.Min(50, Convert.ToInt32(seMaxVisualPages.Value)));
            _settings.ChatDefaults.AttachmentInlineCharBudget = Math.Max(1000, Math.Min(200000, Convert.ToInt32(seAttachmentInlineCharBudget.Value)));
            _settings.ChatDefaults.NativeThresholdKb = Math.Max(1, Convert.ToInt32(seNativeThresholdKB.Value));
            _settings.ChatDefaults.InlineTextThresholdMb = Math.Max(1, Convert.ToInt32(seInlineTextThresholdMB.Value));
            _settings.ChatDefaults.ToolFetchFileThresholdMb = Math.Max(1, Convert.ToInt32(seToolFetchFileThresholdMB.Value));
            _settings.ChatDefaults.AutoResizeBigImage = ceAutoResizeBigImage.Checked;
           // _settings.Logging.ModelTranscripts.IsEnabled = ceLogChat.Checked;

           // _settings.ChatDefaults.AutoRestoreConversation = ceAutoRestoreConversation.Checked;

           // _settings.Runtime.MaxToolRoundCount = Math.Max(1, Convert.ToInt32(seMaxToolRoundCount.Value));
           // _settings.Runtime.DefaultSkillName = (cbeDefaultSkillName.EditValue ?? "read-analysis").ToString();
            //_settings.Runtime.ContextWindowSize = Math.Max(1000, Convert.ToInt32(seContextWindowSize.Value));
            _settings.Runtime.ContextWindowSize = Math.Max(1000, Convert.ToInt32(seContextWindowSize.Value));
            _settings.Runtime.AutoCompactEnabled = ceAutoCompactEnabled.Checked;
           // _settings.Runtime.MicroCompactEnabled = ceMicroCompactEnabled.Checked;
           // _settings.Runtime.MicroCompactCharThreshold = Math.Max(1000, Convert.ToInt32(seMicroCompactCharThreshold.Value));

            // --- Storage ---
           // _settings.Runtime.StorageMode = cbeStorageMode.SelectedIndex == 1
           //     ? ConversationStorageMode.Jsonl
           //    : ConversationStorageMode.InMemory;
           // _settings.Runtime.JsonlRootDirectory = (beJsonlRootDirectory.Text ?? string.Empty).Trim();

            // --- Logging ---
            //// Transcript mode
            //if (ceLogChat.Checked)
            //{
            //    switch (cbeTranscriptLogMode.SelectedIndex)
            //    {
            //        case 1:
            //            _settings.Logging.ModelTranscripts.Mode = ModelTranscriptLogMode.MetadataOnly; break;
            //        case 2:
            //            _settings.Logging.ModelTranscripts.Mode = ModelTranscriptLogMode.SanitizedContent; break;
            //        case 3:
            //            _settings.Logging.ModelTranscripts.Mode = ModelTranscriptLogMode.RawContent; break;
            //        default:
            //            _settings.Logging.ModelTranscripts.Mode = ModelTranscriptLogMode.SanitizedContent; break;
            //    }
            //}
            //else
            //{
                _settings.Logging.ModelTranscripts.Mode = ModelTranscriptLogMode.Disabled;
            //}

            _settings.Logging.Runtime.IsEnabled = ceRuntimeLogEnabled.Checked;
            _settings.Logging.Runtime.MinimumLevel = MapIndexToAppLogLevel(cbeRuntimeLogLevel.SelectedIndex);
            //_settings.Logging.Runtime.EnableDebugTrace = ceEnableDebugTrace.Checked;
            //_settings.Logging.Runtime.EnableFile = ceEnableFileLog.Checked;
            //_settings.Logging.Runtime.IncludeScopes = ceIncludeScopes.Checked;
            _settings.Logging.Runtime.MaxFileSizeMb = Math.Max(1, Convert.ToInt32(seMaxFileSizeMb.Value));
            _settings.Logging.Runtime.RetainedFileCountLimit = Math.Max(1, Convert.ToInt32(seRetainedFileCount.Value));
            _settings.Logging.ModelTranscripts.IncludeToolCalls = ceTranscriptIncludeToolCalls.Checked;
            _settings.Logging.ModelTranscripts.IncludeHostContext = ceTranscriptIncludeHostContext.Checked;
            //_settings.Logging.ModelTranscripts.MaskSensitiveData = ceTranscriptMaskSensitive.Checked;
            _settings.Logging.ModelTranscripts.MaxContentLength = Math.Max(0, Convert.ToInt32(seTranscriptMaxContentLength.Value));
            _settings.Logging.ToolCalls.IsEnabled = ceToolCallLogEnabled.Checked;
            _settings.PowerPointSlides.DefaultStyle = (_pptDefaultStyle.Text ?? string.Empty).Trim();
            _settings.PowerPointSlides.DefaultDensity = (_pptDefaultDensity.SelectedItem ?? "medium").ToString();
            _settings.PowerPointSlides.DefaultBuildMode = (_pptDefaultBuildMode.SelectedItem ?? "fidelity").ToString();
            _settings.PowerPointSlides.RepairIterationCap = (int)_pptRepairIterationCap.Value;
            _settings.PowerPointSlides.WorkspaceRetentionDays = (int)_pptWorkspaceRetentionDays.Value;
            _settings.PowerPointSlides.EmbedFontsByDefault = _pptEmbedFontsByDefault.Checked;
        }

        private void InitializePowerPointSlidesSettingsControls()
        {
            _pptSlidesGroup = new GroupBox
            {
                Text = "PowerPoint Slides",
                Location = new System.Drawing.Point(34, 490),
                Size = new System.Drawing.Size(1109, 190),
                Font = new System.Drawing.Font("Microsoft YaHei UI", 9F, System.Drawing.FontStyle.Bold)
            };
            xtpGeneral.Controls.Add(_pptSlidesGroup);

            _pptDefaultStyleLabel = AddSlidesSettingLabel(_pptSlidesGroup, "Settings.Config.PowerPoint.DefaultStyle", 24, 34, 150);
            _pptDefaultStyle = new System.Windows.Forms.TextBox { Location = new System.Drawing.Point(180, 30), Size = new System.Drawing.Size(330, 24) };
            _pptSlidesGroup.Controls.Add(_pptDefaultStyle);

            _pptDefaultDensityLabel = AddSlidesSettingLabel(_pptSlidesGroup, "Settings.Config.PowerPoint.DefaultDensity", 24, 72, 150);
            _pptDefaultDensity = new System.Windows.Forms.ComboBox { Location = new System.Drawing.Point(180, 68), Size = new System.Drawing.Size(160, 24), DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList };
            _pptDefaultDensity.Items.AddRange(new object[] { "low", "medium", "high" });
            _pptSlidesGroup.Controls.Add(_pptDefaultDensity);

            _pptDefaultBuildModeLabel = AddSlidesSettingLabel(_pptSlidesGroup, "Settings.Config.PowerPoint.DefaultBuildMode", 24, 110, 150);
            _pptDefaultBuildMode = new System.Windows.Forms.ComboBox { Location = new System.Drawing.Point(180, 106), Size = new System.Drawing.Size(160, 24), DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList };
            _pptDefaultBuildMode.Items.AddRange(new object[] { "fidelity", "editable", "image" });
            _pptSlidesGroup.Controls.Add(_pptDefaultBuildMode);

            _pptRepairIterationCapLabel = AddSlidesSettingLabel(_pptSlidesGroup, "Settings.Config.PowerPoint.RepairIterationCap", 560, 34, 170);
            _pptRepairIterationCap = new System.Windows.Forms.NumericUpDown { Location = new System.Drawing.Point(740, 30), Size = new System.Drawing.Size(110, 24), Minimum = 0, Maximum = 10 };
            _pptSlidesGroup.Controls.Add(_pptRepairIterationCap);

            _pptWorkspaceRetentionDaysLabel = AddSlidesSettingLabel(_pptSlidesGroup, "Settings.Config.PowerPoint.WorkspaceRetentionDays", 560, 72, 170);
            _pptWorkspaceRetentionDays = new System.Windows.Forms.NumericUpDown { Location = new System.Drawing.Point(740, 68), Size = new System.Drawing.Size(110, 24), Minimum = 1, Maximum = 3650 };
            _pptSlidesGroup.Controls.Add(_pptWorkspaceRetentionDays);

            _pptEmbedFontsByDefault = new System.Windows.Forms.CheckBox
            {
                Text = LocalizationService.Get("Settings.Config.PowerPoint.EmbedFontsByDefault"),
                Location = new System.Drawing.Point(560, 108),
                Size = new System.Drawing.Size(200, 24)
            };
            _pptSlidesGroup.Controls.Add(_pptEmbedFontsByDefault);

            _pptSelfTestButton = new System.Windows.Forms.Button
            {
                Text = LocalizationService.Get("Settings.Config.PowerPoint.RunSelfTest"),
                Location = new System.Drawing.Point(900, 106),
                Size = new System.Drawing.Size(130, 26),
                Visible = string.Equals(_toolHostApplicationName, "PowerPoint", StringComparison.OrdinalIgnoreCase)
            };
            _pptSelfTestButton.Click += (sender, args) => RunPowerPointToolSelfTest();
            _pptSlidesGroup.Controls.Add(_pptSelfTestButton);

            _pptDefaultStyle.Leave += GeneralSetting_Changed;
            _pptDefaultDensity.SelectedIndexChanged += GeneralSetting_Changed;
            _pptDefaultBuildMode.SelectedIndexChanged += GeneralSetting_Changed;
            _pptRepairIterationCap.ValueChanged += GeneralSetting_Changed;
            _pptWorkspaceRetentionDays.ValueChanged += GeneralSetting_Changed;
            _pptEmbedFontsByDefault.CheckedChanged += GeneralSetting_Changed;
        }

        private static System.Windows.Forms.Label AddSlidesSettingLabel(Control parent, string key, int x, int y, int width)
        {
            var label = new System.Windows.Forms.Label
            {
                Text = LocalizationService.Get(key),
                Location = new System.Drawing.Point(x, y + 3),
                Size = new System.Drawing.Size(width, 20),
                Font = new System.Drawing.Font("Microsoft YaHei UI", 9F)
            };
            parent.Controls.Add(label);
            return label;
        }

        private void RunPowerPointToolSelfTest()
        {
            if (_toolRegistry == null || _toolServices == null)
            {
                MessageBox.Show(this, LocalizationService.Get("Settings.Config.PowerPoint.SelfTestUnavailable"),
                    LocalizationService.Get("Settings.Config.DialogTitle.PowerPointSelfTest"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            try
            {
                var results = PowerPointToolSelfTestRunner.Run(_toolRegistry, _toolServices);
                MessageBox.Show(this, BuildPowerPointToolSelfTestReport(results),
                    LocalizationService.Get("Settings.Config.DialogTitle.PowerPointSelfTest"), MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, LocalizationService.Format("Settings.Config.Error.PowerPointSelfTest", ex.Message),
                    LocalizationService.Get("Settings.Config.DialogTitle.PowerPointSelfTest"), MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private static string BuildPowerPointToolSelfTestReport(IReadOnlyList<PowerPointToolSelfTestGroupResult> results)
        {
            if (results == null || results.Count == 0)
            {
                return LocalizationService.Get("Settings.Config.PowerPoint.SelfTestNoResults");
            }

            var success = 0;
            var failed = 0;
            var skipped = 0;
            var lines = new List<string>
            {
                LocalizationService.Get("Settings.Config.PowerPoint.SelfTestReportTitle"),
                LocalizationService.Format(
                    "Settings.Config.PowerPoint.SelfTestGenerated",
                    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")),
                string.Empty
            };

            foreach (var group in results)
            {
                lines.Add(LocalizationService.Format(
                    "Settings.Config.PowerPoint.SelfTestGroup",
                    GetGroupDisplayName(group.GroupName)));
                lines.Add(LocalizationService.Format(
                    "Settings.Config.PowerPoint.SelfTestRegistered",
                    group.RegisteredToolCount));

                foreach (var item in group.Items)
                {
                    var status = item.Status == PowerPointToolSelfTestStatus.Success
                        ? LocalizationService.Get("Settings.Config.PowerPoint.SelfTestStatus.Passed")
                        : item.Status == PowerPointToolSelfTestStatus.Failed
                            ? LocalizationService.Get("Settings.Config.PowerPoint.SelfTestStatus.Failed")
                            : LocalizationService.Get("Settings.Config.PowerPoint.SelfTestStatus.Skipped");
                    if (item.Status == PowerPointToolSelfTestStatus.Success)
                    {
                        success++;
                    }
                    else if (item.Status == PowerPointToolSelfTestStatus.Failed)
                    {
                        failed++;
                    }
                    else
                    {
                        skipped++;
                    }

                    lines.Add(LocalizationService.Format(
                        "Settings.Config.PowerPoint.SelfTestItem",
                        status,
                        item.DisplayName,
                        item.ToolName,
                        item.DurationMilliseconds,
                        item.Message));
                }

                lines.Add(string.Empty);
            }

            lines.Insert(2, LocalizationService.Format(
                "Settings.Config.PowerPoint.SelfTestSummary",
                success,
                failed,
                skipped));
            return string.Join(Environment.NewLine, lines);
        }

        private void SaveGeneralSettings()
        {
            if (_loadingSettings)
            {
                return;
            }

            try
            {
                SaveSettings();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, LocalizationService.Format("Settings.Config.Error.SaveGeneralSettings", ex.Message),
                    LocalizationService.Get("Settings.Config.DialogTitle.Settings"), MessageBoxButtons.OK, MessageBoxIcon.Error);
                LoadSettings();
            }
        }

        private void spHttpRequestTimeOut_EditValueChanged(object sender, EventArgs e)
        {
            SaveGeneralSettings();
        }

        private void ceLogChat_CheckedChanged(object sender, EventArgs e)
        {
            SaveGeneralSettings();
        }

        private void GeneralThreshold_EditValueChanged(object sender, EventArgs e)
        {
            SaveGeneralSettings();
        }

        private void ceAutoResizeBigImage_CheckedChanged(object sender, EventArgs e)
        {
            SaveGeneralSettings();
        }

        /// <summary>
        /// 通用事件处理：所有动态添加的系统设置控件变更时触发保存。
        /// </summary>
        private void GeneralSetting_Changed(object sender, EventArgs e)
        {
            SaveGeneralSettings();
        }

        private void btnSaveGenenalConfig_ItemClick(object sender, ItemClickEventArgs e)
        {
            try
            {
                CommitPendingEditors();
                ApplyToolSettings();
                SaveSettings();
                RebuildToolRegistry();
                _skipSaveOnClose = true;
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, LocalizationService.Format("Settings.Config.Error.SaveSettings", ex.Message),
                    LocalizationService.Get("Settings.Config.DialogTitle.Settings"), MessageBoxButtons.OK, MessageBoxIcon.Error);
                LoadSettings();
            }
        }

        private void btnCancelSaveGeneralConfig_ItemClick(object sender, ItemClickEventArgs e)
        {
            _skipSaveOnClose = true;
            Close();
        }

        private int GetNextModelId()
        {
            if (_models == null || _models.Count == 0)
            {
                return 1;
            }

            return _models.Max(m => m.Id) + 1;
        }

        private ModelProviderSettings GetFocusedModel()
        {
            if (gvModels.FocusedRowHandle < 0)
            {
                return null;
            }

            return gvModels.GetRow(gvModels.FocusedRowHandle) as ModelProviderSettings;
        }

        private void btnAddModel_ItemClick(object sender, ItemClickEventArgs e)
        {
            using (var form = new fmModelEditor())
            {
                if (form.ShowDialog(this) != DialogResult.OK || form.Model == null)
                {
                    return;
                }

                if (form.Model.Id <= 0)
                {
                    form.Model.Id = GetNextModelId();
                }

                var list = _models == null ? new List<ModelProviderSettings>() : _models.ToList();
                list.Add(form.Model);

                RebindModels(list);
                SaveSettings();
                gvModels.RefreshData();
                //gvModels.BestFitColumns();
            }
        }

        private void gvModels_DoubleClick(object sender, EventArgs e)
        {
            if (!IsDoubleClickOnRowCell(sender)) return;
            EditFocusedModel();
        }

        private void EditFocusedModel()
        {
            var current = GetFocusedModel();
            if (current == null)
            {
                return;
            }

            var list = _models.ToList();
            var index = list.FindIndex(m => m != null && m.Id == current.Id);
            if (index < 0)
            {
                return;
            }

            using (var form = new fmModelEditor(current))
            {
                if (form.ShowDialog(this) != DialogResult.OK || form.Model == null)
                {
                    return;
                }

                if (form.Model.Id <= 0)
                {
                    form.Model.Id = current.Id;
                }

                list[index] = form.Model;

                RebindModels(list);
                SaveSettings();
                gvModels.RefreshData();
                //gvModels.BestFitColumns();
            }
        }

        private void btnDeleteModel_ItemClick(object sender, ItemClickEventArgs e)
        {
            var current = GetFocusedModel();
            if (current == null)
            {
                return;
            }

            var result = MessageBox.Show(
                this,
                LocalizationService.Format("Settings.Config.Models.ConfirmDelete", current.Name),
                LocalizationService.Get("Settings.Config.DialogTitle.Model"),
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);

            if (result != DialogResult.Yes)
            {
                return;
            }

            var list = _models.ToList();
            list.RemoveAll(m => m != null && m.Id == current.Id);

            if (_settings != null &&
                _settings.DefaultModelProviderId.HasValue &&
                _settings.DefaultModelProviderId.Value == current.Id)
            {
                _settings.DefaultModelProviderId = null;
            }

            RebindModels(list);
            SaveSettings();
            gvModels.RefreshData();
        }

        private void btnExportListToJson_ItemClick(object sender, ItemClickEventArgs e)
        {
            using (var dialog = new SaveFileDialog())
            {
                dialog.Title = LocalizationService.Get("Settings.Config.Models.ExportDialogTitle");
                dialog.Filter = LocalizationService.Get("Settings.Config.FileFilter.Json");
                dialog.FileName = "model-providers.json";

                if (dialog.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }

                try
                {
                    var json = JsonConvert.SerializeObject(_models.OrderBy(m => m.Id).ToList(), Formatting.Indented);
                    File.WriteAllText(dialog.FileName, json, System.Text.Encoding.UTF8);
                    MessageBox.Show(this, LocalizationService.Get("Settings.Config.Common.ExportComplete"),
                        LocalizationService.Get("Settings.Config.DialogTitle.Model"), MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, LocalizationService.Format("Settings.Config.Common.ExportFailed", ex.Message),
                        LocalizationService.Get("Settings.Config.DialogTitle.Model"), MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        private void btnImportList_ItemClick(object sender, ItemClickEventArgs e)
        {
            using (var dialog = new OpenFileDialog())
            {
                dialog.Title = LocalizationService.Get("Settings.Config.Models.ImportDialogTitle");
                dialog.Filter = LocalizationService.Get("Settings.Config.FileFilter.Json");

                if (dialog.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }

                var confirm = MessageBox.Show(
                    this,
                    LocalizationService.Get("Settings.Config.Models.ConfirmImport"),
                    LocalizationService.Get("Settings.Config.DialogTitle.Model"),
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question);

                if (confirm != DialogResult.Yes)
                {
                    return;
                }

                try
                {
                    var imported = ReadModelsFromFile(dialog.FileName);

                    if (_settings == null)
                    {
                        _settings = OfficeCopilotSettingsFactory.CreateDefault();
                    }

                    _settings.ModelProviders = imported;

                    if (_settings.DefaultModelProviderId.HasValue)
                    {
                        var exists = imported.Any(m => m.Id == _settings.DefaultModelProviderId.Value);
                        if (!exists)
                        {
                            _settings.DefaultModelProviderId = null;
                        }
                    }

                    RebindModels(imported);
                    SaveSettings();
                    gvModels.RefreshData();
                    //gvModels.BestFitColumns();

                    MessageBox.Show(this, LocalizationService.Get("Settings.Config.Common.ImportComplete"),
                        LocalizationService.Get("Settings.Config.DialogTitle.Model"), MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, LocalizationService.Format("Settings.Config.Common.ImportFailed", ex.Message),
                        LocalizationService.Get("Settings.Config.DialogTitle.Model"), MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        private static List<ModelProviderSettings> ReadModelsFromFile(string filePath)
        {
            var json = File.ReadAllText(filePath, System.Text.Encoding.UTF8);
            if (string.IsNullOrWhiteSpace(json))
            {
                return new List<ModelProviderSettings>();
            }

            var token = JToken.Parse(json);
            List<ModelProviderSettings> models = null;

            if (token.Type == JTokenType.Array)
            {
                var array = token as JArray;
                MigrateModelProviderIds(array);
                models = array == null ? new List<ModelProviderSettings>() : array.ToObject<List<ModelProviderSettings>>();
            }
            else if (token.Type == JTokenType.Object)
            {
                var root = token as JObject;
                var providers = root == null ? null : root["ModelProviders"] as JArray;
                if (providers != null)
                {
                    MigrateModelProviderIds(providers);
                    models = providers.ToObject<List<ModelProviderSettings>>();
                }
            }

            models = models ?? new List<ModelProviderSettings>();
            NormalizeImportedModels(models);
            return models.OrderBy(m => m.Id).ToList();
        }

        private static void MigrateModelProviderIds(JArray providers)
        {
            if (providers == null || providers.Count == 0)
            {
                return;
            }

            var maxId = 0;
            foreach (var item in providers.OfType<JObject>())
            {
                var id = TryReadPositiveInt(item["Id"]);
                if (id > maxId)
                {
                    maxId = id;
                }
            }

            var nextId = maxId <= 0 ? 1 : maxId + 1;
            var usedIds = new HashSet<int>();

            foreach (var item in providers.OfType<JObject>())
            {
                var currentId = TryReadPositiveInt(item["Id"]);
                if (currentId <= 0 || usedIds.Contains(currentId))
                {
                    currentId = nextId++;
                }

                item["Id"] = currentId;
                usedIds.Add(currentId);
            }
        }

        private static void NormalizeImportedModels(List<ModelProviderSettings> models)
        {
            if (models == null)
            {
                return;
            }

            var maxId = models
                .Where(m => m != null && m.Id > 0)
                .Select(m => m.Id)
                .DefaultIfEmpty(0)
                .Max();

            var nextId = maxId <= 0 ? 1 : maxId + 1;
            var usedIds = new HashSet<int>();

            foreach (var model in models.Where(m => m != null))
            {
                if (model.Id <= 0 || usedIds.Contains(model.Id))
                {
                    model.Id = nextId++;
                }

                usedIds.Add(model.Id);

                if (model.SystemPrompt == null)
                {
                    model.SystemPrompt = string.Empty;
                }

                OfficeCopilotSettingsFactory.NormalizeModelProvider(model);
            }
        }

        private static int TryReadPositiveInt(JToken token)
        {
            if (token == null || token.Type == JTokenType.Null)
            {
                return 0;
            }

            if (token.Type == JTokenType.Integer)
            {
                var value = token.Value<int>();
                return value > 0 ? value : 0;
            }

            int parsed;
            if (int.TryParse(token.ToString(), out parsed) && parsed > 0)
            {
                return parsed;
            }

            return 0;
        }

        private void btnNewMcp_ItemClick(object sender, ItemClickEventArgs e)
        {
            using (var form = new fmMcpEditor())
            {
                if (form.ShowDialog(this) != DialogResult.OK || form.McpServer == null)
                {
                    return;
                }

                EnsureSettings();
                UpsertMcpServer(form.McpServer);
                _settings.UpdatedAtUtc = DateTime.UtcNow;
                _settingsStore.Save(_settings);
                LoadMcpSettings();
            }
        }

        private void btnEditMcp_ItemClick(object sender, ItemClickEventArgs e)
        {
            EditFocusedMcpServer();
        }

        private void gvMcpServer_DoubleClick(object sender, EventArgs e)
        {
            if (!IsDoubleClickOnRowCell(sender)) return;
            EditFocusedMcpServer();
        }

        private void EditFocusedMcpServer()
        {
            var server = GetFocusedMcpServer();
            if (server == null)
            {
                return;
            }

            using (var form = new fmMcpEditor(FindMcpServer(server.ServerId)))
            {
                if (form.ShowDialog(this) != DialogResult.OK || form.McpServer == null)
                {
                    return;
                }

                EnsureSettings();
                UpsertMcpServer(form.McpServer);
                _settings.UpdatedAtUtc = DateTime.UtcNow;
                _settingsStore.Save(_settings);
                LoadMcpSettings();
            }
        }

        private void btnDelMcp_ItemClick(object sender, ItemClickEventArgs e)
        {
            var server = GetFocusedMcpServer();
            if (server == null)
            {
                return;
            }

            var result = MessageBox.Show(
                this,
                LocalizationService.Format("Settings.Config.Mcp.ConfirmDelete", server.DisplayName),
                LocalizationService.Get("Settings.Config.DialogTitle.Mcp"),
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);

            if (result != DialogResult.Yes)
            {
                return;
            }

            EnsureSettings();
            _settings.McpServers.RemoveAll(p => p != null && string.Equals(p.Id, server.ServerId, StringComparison.OrdinalIgnoreCase));
            _settings.UpdatedAtUtc = DateTime.UtcNow;
            _settingsStore.Save(_settings);
            LoadMcpSettings();
        }

        private void btnExportMcp_ItemClick(object sender, ItemClickEventArgs e)
        {
            using (var dialog = new SaveFileDialog())
            {
                dialog.Title = LocalizationService.Get("Settings.Config.Mcp.ExportDialogTitle");
                dialog.Filter = LocalizationService.Get("Settings.Config.FileFilter.Json");
                dialog.FileName = "mcp-servers.json";

                if (dialog.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }

                try
                {
                    EnsureSettings();
                    ApplyMcpSettings();
                    File.WriteAllText(dialog.FileName, JsonConvert.SerializeObject(_settings.McpServers, Formatting.Indented), System.Text.Encoding.UTF8);
                    MessageBox.Show(this, LocalizationService.Get("Settings.Config.Common.ExportComplete"),
                        LocalizationService.Get("Settings.Config.DialogTitle.Mcp"), MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, LocalizationService.Format("Settings.Config.Common.ExportFailed", ex.Message),
                        LocalizationService.Get("Settings.Config.DialogTitle.Mcp"), MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        private void btnImportMcp_ItemClick(object sender, ItemClickEventArgs e)
        {
            using (var dialog = new OpenFileDialog())
            {
                dialog.Title = LocalizationService.Get("Settings.Config.Mcp.ImportDialogTitle");
                dialog.Filter = LocalizationService.Get("Settings.Config.FileFilter.Json");

                if (dialog.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }

                var confirm = MessageBox.Show(
                    this,
                    LocalizationService.Get("Settings.Config.Mcp.ConfirmImport"),
                    LocalizationService.Get("Settings.Config.DialogTitle.Mcp"),
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question);

                if (confirm != DialogResult.Yes)
                {
                    return;
                }

                try
                {
                    EnsureSettings();
                    _settings.McpServers = ReadMcpServersFromFile(dialog.FileName);
                    SaveMcpSettings();
                    LoadMcpSettings();
                    MessageBox.Show(this, LocalizationService.Get("Settings.Config.Common.ImportComplete"),
                        LocalizationService.Get("Settings.Config.DialogTitle.Mcp"), MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, LocalizationService.Format("Settings.Config.Common.ImportFailed", ex.Message),
                        LocalizationService.Get("Settings.Config.DialogTitle.Mcp"), MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        private void EnsureSettings()
        {
            if (_settings == null)
            {
                _settings = OfficeCopilotSettingsFactory.CreateDefault();
            }

            if (_settings.McpServers == null)
            {
                _settings.McpServers = new List<McpServerSettings>();
            }
        }

        private void UpsertMcpServer(McpServerSettings server)
        {
            if (server == null)
            {
                return;
            }

            EnsureSettings();
            var index = _settings.McpServers.FindIndex(p => p != null && string.Equals(p.Id, server.Id, StringComparison.OrdinalIgnoreCase));
            if (index < 0)
            {
                _settings.McpServers.Add(OfficeCopilotSettingsFactory.NormalizeMcpServer(server));
                return;
            }

            _settings.McpServers[index] = OfficeCopilotSettingsFactory.NormalizeMcpServer(server);
        }

        private static List<McpServerSettings> ReadMcpServersFromFile(string filePath)
        {
            var json = File.ReadAllText(filePath, System.Text.Encoding.UTF8);
            if (string.IsNullOrWhiteSpace(json))
            {
                return new List<McpServerSettings>();
            }

            var token = JToken.Parse(json);
            List<McpServerSettings> servers = null;

            if (token.Type == JTokenType.Array)
            {
                servers = token.ToObject<List<McpServerSettings>>();
            }
            else if (token.Type == JTokenType.Object)
            {
                var root = token as JObject;
                var mcpServers = root == null ? null : root["McpServers"];
                if (mcpServers != null)
                {
                    servers = mcpServers.ToObject<List<McpServerSettings>>();
                }
            }

            return (servers ?? new List<McpServerSettings>())
                .Select(OfficeCopilotSettingsFactory.NormalizeMcpServer)
                .ToList();
        }

        private void btnShowPreview_ItemClick(object sender, ItemClickEventArgs e)
        {
            gvModels.OptionsView.ShowPreview = !gvModels.OptionsView.ShowPreview;
            RefreshPreviewButtonCaption();
        }

        private void RefreshPreviewButtonCaption()
        {
            btnShowPreview.Caption = LocalizationService.Get(
                gvModels.OptionsView.ShowPreview
                    ? "Settings.Config.Models.HidePromptPreview"
                    : "Settings.Config.Models.ShowPromptPreview");
        }

        private void gvModels_CustomColumnDisplayText(object sender, CustomColumnDisplayTextEventArgs e)
        {
            if (e == null || e.Column == null)
            {
                return;
            }

            var fieldName = e.Column.FieldName;
            if (string.IsNullOrWhiteSpace(fieldName))
            {
                return;
            }

            if (fieldName == "IsEnabled" ||
                fieldName == "SupportsImageInput" ||
                fieldName == "SupportsFunctionCalling" ||
                fieldName == "SupportsDeepThinking")
            {
                bool boolValue;
                if (TryConvertToBoolean(e.Value, out boolValue))
                {
                    e.DisplayText = LocalizationService.Get(
                        boolValue ? "Settings.Config.Value.Yes" : "Settings.Config.Value.No");
                }

                return;
            }

            if (fieldName == "ProviderKind")
            {
                ModelProviderKind providerKind;
                if (TryConvertToProviderKind(e.Value, out providerKind))
                {
                    e.DisplayText = GetProviderKindDisplayText(providerKind);
                }
            }
        }

        private static bool TryConvertToBoolean(object value, out bool result)
        {
            if (value is bool)
            {
                result = (bool)value;
                return true;
            }

            return bool.TryParse(Convert.ToString(value), out result);
        }

        private static bool TryConvertToProviderKind(object value, out ModelProviderKind result)
        {
            if (value is ModelProviderKind)
            {
                result = (ModelProviderKind)value;
                return true;
            }

            return Enum.TryParse(Convert.ToString(value), true, out result);
        }

        private static string GetProviderKindDisplayText(ModelProviderKind kind)
        {
            switch (kind)
            {
                case ModelProviderKind.OpenAI:
                    return "OpenAI";
                case ModelProviderKind.AzureOpenAI:
                    return "Azure OpenAI";
                case ModelProviderKind.Ollama:
                    return "Ollama";
                case ModelProviderKind.LMStudio:
                    return "LM Studio";
                case ModelProviderKind.Anthropic:
                    return "Anthropic";
                case ModelProviderKind.DeepSeek:
                    return "DeepSeek";
                default:
                    return "Custom";
            }
        }

        private static McpServerSettings CloneMcpServer(McpServerSettings source)
        {
            source = OfficeCopilotSettingsFactory.NormalizeMcpServer(source);
            return new McpServerSettings
            {
                Id = source.Id,
                Name = source.Name,
                Description = source.Description,
                IsEnabled = source.IsEnabled,
                Stdio = source.Stdio == null ? null : new StdioMcpServerSettings
                {
                    Command = source.Stdio.Command,
                    WorkingDirectory = source.Stdio.WorkingDirectory,
                    Arguments = source.Stdio.Arguments == null ? new List<string>() : new List<string>(source.Stdio.Arguments),
                    EnvironmentVariables = source.Stdio.EnvironmentVariables == null
                        ? new List<NameValueItem>()
                        : source.Stdio.EnvironmentVariables.Select(CloneNameValueItem).ToList()
                },
                Http = source.Http == null ? null : new HttpMcpServerSettings
                {
                    Endpoint = source.Http.Endpoint,
                    TransportMode = source.Http.TransportMode,
                    Headers = source.Http.Headers == null
                        ? new List<NameValueItem>()
                        : source.Http.Headers.Select(CloneNameValueItem).ToList()
                },
                Tools = source.Tools == null
                    ? new List<McpToolSettings>()
                    : source.Tools.Select(CloneMcpTool).ToList()
            };
        }

        private static NameValueItem CloneNameValueItem(NameValueItem item)
        {
            return item == null
                ? new NameValueItem()
                : new NameValueItem
                {
                    Name = item.Name,
                    Value = item.Value
                };
        }

        private static McpToolSettings CloneMcpTool(McpToolSettings tool)
        {
            return tool == null
                ? new McpToolSettings()
                : new McpToolSettings
                {
                    Name = tool.Name,
                    DisplayName = tool.DisplayName,
                    Description = tool.Description,
                    SourceName = tool.SourceName,
                    InputSchemaJson = tool.InputSchemaJson,
                    OutputSchemaJson = tool.OutputSchemaJson,
                    IsReadOnly = tool.IsReadOnly,
                    IsEnabled = tool.IsEnabled,
                    Metadata = tool.Metadata == null
                        ? new List<NameValueItem>()
                        : tool.Metadata.Select(CloneNameValueItem).ToList()
                };
        }

        private sealed class ToolGroupRow
        {
            public ToolGroupRow()
            {
                GroupName = string.Empty;
                DisplayName = string.Empty;
                IsRegistered = true;
            }

            public string GroupName { get; set; }

            public string DisplayName { get; set; }

            public bool IsRegistered { get; set; }
        }

        private sealed class ToolRow
        {
            public ToolRow()
            {
                GroupName = string.Empty;
                ToolName = string.Empty;
                DisplayName = string.Empty;
                Description = string.Empty;
                IsRegistered = true;
                IsAutomaticAuthorization = false;
            }

            public string GroupName { get; set; }

            public string ToolName { get; set; }

            public string DisplayName { get; set; }

            public string Description { get; set; }

            public bool IsRegistered { get; set; }

            public bool IsAutomaticAuthorization { get; set; }

            public string TestActionText
            {
                get { return LocalizationService.Get("Settings.Config.Tools.Details"); }
            }
        }

        private sealed class McpServerRow
        {
            public McpServerRow()
            {
                ServerId = string.Empty;
                DisplayName = string.Empty;
                Description = string.Empty;
                IsEnabled = true;
                ServerType = McpServerType.Stdio;
            }

            public string ServerId { get; set; }

            public string DisplayName { get; set; }

            public string Description { get; set; }

            public bool IsEnabled { get; set; }

            public McpServerType ServerType { get; set; }
        }

        private sealed class SkillRow
        {
            public SkillRow()
            {
                Id = string.Empty;
                Name = string.Empty;
                Description = string.Empty;
                Category = string.Empty;
                SourceKind = "文件系统";
                FilePath = string.Empty;
                IsEnabled = true;
                IsReadOnly = false;
            }

            public string Id { get; set; }

            public string Name { get; set; }

            public string Description { get; set; }

            public string Category { get; set; }

            /// <summary>"内置" / "文件系统" / "内置+文件"</summary>
            public string SourceKind { get; set; }

            public string FilePath { get; set; }

            public bool IsEnabled { get; set; }

            /// <summary>内置只读技能不可编辑/删除。</summary>
            public bool IsReadOnly { get; set; }
        }
    }
}
