namespace OfficeCopilot.Settings.WinForms
{
    partial class fmConfig
    {
        /// <summary>
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        /// Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        /// <summary>
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(fmConfig));
            this.xtcMain = new DevExpress.XtraTab.XtraTabControl();
            this.xtpModels = new DevExpress.XtraTab.XtraTabPage();
            this.gcModels = new DevExpress.XtraGrid.GridControl();
            this.gvModels = new DevExpress.XtraGrid.Views.Grid.GridView();
            this.colId = new DevExpress.XtraGrid.Columns.GridColumn();
            this.colName = new DevExpress.XtraGrid.Columns.GridColumn();
            this.colProviderKind = new DevExpress.XtraGrid.Columns.GridColumn();
            this.colEndpoint = new DevExpress.XtraGrid.Columns.GridColumn();
            this.colApiKey = new DevExpress.XtraGrid.Columns.GridColumn();
            this.colModelId = new DevExpress.XtraGrid.Columns.GridColumn();
            this.colSupportsImageInput = new DevExpress.XtraGrid.Columns.GridColumn();
            this.colIsEnabled = new DevExpress.XtraGrid.Columns.GridColumn();
            this.barMan = new DevExpress.XtraBars.BarManager();
            this.barModelTop = new DevExpress.XtraBars.Bar();
            this.btnAddModel = new DevExpress.XtraBars.BarButtonItem();
            this.btnEditModel = new DevExpress.XtraBars.BarButtonItem();
            this.btnDeleteModel = new DevExpress.XtraBars.BarButtonItem();
            this.btnExportListToJson = new DevExpress.XtraBars.BarButtonItem();
            this.btnImportList = new DevExpress.XtraBars.BarButtonItem();
            this.btnShowPreview = new DevExpress.XtraBars.BarButtonItem();
            this.sbdcModelTop = new DevExpress.XtraBars.StandaloneBarDockControl();
            this.barMcpTop = new DevExpress.XtraBars.Bar();
            this.btnNewMcp = new DevExpress.XtraBars.BarButtonItem();
            this.btnEditMcp = new DevExpress.XtraBars.BarButtonItem();
            this.btnDelMcp = new DevExpress.XtraBars.BarButtonItem();
            this.btnExportMcp = new DevExpress.XtraBars.BarButtonItem();
            this.btnImportMcp = new DevExpress.XtraBars.BarButtonItem();
            this.sbdcMcpTop = new DevExpress.XtraBars.StandaloneBarDockControl();
            this.barSkillTop = new DevExpress.XtraBars.Bar();
            this.btnNewSkill = new DevExpress.XtraBars.BarButtonItem();
            this.btnEditSkill = new DevExpress.XtraBars.BarButtonItem();
            this.btnDeleteSkill = new DevExpress.XtraBars.BarButtonItem();
            this.btnExportSkills = new DevExpress.XtraBars.BarButtonItem();
            this.btnImportSkills = new DevExpress.XtraBars.BarButtonItem();
            this.sbdcSkillTop = new DevExpress.XtraBars.StandaloneBarDockControl();
            this.barMemBottom = new DevExpress.XtraBars.Bar();
            this.btnSaveMem = new DevExpress.XtraBars.BarButtonItem();
            this.bsiMemInfo = new DevExpress.XtraBars.BarStaticItem();
            this.sbdcMemBottom = new DevExpress.XtraBars.StandaloneBarDockControl();
            this.barGeneralBottom = new DevExpress.XtraBars.Bar();
            this.btnSaveGenenalConfig = new DevExpress.XtraBars.BarButtonItem();
            this.btnCancelSaveGeneralConfig = new DevExpress.XtraBars.BarButtonItem();
            this.sbdcSysConfigBottom = new DevExpress.XtraBars.StandaloneBarDockControl();
            this.barDockControlTop = new DevExpress.XtraBars.BarDockControl();
            this.barDockControlBottom = new DevExpress.XtraBars.BarDockControl();
            this.barDockControlLeft = new DevExpress.XtraBars.BarDockControl();
            this.barDockControlRight = new DevExpress.XtraBars.BarDockControl();
            this.btnShowDesc = new DevExpress.XtraBars.BarButtonItem();
            this.xtpTools = new DevExpress.XtraTab.XtraTabPage();
            this.spcTools = new System.Windows.Forms.SplitContainer();
            this.gcToolsGroup = new DevExpress.XtraGrid.GridControl();
            this.gvToolsGroup = new DevExpress.XtraGrid.Views.Grid.GridView();
            this.colToolsGroupName = new DevExpress.XtraGrid.Columns.GridColumn();
            this.colToolsGroupEnable = new DevExpress.XtraGrid.Columns.GridColumn();
            this.gcTools = new DevExpress.XtraGrid.GridControl();
            this.gvTools = new DevExpress.XtraGrid.Views.Grid.GridView();
            this.colToolName = new DevExpress.XtraGrid.Columns.GridColumn();
            this.colDescription = new DevExpress.XtraGrid.Columns.GridColumn();
            this.colEnableTool = new DevExpress.XtraGrid.Columns.GridColumn();
            this.colNeedConfirm = new DevExpress.XtraGrid.Columns.GridColumn();
            this.colTestTool = new DevExpress.XtraGrid.Columns.GridColumn();
            this.xtpMcp = new DevExpress.XtraTab.XtraTabPage();
            this.gcMcpServer = new DevExpress.XtraGrid.GridControl();
            this.gvMcpServer = new DevExpress.XtraGrid.Views.Grid.GridView();
            this.colMcpId = new DevExpress.XtraGrid.Columns.GridColumn();
            this.colMcpName = new DevExpress.XtraGrid.Columns.GridColumn();
            this.colMcpDesc = new DevExpress.XtraGrid.Columns.GridColumn();
            this.colEnableMcp = new DevExpress.XtraGrid.Columns.GridColumn();
            this.xtpSkills = new DevExpress.XtraTab.XtraTabPage();
            this.gcSkills = new DevExpress.XtraGrid.GridControl();
            this.gvSkills = new DevExpress.XtraGrid.Views.Grid.GridView();
            this.colSkillId = new DevExpress.XtraGrid.Columns.GridColumn();
            this.colSkillName = new DevExpress.XtraGrid.Columns.GridColumn();
            this.colSkillDesc = new DevExpress.XtraGrid.Columns.GridColumn();
            this.colSkillEnabled = new DevExpress.XtraGrid.Columns.GridColumn();
            this.xtpMemoryMan = new DevExpress.XtraTab.XtraTabPage();
            this.spcMemoryMan = new System.Windows.Forms.SplitContainer();
            this.tlMemFileTree = new DevExpress.XtraTreeList.TreeList();
            this.xtcMemMarkDown = new DevExpress.XtraTab.XtraTabControl();
            this.xtpMemWebPreview = new DevExpress.XtraTab.XtraTabPage();
            this.webPreview = new System.Windows.Forms.WebBrowser();
            this.xtpMemMarkDown = new DevExpress.XtraTab.XtraTabPage();
            this.txtMarkDown = new DevExpress.XtraRichEdit.RichEditControl();
            this.xtpGeneral = new DevExpress.XtraTab.XtraTabPage();
            this.grpLogging = new System.Windows.Forms.GroupBox();
            this.seTranscriptMaxContentLength = new DevExpress.XtraEditors.SpinEdit();
            this.lbTranscriptMaxContentLength = new DevExpress.XtraEditors.LabelControl();
            this.ceToolCallLogEnabled = new DevExpress.XtraEditors.CheckEdit();
            this.ceTranscriptIncludeHostContext = new DevExpress.XtraEditors.CheckEdit();
            this.ceTranscriptIncludeToolCalls = new DevExpress.XtraEditors.CheckEdit();
            this.seRetainedFileCount = new DevExpress.XtraEditors.SpinEdit();
            this.lbRetainedFileCount = new DevExpress.XtraEditors.LabelControl();
            this.seMaxFileSizeMb = new DevExpress.XtraEditors.SpinEdit();
            this.lbMaxFileSizeMb = new DevExpress.XtraEditors.LabelControl();
            this.cbeRuntimeLogLevel = new DevExpress.XtraEditors.ComboBoxEdit();
            this.lbRuntimeLogLevel = new DevExpress.XtraEditors.LabelControl();
            this.ceRuntimeLogEnabled = new DevExpress.XtraEditors.CheckEdit();
            this.cbeTranscriptLogMode = new DevExpress.XtraEditors.ComboBoxEdit();
            this.lbTranscriptLogMode = new DevExpress.XtraEditors.LabelControl();
            this.grpRuntime = new System.Windows.Forms.GroupBox();
            this.ceAutoMemoryRecorder = new DevExpress.XtraEditors.CheckEdit();
            this.ceAutoCompactEnabled = new DevExpress.XtraEditors.CheckEdit();
            this.grpModelParams = new System.Windows.Forms.GroupBox();
            this.lbHttpRequestTimeOut = new DevExpress.XtraEditors.LabelControl();
            this.lbNativeThresholdKB = new DevExpress.XtraEditors.LabelControl();
            this.lbInlineTextThresholdMB = new DevExpress.XtraEditors.LabelControl();
            this.lbAttachmentInlineCharBudget = new DevExpress.XtraEditors.LabelControl();
            this.lbMaxVisualPages = new DevExpress.XtraEditors.LabelControl();
            this.seAttachmentFileCountThreshold = new DevExpress.XtraEditors.SpinEdit();
            this.ceAutoResizeBigImage = new DevExpress.XtraEditors.CheckEdit();
            this.lbToolFetchFileThresholdMB = new DevExpress.XtraEditors.LabelControl();
            this.seToolFetchFileThresholdMB = new DevExpress.XtraEditors.SpinEdit();
            this.lbAttachmentFileCountThreshold = new DevExpress.XtraEditors.LabelControl();
            this.seInlineTextThresholdMB = new DevExpress.XtraEditors.SpinEdit();
            this.seAttachmentInlineCharBudget = new DevExpress.XtraEditors.SpinEdit();
            this.seMaxVisualPages = new DevExpress.XtraEditors.SpinEdit();
            this.spHttpRequestTimeOut = new DevExpress.XtraEditors.SpinEdit();
            this.seNativeThresholdKB = new DevExpress.XtraEditors.SpinEdit();
            ((System.ComponentModel.ISupportInitialize)(this.xtcMain)).BeginInit();
            this.xtcMain.SuspendLayout();
            this.xtpModels.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.gcModels)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.gvModels)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.barMan)).BeginInit();
            this.xtpTools.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.spcTools)).BeginInit();
            this.spcTools.Panel1.SuspendLayout();
            this.spcTools.Panel2.SuspendLayout();
            this.spcTools.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.gcToolsGroup)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.gvToolsGroup)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.gcTools)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.gvTools)).BeginInit();
            this.xtpMcp.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.gcMcpServer)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.gvMcpServer)).BeginInit();
            this.xtpSkills.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.gcSkills)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.gvSkills)).BeginInit();
            this.xtpMemoryMan.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.spcMemoryMan)).BeginInit();
            this.spcMemoryMan.Panel1.SuspendLayout();
            this.spcMemoryMan.Panel2.SuspendLayout();
            this.spcMemoryMan.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.tlMemFileTree)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.xtcMemMarkDown)).BeginInit();
            this.xtcMemMarkDown.SuspendLayout();
            this.xtpMemWebPreview.SuspendLayout();
            this.xtpMemMarkDown.SuspendLayout();
            this.xtpGeneral.SuspendLayout();
            this.grpLogging.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.seTranscriptMaxContentLength.Properties)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.ceToolCallLogEnabled.Properties)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.ceTranscriptIncludeHostContext.Properties)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.ceTranscriptIncludeToolCalls.Properties)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.seRetainedFileCount.Properties)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.seMaxFileSizeMb.Properties)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.cbeRuntimeLogLevel.Properties)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.ceRuntimeLogEnabled.Properties)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.cbeTranscriptLogMode.Properties)).BeginInit();
            this.grpRuntime.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.ceAutoMemoryRecorder.Properties)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.ceAutoCompactEnabled.Properties)).BeginInit();
            this.grpModelParams.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.seAttachmentFileCountThreshold.Properties)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.ceAutoResizeBigImage.Properties)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.seToolFetchFileThresholdMB.Properties)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.seInlineTextThresholdMB.Properties)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.seAttachmentInlineCharBudget.Properties)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.seMaxVisualPages.Properties)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.spHttpRequestTimeOut.Properties)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.seNativeThresholdKB.Properties)).BeginInit();
            this.SuspendLayout();
            // 
            // xtcMain
            // 
            this.xtcMain.Dock = System.Windows.Forms.DockStyle.Fill;
            this.xtcMain.Location = new System.Drawing.Point(0, 0);
            this.xtcMain.Margin = new System.Windows.Forms.Padding(4);
            this.xtcMain.Name = "xtcMain";
            this.xtcMain.SelectedTabPage = this.xtpModels;
            this.xtcMain.Size = new System.Drawing.Size(1171, 670);
            this.xtcMain.TabIndex = 0;
            this.xtcMain.TabPages.AddRange(new DevExpress.XtraTab.XtraTabPage[] {
            this.xtpModels,
            this.xtpTools,
            this.xtpMcp,
            this.xtpSkills,
            this.xtpMemoryMan,
            this.xtpGeneral});
            // 
            // xtpModels
            // 
            this.xtpModels.Controls.Add(this.gcModels);
            this.xtpModels.Controls.Add(this.sbdcModelTop);
            this.xtpModels.ImageOptions.Image = ((System.Drawing.Image)(resources.GetObject("xtpModels.ImageOptions.Image")));
            this.xtpModels.Margin = new System.Windows.Forms.Padding(4);
            this.xtpModels.Name = "xtpModels";
            this.xtpModels.Padding = new System.Windows.Forms.Padding(1);
            this.xtpModels.Size = new System.Drawing.Size(1165, 634);
            this.xtpModels.Text = "模型设置";
            // 
            // gcModels
            // 
            this.gcModels.Dock = System.Windows.Forms.DockStyle.Fill;
            this.gcModels.EmbeddedNavigator.Margin = new System.Windows.Forms.Padding(4);
            this.gcModels.Location = new System.Drawing.Point(1, 31);
            this.gcModels.MainView = this.gvModels;
            this.gcModels.Margin = new System.Windows.Forms.Padding(4);
            this.gcModels.MenuManager = this.barMan;
            this.gcModels.Name = "gcModels";
            this.gcModels.Size = new System.Drawing.Size(1163, 602);
            this.gcModels.TabIndex = 1;
            this.gcModels.ViewCollection.AddRange(new DevExpress.XtraGrid.Views.Base.BaseView[] {
            this.gvModels});
            // 
            // gvModels
            // 
            this.gvModels.Columns.AddRange(new DevExpress.XtraGrid.Columns.GridColumn[] {
            this.colId,
            this.colName,
            this.colProviderKind,
            this.colEndpoint,
            this.colApiKey,
            this.colModelId,
            this.colSupportsImageInput,
            this.colIsEnabled});
            this.gvModels.DetailHeight = 437;
            this.gvModels.GridControl = this.gcModels;
            this.gvModels.Name = "gvModels";
            this.gvModels.OptionsEditForm.PopupEditFormWidth = 1067;
            this.gvModels.OptionsView.ShowGroupPanel = false;
            this.gvModels.OptionsView.ShowIndicator = false;
            this.gvModels.PreviewFieldName = "SystemPrompt";
            // 
            // colId
            // 
            this.colId.Caption = "ID";
            this.colId.FieldName = "Id";
            this.colId.MinWidth = 27;
            this.colId.Name = "colId";
            this.colId.OptionsColumn.AllowEdit = false;
            this.colId.Width = 181;
            // 
            // colName
            // 
            this.colName.Caption = "名称";
            this.colName.FieldName = "Name";
            this.colName.MinWidth = 27;
            this.colName.Name = "colName";
            this.colName.OptionsColumn.AllowEdit = false;
            this.colName.Visible = true;
            this.colName.VisibleIndex = 0;
            this.colName.Width = 269;
            // 
            // colProviderKind
            // 
            this.colProviderKind.Caption = "API协议";
            this.colProviderKind.FieldName = "ProviderKind";
            this.colProviderKind.MinWidth = 27;
            this.colProviderKind.Name = "colProviderKind";
            this.colProviderKind.OptionsColumn.AllowEdit = false;
            this.colProviderKind.Visible = true;
            this.colProviderKind.VisibleIndex = 1;
            this.colProviderKind.Width = 151;
            // 
            // colEndpoint
            // 
            this.colEndpoint.Caption = "API地址";
            this.colEndpoint.FieldName = "Endpoint";
            this.colEndpoint.MinWidth = 27;
            this.colEndpoint.Name = "colEndpoint";
            this.colEndpoint.OptionsColumn.AllowEdit = false;
            this.colEndpoint.Visible = true;
            this.colEndpoint.VisibleIndex = 2;
            this.colEndpoint.Width = 472;
            // 
            // colApiKey
            // 
            this.colApiKey.Caption = "API密钥";
            this.colApiKey.FieldName = "ApiKey";
            this.colApiKey.MinWidth = 27;
            this.colApiKey.Name = "colApiKey";
            this.colApiKey.OptionsColumn.AllowEdit = false;
            this.colApiKey.Width = 100;
            // 
            // colModelId
            // 
            this.colModelId.Caption = "模型ID";
            this.colModelId.FieldName = "ModelId";
            this.colModelId.MinWidth = 27;
            this.colModelId.Name = "colModelId";
            this.colModelId.OptionsColumn.AllowEdit = false;
            this.colModelId.Visible = true;
            this.colModelId.VisibleIndex = 3;
            this.colModelId.Width = 133;
            // 
            // colSupportsImageInput
            // 
            this.colSupportsImageInput.Caption = "多模态";
            this.colSupportsImageInput.FieldName = "SupportsImageInput";
            this.colSupportsImageInput.MinWidth = 27;
            this.colSupportsImageInput.Name = "colSupportsImageInput";
            this.colSupportsImageInput.OptionsColumn.FixedWidth = true;
            this.colSupportsImageInput.Visible = true;
            this.colSupportsImageInput.VisibleIndex = 4;
            this.colSupportsImageInput.Width = 60;
            // 
            // colIsEnabled
            // 
            this.colIsEnabled.Caption = "启用";
            this.colIsEnabled.FieldName = "IsEnabled";
            this.colIsEnabled.MinWidth = 27;
            this.colIsEnabled.Name = "colIsEnabled";
            this.colIsEnabled.OptionsColumn.FixedWidth = true;
            this.colIsEnabled.Visible = true;
            this.colIsEnabled.VisibleIndex = 5;
            this.colIsEnabled.Width = 60;
            // 
            // barMan
            // 
            this.barMan.Bars.AddRange(new DevExpress.XtraBars.Bar[] {
            this.barModelTop,
            this.barMcpTop,
            this.barSkillTop,
            this.barMemBottom,
            this.barGeneralBottom});
            this.barMan.DockControls.Add(this.barDockControlTop);
            this.barMan.DockControls.Add(this.barDockControlBottom);
            this.barMan.DockControls.Add(this.barDockControlLeft);
            this.barMan.DockControls.Add(this.barDockControlRight);
            this.barMan.DockControls.Add(this.sbdcModelTop);
            this.barMan.DockControls.Add(this.sbdcMcpTop);
            this.barMan.DockControls.Add(this.sbdcSkillTop);
            this.barMan.DockControls.Add(this.sbdcMemBottom);
            this.barMan.DockControls.Add(this.sbdcSysConfigBottom);
            this.barMan.Form = this;
            this.barMan.Items.AddRange(new DevExpress.XtraBars.BarItem[] {
            this.btnAddModel,
            this.btnDeleteModel,
            this.btnExportListToJson,
            this.btnImportList,
            this.btnShowPreview,
            this.btnEditModel,
            this.btnNewMcp,
            this.btnEditMcp,
            this.btnDelMcp,
            this.btnShowDesc,
            this.btnImportMcp,
            this.btnExportMcp,
            this.btnNewSkill,
            this.btnEditSkill,
            this.btnDeleteSkill,
            this.btnExportSkills,
            this.btnImportSkills,
            this.btnSaveMem,
            this.bsiMemInfo,
            this.btnSaveGenenalConfig,
            this.btnCancelSaveGeneralConfig});
            this.barMan.MaxItemId = 21;
            // 
            // barModelTop
            // 
            this.barModelTop.BarName = "barModelTop";
            this.barModelTop.DockCol = 0;
            this.barModelTop.DockRow = 0;
            this.barModelTop.DockStyle = DevExpress.XtraBars.BarDockStyle.Standalone;
            this.barModelTop.FloatLocation = new System.Drawing.Point(60, 165);
            this.barModelTop.LinksPersistInfo.AddRange(new DevExpress.XtraBars.LinkPersistInfo[] {
            new DevExpress.XtraBars.LinkPersistInfo(DevExpress.XtraBars.BarLinkUserDefines.PaintStyle, this.btnAddModel, DevExpress.XtraBars.BarItemPaintStyle.CaptionGlyph),
            new DevExpress.XtraBars.LinkPersistInfo(DevExpress.XtraBars.BarLinkUserDefines.PaintStyle, this.btnEditModel, DevExpress.XtraBars.BarItemPaintStyle.CaptionGlyph),
            new DevExpress.XtraBars.LinkPersistInfo(DevExpress.XtraBars.BarLinkUserDefines.PaintStyle, this.btnDeleteModel, DevExpress.XtraBars.BarItemPaintStyle.CaptionGlyph),
            new DevExpress.XtraBars.LinkPersistInfo(DevExpress.XtraBars.BarLinkUserDefines.PaintStyle, this.btnExportListToJson, "", true, true, true, 0, null, DevExpress.XtraBars.BarItemPaintStyle.CaptionGlyph),
            new DevExpress.XtraBars.LinkPersistInfo(DevExpress.XtraBars.BarLinkUserDefines.PaintStyle, this.btnImportList, DevExpress.XtraBars.BarItemPaintStyle.CaptionGlyph),
            new DevExpress.XtraBars.LinkPersistInfo(this.btnShowPreview, true)});
            this.barModelTop.OptionsBar.AllowQuickCustomization = false;
            this.barModelTop.OptionsBar.DisableClose = true;
            this.barModelTop.OptionsBar.DisableCustomization = true;
            this.barModelTop.OptionsBar.DrawDragBorder = false;
            this.barModelTop.OptionsBar.MinHeight = 30;
            this.barModelTop.OptionsBar.UseWholeRow = true;
            this.barModelTop.StandaloneBarDockControl = this.sbdcModelTop;
            this.barModelTop.Text = "barModelTop";
            // 
            // btnAddModel
            // 
            this.btnAddModel.Caption = "新增模型";
            this.btnAddModel.Id = 0;
            this.btnAddModel.ImageOptions.Image = ((System.Drawing.Image)(resources.GetObject("btnAddModel.ImageOptions.Image")));
            this.btnAddModel.ImageOptions.LargeImage = ((System.Drawing.Image)(resources.GetObject("btnAddModel.ImageOptions.LargeImage")));
            this.btnAddModel.Name = "btnAddModel";
            // 
            // btnEditModel
            // 
            this.btnEditModel.Caption = "编辑选中模型";
            this.btnEditModel.Id = 5;
            this.btnEditModel.ImageOptions.Image = global::OfficeCopilot.Settings.WinForms.Properties.Resources.loadpagesetup_16x16;
            this.btnEditModel.ImageOptions.LargeImage = global::OfficeCopilot.Settings.WinForms.Properties.Resources.loadpagesetup_32x32;
            this.btnEditModel.Name = "btnEditModel";
            // 
            // btnDeleteModel
            // 
            this.btnDeleteModel.Caption = "删除选中的模型";
            this.btnDeleteModel.Id = 1;
            this.btnDeleteModel.ImageOptions.Image = ((System.Drawing.Image)(resources.GetObject("btnDeleteModel.ImageOptions.Image")));
            this.btnDeleteModel.ImageOptions.LargeImage = ((System.Drawing.Image)(resources.GetObject("btnDeleteModel.ImageOptions.LargeImage")));
            this.btnDeleteModel.Name = "btnDeleteModel";
            // 
            // btnExportListToJson
            // 
            this.btnExportListToJson.Caption = "导出列表";
            this.btnExportListToJson.Id = 2;
            this.btnExportListToJson.ImageOptions.Image = ((System.Drawing.Image)(resources.GetObject("btnExportListToJson.ImageOptions.Image")));
            this.btnExportListToJson.ImageOptions.LargeImage = ((System.Drawing.Image)(resources.GetObject("btnExportListToJson.ImageOptions.LargeImage")));
            this.btnExportListToJson.Name = "btnExportListToJson";
            // 
            // btnImportList
            // 
            this.btnImportList.Caption = "导入模型定义";
            this.btnImportList.Id = 3;
            this.btnImportList.ImageOptions.Image = ((System.Drawing.Image)(resources.GetObject("btnImportList.ImageOptions.Image")));
            this.btnImportList.ImageOptions.LargeImage = ((System.Drawing.Image)(resources.GetObject("btnImportList.ImageOptions.LargeImage")));
            this.btnImportList.Name = "btnImportList";
            // 
            // btnShowPreview
            // 
            this.btnShowPreview.Caption = "显示/隐藏系统提示词";
            this.btnShowPreview.Id = 4;
            this.btnShowPreview.ImageOptions.Image = ((System.Drawing.Image)(resources.GetObject("btnShowPreview.ImageOptions.Image")));
            this.btnShowPreview.ImageOptions.LargeImage = ((System.Drawing.Image)(resources.GetObject("btnShowPreview.ImageOptions.LargeImage")));
            this.btnShowPreview.Name = "btnShowPreview";
            // 
            // sbdcModelTop
            // 
            this.sbdcModelTop.CausesValidation = false;
            this.sbdcModelTop.Dock = System.Windows.Forms.DockStyle.Top;
            this.sbdcModelTop.Location = new System.Drawing.Point(1, 1);
            this.sbdcModelTop.Manager = this.barMan;
            this.sbdcModelTop.Name = "sbdcModelTop";
            this.sbdcModelTop.Size = new System.Drawing.Size(1163, 30);
            this.sbdcModelTop.Text = "standaloneBarDockControl1";
            // 
            // barMcpTop
            // 
            this.barMcpTop.BarName = "barMcpTop";
            this.barMcpTop.DockCol = 0;
            this.barMcpTop.DockRow = 0;
            this.barMcpTop.DockStyle = DevExpress.XtraBars.BarDockStyle.Standalone;
            this.barMcpTop.LinksPersistInfo.AddRange(new DevExpress.XtraBars.LinkPersistInfo[] {
            new DevExpress.XtraBars.LinkPersistInfo(DevExpress.XtraBars.BarLinkUserDefines.PaintStyle, this.btnNewMcp, DevExpress.XtraBars.BarItemPaintStyle.CaptionGlyph),
            new DevExpress.XtraBars.LinkPersistInfo(DevExpress.XtraBars.BarLinkUserDefines.PaintStyle, this.btnEditMcp, DevExpress.XtraBars.BarItemPaintStyle.CaptionGlyph),
            new DevExpress.XtraBars.LinkPersistInfo(DevExpress.XtraBars.BarLinkUserDefines.PaintStyle, this.btnDelMcp, DevExpress.XtraBars.BarItemPaintStyle.CaptionGlyph),
            new DevExpress.XtraBars.LinkPersistInfo(DevExpress.XtraBars.BarLinkUserDefines.PaintStyle, this.btnExportMcp, "", true, true, true, 0, null, DevExpress.XtraBars.BarItemPaintStyle.CaptionGlyph),
            new DevExpress.XtraBars.LinkPersistInfo(DevExpress.XtraBars.BarLinkUserDefines.PaintStyle, this.btnImportMcp, DevExpress.XtraBars.BarItemPaintStyle.CaptionGlyph)});
            this.barMcpTop.OptionsBar.AllowQuickCustomization = false;
            this.barMcpTop.OptionsBar.DisableClose = true;
            this.barMcpTop.OptionsBar.DisableCustomization = true;
            this.barMcpTop.OptionsBar.DrawDragBorder = false;
            this.barMcpTop.OptionsBar.MinHeight = 30;
            this.barMcpTop.OptionsBar.RotateWhenVertical = false;
            this.barMcpTop.OptionsBar.UseWholeRow = true;
            this.barMcpTop.StandaloneBarDockControl = this.sbdcMcpTop;
            this.barMcpTop.Text = "barMcpTop";
            // 
            // btnNewMcp
            // 
            this.btnNewMcp.Caption = "新增MCP";
            this.btnNewMcp.Id = 6;
            this.btnNewMcp.ImageOptions.Image = global::OfficeCopilot.Settings.WinForms.Properties.Resources.addfile_16x16;
            this.btnNewMcp.ImageOptions.LargeImage = global::OfficeCopilot.Settings.WinForms.Properties.Resources.addfile_32x32;
            this.btnNewMcp.Name = "btnNewMcp";
            // 
            // btnEditMcp
            // 
            this.btnEditMcp.Caption = "修改选中的MCP";
            this.btnEditMcp.Id = 7;
            this.btnEditMcp.ImageOptions.Image = global::OfficeCopilot.Settings.WinForms.Properties.Resources.edit_16x16;
            this.btnEditMcp.ImageOptions.LargeImage = global::OfficeCopilot.Settings.WinForms.Properties.Resources.edit_32x32;
            this.btnEditMcp.Name = "btnEditMcp";
            // 
            // btnDelMcp
            // 
            this.btnDelMcp.Caption = "删除MCP";
            this.btnDelMcp.Id = 8;
            this.btnDelMcp.ImageOptions.Image = global::OfficeCopilot.Settings.WinForms.Properties.Resources.deletelist_16x16;
            this.btnDelMcp.ImageOptions.LargeImage = global::OfficeCopilot.Settings.WinForms.Properties.Resources.deletelist_32x32;
            this.btnDelMcp.Name = "btnDelMcp";
            // 
            // btnExportMcp
            // 
            this.btnExportMcp.Caption = "导出列表";
            this.btnExportMcp.Id = 11;
            this.btnExportMcp.ImageOptions.Image = global::OfficeCopilot.Settings.WinForms.Properties.Resources.export_16x16;
            this.btnExportMcp.ImageOptions.LargeImage = global::OfficeCopilot.Settings.WinForms.Properties.Resources.export_32x32;
            this.btnExportMcp.Name = "btnExportMcp";
            // 
            // btnImportMcp
            // 
            this.btnImportMcp.Caption = "导入MCP配置";
            this.btnImportMcp.Id = 10;
            this.btnImportMcp.ImageOptions.Image = global::OfficeCopilot.Settings.WinForms.Properties.Resources.inbox_16x16;
            this.btnImportMcp.ImageOptions.LargeImage = global::OfficeCopilot.Settings.WinForms.Properties.Resources.inbox_32x32;
            this.btnImportMcp.Name = "btnImportMcp";
            // 
            // sbdcMcpTop
            // 
            this.sbdcMcpTop.CausesValidation = false;
            this.sbdcMcpTop.Dock = System.Windows.Forms.DockStyle.Top;
            this.sbdcMcpTop.Location = new System.Drawing.Point(1, 1);
            this.sbdcMcpTop.Manager = this.barMan;
            this.sbdcMcpTop.Margin = new System.Windows.Forms.Padding(0);
            this.sbdcMcpTop.Name = "sbdcMcpTop";
            this.sbdcMcpTop.Size = new System.Drawing.Size(1163, 30);
            this.sbdcMcpTop.Text = "standaloneBarDockControl1";
            // 
            // barSkillTop
            // 
            this.barSkillTop.BarName = "barSkillTop";
            this.barSkillTop.DockCol = 0;
            this.barSkillTop.DockRow = 0;
            this.barSkillTop.DockStyle = DevExpress.XtraBars.BarDockStyle.Standalone;
            this.barSkillTop.LinksPersistInfo.AddRange(new DevExpress.XtraBars.LinkPersistInfo[] {
            new DevExpress.XtraBars.LinkPersistInfo(DevExpress.XtraBars.BarLinkUserDefines.PaintStyle, this.btnNewSkill, DevExpress.XtraBars.BarItemPaintStyle.CaptionGlyph),
            new DevExpress.XtraBars.LinkPersistInfo(DevExpress.XtraBars.BarLinkUserDefines.PaintStyle, this.btnEditSkill, DevExpress.XtraBars.BarItemPaintStyle.CaptionGlyph),
            new DevExpress.XtraBars.LinkPersistInfo(DevExpress.XtraBars.BarLinkUserDefines.PaintStyle, this.btnDeleteSkill, DevExpress.XtraBars.BarItemPaintStyle.CaptionGlyph),
            new DevExpress.XtraBars.LinkPersistInfo(DevExpress.XtraBars.BarLinkUserDefines.PaintStyle, this.btnExportSkills, "", true, true, true, 0, null, DevExpress.XtraBars.BarItemPaintStyle.CaptionGlyph),
            new DevExpress.XtraBars.LinkPersistInfo(DevExpress.XtraBars.BarLinkUserDefines.PaintStyle, this.btnImportSkills, DevExpress.XtraBars.BarItemPaintStyle.CaptionGlyph)});
            this.barSkillTop.OptionsBar.AllowQuickCustomization = false;
            this.barSkillTop.OptionsBar.DisableClose = true;
            this.barSkillTop.OptionsBar.DisableCustomization = true;
            this.barSkillTop.OptionsBar.DrawDragBorder = false;
            this.barSkillTop.OptionsBar.MinHeight = 30;
            this.barSkillTop.OptionsBar.RotateWhenVertical = false;
            this.barSkillTop.OptionsBar.UseWholeRow = true;
            this.barSkillTop.StandaloneBarDockControl = this.sbdcSkillTop;
            this.barSkillTop.Text = "barSkillTop";
            // 
            // btnNewSkill
            // 
            this.btnNewSkill.Caption = "新增Skill";
            this.btnNewSkill.Id = 12;
            this.btnNewSkill.ImageOptions.Image = global::OfficeCopilot.Settings.WinForms.Properties.Resources.addfile_16x16;
            this.btnNewSkill.ImageOptions.LargeImage = global::OfficeCopilot.Settings.WinForms.Properties.Resources.addfile_32x32;
            this.btnNewSkill.Name = "btnNewSkill";
            // 
            // btnEditSkill
            // 
            this.btnEditSkill.Caption = "编辑选中的Skill";
            this.btnEditSkill.Id = 13;
            this.btnEditSkill.ImageOptions.Image = global::OfficeCopilot.Settings.WinForms.Properties.Resources.edit_16x16;
            this.btnEditSkill.ImageOptions.LargeImage = global::OfficeCopilot.Settings.WinForms.Properties.Resources.edit_32x32;
            this.btnEditSkill.Name = "btnEditSkill";
            // 
            // btnDeleteSkill
            // 
            this.btnDeleteSkill.Caption = "删除选中的Skill";
            this.btnDeleteSkill.Id = 14;
            this.btnDeleteSkill.ImageOptions.Image = global::OfficeCopilot.Settings.WinForms.Properties.Resources.deletelist_16x16;
            this.btnDeleteSkill.ImageOptions.LargeImage = global::OfficeCopilot.Settings.WinForms.Properties.Resources.deletelist_32x32;
            this.btnDeleteSkill.Name = "btnDeleteSkill";
            // 
            // btnExportSkills
            // 
            this.btnExportSkills.Caption = "导出列表";
            this.btnExportSkills.Id = 15;
            this.btnExportSkills.ImageOptions.Image = global::OfficeCopilot.Settings.WinForms.Properties.Resources.export_16x16;
            this.btnExportSkills.ImageOptions.LargeImage = global::OfficeCopilot.Settings.WinForms.Properties.Resources.export_32x32;
            this.btnExportSkills.Name = "btnExportSkills";
            // 
            // btnImportSkills
            // 
            this.btnImportSkills.Caption = "导入Skill配置";
            this.btnImportSkills.Id = 16;
            this.btnImportSkills.ImageOptions.Image = global::OfficeCopilot.Settings.WinForms.Properties.Resources.inbox_16x16;
            this.btnImportSkills.ImageOptions.LargeImage = global::OfficeCopilot.Settings.WinForms.Properties.Resources.inbox_32x32;
            this.btnImportSkills.Name = "btnImportSkills";
            // 
            // sbdcSkillTop
            // 
            this.sbdcSkillTop.CausesValidation = false;
            this.sbdcSkillTop.Dock = System.Windows.Forms.DockStyle.Top;
            this.sbdcSkillTop.Location = new System.Drawing.Point(1, 1);
            this.sbdcSkillTop.Manager = this.barMan;
            this.sbdcSkillTop.Name = "sbdcSkillTop";
            this.sbdcSkillTop.Size = new System.Drawing.Size(1163, 30);
            this.sbdcSkillTop.Text = "sbdcSkillTop";
            // 
            // barMemBottom
            // 
            this.barMemBottom.BarName = "barMemBottom";
            this.barMemBottom.DockCol = 0;
            this.barMemBottom.DockRow = 0;
            this.barMemBottom.DockStyle = DevExpress.XtraBars.BarDockStyle.Standalone;
            this.barMemBottom.LinksPersistInfo.AddRange(new DevExpress.XtraBars.LinkPersistInfo[] {
            new DevExpress.XtraBars.LinkPersistInfo(DevExpress.XtraBars.BarLinkUserDefines.PaintStyle, this.btnSaveMem, DevExpress.XtraBars.BarItemPaintStyle.CaptionGlyph),
            new DevExpress.XtraBars.LinkPersistInfo(this.bsiMemInfo)});
            this.barMemBottom.OptionsBar.AllowQuickCustomization = false;
            this.barMemBottom.OptionsBar.DisableClose = true;
            this.barMemBottom.OptionsBar.DisableCustomization = true;
            this.barMemBottom.OptionsBar.DistanceBetweenItems = 3;
            this.barMemBottom.OptionsBar.DrawDragBorder = false;
            this.barMemBottom.OptionsBar.DrawSizeGrip = true;
            this.barMemBottom.OptionsBar.MinHeight = 30;
            this.barMemBottom.OptionsBar.UseWholeRow = true;
            this.barMemBottom.StandaloneBarDockControl = this.sbdcMemBottom;
            this.barMemBottom.Text = "barMemBottom";
            // 
            // btnSaveMem
            // 
            this.btnSaveMem.Alignment = DevExpress.XtraBars.BarItemLinkAlignment.Right;
            this.btnSaveMem.Caption = "保存";
            this.btnSaveMem.Id = 17;
            this.btnSaveMem.ImageOptions.Image = global::OfficeCopilot.Settings.WinForms.Properties.Resources.save_16x16;
            this.btnSaveMem.ImageOptions.LargeImage = global::OfficeCopilot.Settings.WinForms.Properties.Resources.richeditbookmark_32x32;
            this.btnSaveMem.Name = "btnSaveMem";
            // 
            // bsiMemInfo
            // 
            this.bsiMemInfo.AutoSize = DevExpress.XtraBars.BarStaticItemSize.Spring;
            this.bsiMemInfo.Border = DevExpress.XtraEditors.Controls.BorderStyles.NoBorder;
            this.bsiMemInfo.Id = 18;
            this.bsiMemInfo.Name = "bsiMemInfo";
            // 
            // sbdcMemBottom
            // 
            this.sbdcMemBottom.CausesValidation = false;
            this.sbdcMemBottom.Dock = System.Windows.Forms.DockStyle.Bottom;
            this.sbdcMemBottom.Location = new System.Drawing.Point(0, 604);
            this.sbdcMemBottom.Manager = this.barMan;
            this.sbdcMemBottom.Name = "sbdcMemBottom";
            this.sbdcMemBottom.Size = new System.Drawing.Size(775, 30);
            this.sbdcMemBottom.Text = "standaloneBarDockControl1";
            // 
            // barGeneralBottom
            // 
            this.barGeneralBottom.BarName = "barGeneralBottom";
            this.barGeneralBottom.DockCol = 0;
            this.barGeneralBottom.DockRow = 0;
            this.barGeneralBottom.DockStyle = DevExpress.XtraBars.BarDockStyle.Standalone;
            this.barGeneralBottom.LinksPersistInfo.AddRange(new DevExpress.XtraBars.LinkPersistInfo[] {
            new DevExpress.XtraBars.LinkPersistInfo(DevExpress.XtraBars.BarLinkUserDefines.PaintStyle, this.btnSaveGenenalConfig, DevExpress.XtraBars.BarItemPaintStyle.CaptionGlyph),
            new DevExpress.XtraBars.LinkPersistInfo(DevExpress.XtraBars.BarLinkUserDefines.PaintStyle, this.btnCancelSaveGeneralConfig, "", true, true, true, 0, null, DevExpress.XtraBars.BarItemPaintStyle.CaptionGlyph)});
            this.barGeneralBottom.OptionsBar.AllowQuickCustomization = false;
            this.barGeneralBottom.OptionsBar.DisableClose = true;
            this.barGeneralBottom.OptionsBar.DisableCustomization = true;
            this.barGeneralBottom.OptionsBar.DistanceBetweenItems = 3;
            this.barGeneralBottom.OptionsBar.DrawDragBorder = false;
            this.barGeneralBottom.OptionsBar.MinHeight = 35;
            this.barGeneralBottom.OptionsBar.UseWholeRow = true;
            this.barGeneralBottom.StandaloneBarDockControl = this.sbdcSysConfigBottom;
            this.barGeneralBottom.Text = "barGeneralBottom";
            // 
            // btnSaveGenenalConfig
            // 
            this.btnSaveGenenalConfig.Alignment = DevExpress.XtraBars.BarItemLinkAlignment.Right;
            this.btnSaveGenenalConfig.Caption = "保存";
            this.btnSaveGenenalConfig.Id = 19;
            this.btnSaveGenenalConfig.ImageOptions.Image = global::OfficeCopilot.Settings.WinForms.Properties.Resources.save_16x16;
            this.btnSaveGenenalConfig.Name = "btnSaveGenenalConfig";
            // 
            // btnCancelSaveGeneralConfig
            // 
            this.btnCancelSaveGeneralConfig.Alignment = DevExpress.XtraBars.BarItemLinkAlignment.Right;
            this.btnCancelSaveGeneralConfig.Caption = "取消";
            this.btnCancelSaveGeneralConfig.Id = 20;
            this.btnCancelSaveGeneralConfig.ImageOptions.Image = global::OfficeCopilot.Settings.WinForms.Properties.Resources.cancel_16x16;
            this.btnCancelSaveGeneralConfig.Name = "btnCancelSaveGeneralConfig";
            // 
            // sbdcSysConfigBottom
            // 
            this.sbdcSysConfigBottom.CausesValidation = false;
            this.sbdcSysConfigBottom.Dock = System.Windows.Forms.DockStyle.Bottom;
            this.sbdcSysConfigBottom.Location = new System.Drawing.Point(1, 598);
            this.sbdcSysConfigBottom.Manager = this.barMan;
            this.sbdcSysConfigBottom.Name = "sbdcSysConfigBottom";
            this.sbdcSysConfigBottom.Size = new System.Drawing.Size(1163, 35);
            this.sbdcSysConfigBottom.Text = "standaloneBarDockControl1";
            // 
            // barDockControlTop
            // 
            this.barDockControlTop.CausesValidation = false;
            this.barDockControlTop.Dock = System.Windows.Forms.DockStyle.Top;
            this.barDockControlTop.Location = new System.Drawing.Point(0, 0);
            this.barDockControlTop.Manager = this.barMan;
            this.barDockControlTop.Margin = new System.Windows.Forms.Padding(4);
            this.barDockControlTop.Size = new System.Drawing.Size(1171, 0);
            // 
            // barDockControlBottom
            // 
            this.barDockControlBottom.CausesValidation = false;
            this.barDockControlBottom.Dock = System.Windows.Forms.DockStyle.Bottom;
            this.barDockControlBottom.Location = new System.Drawing.Point(0, 670);
            this.barDockControlBottom.Manager = this.barMan;
            this.barDockControlBottom.Margin = new System.Windows.Forms.Padding(4);
            this.barDockControlBottom.Size = new System.Drawing.Size(1171, 0);
            // 
            // barDockControlLeft
            // 
            this.barDockControlLeft.CausesValidation = false;
            this.barDockControlLeft.Dock = System.Windows.Forms.DockStyle.Left;
            this.barDockControlLeft.Location = new System.Drawing.Point(0, 0);
            this.barDockControlLeft.Manager = this.barMan;
            this.barDockControlLeft.Margin = new System.Windows.Forms.Padding(4);
            this.barDockControlLeft.Size = new System.Drawing.Size(0, 670);
            // 
            // barDockControlRight
            // 
            this.barDockControlRight.CausesValidation = false;
            this.barDockControlRight.Dock = System.Windows.Forms.DockStyle.Right;
            this.barDockControlRight.Location = new System.Drawing.Point(1171, 0);
            this.barDockControlRight.Manager = this.barMan;
            this.barDockControlRight.Margin = new System.Windows.Forms.Padding(4);
            this.barDockControlRight.Size = new System.Drawing.Size(0, 670);
            // 
            // btnShowDesc
            // 
            this.btnShowDesc.Caption = "查看";
            this.btnShowDesc.Id = 9;
            this.btnShowDesc.ImageOptions.Image = global::OfficeCopilot.Settings.WinForms.Properties.Resources.show_16x16;
            this.btnShowDesc.ImageOptions.LargeImage = global::OfficeCopilot.Settings.WinForms.Properties.Resources.show_32x32;
            this.btnShowDesc.Name = "btnShowDesc";
            // 
            // xtpTools
            // 
            this.xtpTools.Controls.Add(this.spcTools);
            this.xtpTools.ImageOptions.Image = ((System.Drawing.Image)(resources.GetObject("xtpTools.ImageOptions.Image")));
            this.xtpTools.Margin = new System.Windows.Forms.Padding(4);
            this.xtpTools.Name = "xtpTools";
            this.xtpTools.Padding = new System.Windows.Forms.Padding(1);
            this.xtpTools.Size = new System.Drawing.Size(1165, 634);
            this.xtpTools.Text = "内置工具";
            // 
            // spcTools
            // 
            this.spcTools.Dock = System.Windows.Forms.DockStyle.Fill;
            this.spcTools.Location = new System.Drawing.Point(1, 1);
            this.spcTools.Margin = new System.Windows.Forms.Padding(4);
            this.spcTools.Name = "spcTools";
            // 
            // spcTools.Panel1
            // 
            this.spcTools.Panel1.Controls.Add(this.gcToolsGroup);
            // 
            // spcTools.Panel2
            // 
            this.spcTools.Panel2.Controls.Add(this.gcTools);
            this.spcTools.Size = new System.Drawing.Size(1163, 632);
            this.spcTools.SplitterDistance = 233;
            this.spcTools.SplitterWidth = 5;
            this.spcTools.TabIndex = 0;
            // 
            // gcToolsGroup
            // 
            this.gcToolsGroup.Dock = System.Windows.Forms.DockStyle.Fill;
            this.gcToolsGroup.EmbeddedNavigator.Margin = new System.Windows.Forms.Padding(4);
            this.gcToolsGroup.Location = new System.Drawing.Point(0, 0);
            this.gcToolsGroup.MainView = this.gvToolsGroup;
            this.gcToolsGroup.Margin = new System.Windows.Forms.Padding(4);
            this.gcToolsGroup.MenuManager = this.barMan;
            this.gcToolsGroup.Name = "gcToolsGroup";
            this.gcToolsGroup.Size = new System.Drawing.Size(233, 632);
            this.gcToolsGroup.TabIndex = 0;
            this.gcToolsGroup.ViewCollection.AddRange(new DevExpress.XtraGrid.Views.Base.BaseView[] {
            this.gvToolsGroup});
            // 
            // gvToolsGroup
            // 
            this.gvToolsGroup.Columns.AddRange(new DevExpress.XtraGrid.Columns.GridColumn[] {
            this.colToolsGroupName,
            this.colToolsGroupEnable});
            this.gvToolsGroup.DetailHeight = 437;
            this.gvToolsGroup.GridControl = this.gcToolsGroup;
            this.gvToolsGroup.Name = "gvToolsGroup";
            this.gvToolsGroup.OptionsEditForm.PopupEditFormWidth = 1067;
            this.gvToolsGroup.OptionsView.ShowGroupPanel = false;
            this.gvToolsGroup.OptionsView.ShowIndicator = false;
            this.gvToolsGroup.OptionsView.ShowVerticalLines = DevExpress.Utils.DefaultBoolean.False;
            // 
            // colToolsGroupName
            // 
            this.colToolsGroupName.Caption = "分组";
            this.colToolsGroupName.MinWidth = 27;
            this.colToolsGroupName.Name = "colToolsGroupName";
            this.colToolsGroupName.OptionsColumn.ReadOnly = true;
            this.colToolsGroupName.Visible = true;
            this.colToolsGroupName.VisibleIndex = 0;
            this.colToolsGroupName.Width = 288;
            // 
            // colToolsGroupEnable
            // 
            this.colToolsGroupEnable.Caption = "启用";
            this.colToolsGroupEnable.MinWidth = 27;
            this.colToolsGroupEnable.Name = "colToolsGroupEnable";
            this.colToolsGroupEnable.OptionsColumn.FixedWidth = true;
            this.colToolsGroupEnable.Visible = true;
            this.colToolsGroupEnable.VisibleIndex = 1;
            this.colToolsGroupEnable.Width = 53;
            // 
            // gcTools
            // 
            this.gcTools.Dock = System.Windows.Forms.DockStyle.Fill;
            this.gcTools.EmbeddedNavigator.Margin = new System.Windows.Forms.Padding(4);
            this.gcTools.Location = new System.Drawing.Point(0, 0);
            this.gcTools.MainView = this.gvTools;
            this.gcTools.Margin = new System.Windows.Forms.Padding(4);
            this.gcTools.MenuManager = this.barMan;
            this.gcTools.Name = "gcTools";
            this.gcTools.Size = new System.Drawing.Size(925, 632);
            this.gcTools.TabIndex = 0;
            this.gcTools.ViewCollection.AddRange(new DevExpress.XtraGrid.Views.Base.BaseView[] {
            this.gvTools});
            // 
            // gvTools
            // 
            this.gvTools.Columns.AddRange(new DevExpress.XtraGrid.Columns.GridColumn[] {
            this.colToolName,
            this.colDescription,
            this.colEnableTool,
            this.colNeedConfirm,
            this.colTestTool});
            this.gvTools.DetailHeight = 437;
            this.gvTools.GridControl = this.gcTools;
            this.gvTools.Name = "gvTools";
            this.gvTools.OptionsEditForm.PopupEditFormWidth = 1067;
            this.gvTools.OptionsFind.AlwaysVisible = true;
            this.gvTools.OptionsFind.FindMode = DevExpress.XtraEditors.FindMode.Always;
            this.gvTools.OptionsFind.FindPanelLocation = DevExpress.XtraGrid.Views.Grid.GridFindPanelLocation.Panel;
            this.gvTools.OptionsView.ShowGroupPanel = false;
            this.gvTools.OptionsView.ShowIndicator = false;
            // 
            // colToolName
            // 
            this.colToolName.Caption = "名称";
            this.colToolName.MinWidth = 27;
            this.colToolName.Name = "colToolName";
            this.colToolName.OptionsColumn.ReadOnly = true;
            this.colToolName.Visible = true;
            this.colToolName.VisibleIndex = 0;
            this.colToolName.Width = 200;
            // 
            // colDescription
            // 
            this.colDescription.Caption = "简述";
            this.colDescription.MinWidth = 27;
            this.colDescription.Name = "colDescription";
            this.colDescription.OptionsColumn.ReadOnly = true;
            this.colDescription.Visible = true;
            this.colDescription.VisibleIndex = 1;
            this.colDescription.Width = 559;
            // 
            // colEnableTool
            // 
            this.colEnableTool.Caption = "启用";
            this.colEnableTool.MinWidth = 27;
            this.colEnableTool.Name = "colEnableTool";
            this.colEnableTool.OptionsColumn.FixedWidth = true;
            this.colEnableTool.Visible = true;
            this.colEnableTool.VisibleIndex = 2;
            this.colEnableTool.Width = 53;
            // 
            // colNeedConfirm
            // 
            this.colNeedConfirm.Caption = "免确认";
            this.colNeedConfirm.MinWidth = 27;
            this.colNeedConfirm.Name = "colNeedConfirm";
            this.colNeedConfirm.OptionsColumn.FixedWidth = true;
            this.colNeedConfirm.Visible = true;
            this.colNeedConfirm.VisibleIndex = 3;
            this.colNeedConfirm.Width = 60;
            // 
            // colTestTool
            // 
            this.colTestTool.Caption = "详情";
            this.colTestTool.MinWidth = 27;
            this.colTestTool.Name = "colTestTool";
            this.colTestTool.OptionsColumn.FixedWidth = true;
            this.colTestTool.Visible = true;
            this.colTestTool.VisibleIndex = 4;
            this.colTestTool.Width = 53;
            // 
            // xtpMcp
            // 
            this.xtpMcp.Controls.Add(this.gcMcpServer);
            this.xtpMcp.Controls.Add(this.sbdcMcpTop);
            this.xtpMcp.ImageOptions.Image = global::OfficeCopilot.Settings.WinForms.Properties.Resources.customization_16x16;
            this.xtpMcp.Margin = new System.Windows.Forms.Padding(4);
            this.xtpMcp.Name = "xtpMcp";
            this.xtpMcp.Padding = new System.Windows.Forms.Padding(1);
            this.xtpMcp.Size = new System.Drawing.Size(1165, 634);
            this.xtpMcp.Text = "MCP服务";
            // 
            // gcMcpServer
            // 
            this.gcMcpServer.Dock = System.Windows.Forms.DockStyle.Fill;
            this.gcMcpServer.EmbeddedNavigator.Margin = new System.Windows.Forms.Padding(4);
            this.gcMcpServer.Location = new System.Drawing.Point(1, 31);
            this.gcMcpServer.MainView = this.gvMcpServer;
            this.gcMcpServer.Margin = new System.Windows.Forms.Padding(4);
            this.gcMcpServer.MenuManager = this.barMan;
            this.gcMcpServer.Name = "gcMcpServer";
            this.gcMcpServer.Size = new System.Drawing.Size(1163, 602);
            this.gcMcpServer.TabIndex = 5;
            this.gcMcpServer.ViewCollection.AddRange(new DevExpress.XtraGrid.Views.Base.BaseView[] {
            this.gvMcpServer});
            // 
            // gvMcpServer
            // 
            this.gvMcpServer.Columns.AddRange(new DevExpress.XtraGrid.Columns.GridColumn[] {
            this.colMcpId,
            this.colMcpName,
            this.colMcpDesc,
            this.colEnableMcp});
            this.gvMcpServer.DetailHeight = 437;
            this.gvMcpServer.GridControl = this.gcMcpServer;
            this.gvMcpServer.Name = "gvMcpServer";
            this.gvMcpServer.OptionsBehavior.Editable = false;
            this.gvMcpServer.OptionsEditForm.PopupEditFormWidth = 1067;
            this.gvMcpServer.OptionsSelection.EnableAppearanceFocusedCell = false;
            this.gvMcpServer.OptionsView.ShowGroupPanel = false;
            this.gvMcpServer.OptionsView.ShowIndicator = false;
            this.gvMcpServer.OptionsView.ShowVerticalLines = DevExpress.Utils.DefaultBoolean.False;
            // 
            // colMcpId
            // 
            this.colMcpId.Caption = "ID";
            this.colMcpId.FieldName = "Id";
            this.colMcpId.MinWidth = 25;
            this.colMcpId.Name = "colMcpId";
            this.colMcpId.Width = 94;
            // 
            // colMcpName
            // 
            this.colMcpName.Caption = "名称";
            this.colMcpName.MinWidth = 27;
            this.colMcpName.Name = "colMcpName";
            this.colMcpName.OptionsColumn.FixedWidth = true;
            this.colMcpName.OptionsColumn.ReadOnly = true;
            this.colMcpName.Visible = true;
            this.colMcpName.VisibleIndex = 0;
            this.colMcpName.Width = 200;
            // 
            // colMcpDesc
            // 
            this.colMcpDesc.Caption = "描述";
            this.colMcpDesc.FieldName = "Description";
            this.colMcpDesc.MinWidth = 25;
            this.colMcpDesc.Name = "colMcpDesc";
            this.colMcpDesc.Visible = true;
            this.colMcpDesc.VisibleIndex = 1;
            this.colMcpDesc.Width = 908;
            // 
            // colEnableMcp
            // 
            this.colEnableMcp.Caption = "启用";
            this.colEnableMcp.MinWidth = 27;
            this.colEnableMcp.Name = "colEnableMcp";
            this.colEnableMcp.OptionsColumn.FixedWidth = true;
            this.colEnableMcp.Visible = true;
            this.colEnableMcp.VisibleIndex = 2;
            this.colEnableMcp.Width = 53;
            // 
            // xtpSkills
            // 
            this.xtpSkills.Controls.Add(this.gcSkills);
            this.xtpSkills.Controls.Add(this.sbdcSkillTop);
            this.xtpSkills.ImageOptions.SvgImage = global::OfficeCopilot.Settings.WinForms.Properties.Resources.ai;
            this.xtpSkills.ImageOptions.SvgImageSize = new System.Drawing.Size(12, 12);
            this.xtpSkills.Margin = new System.Windows.Forms.Padding(4);
            this.xtpSkills.Name = "xtpSkills";
            this.xtpSkills.Padding = new System.Windows.Forms.Padding(1);
            this.xtpSkills.Size = new System.Drawing.Size(1165, 634);
            this.xtpSkills.Text = "Skills技能";
            // 
            // gcSkills
            // 
            this.gcSkills.Dock = System.Windows.Forms.DockStyle.Fill;
            this.gcSkills.EmbeddedNavigator.Margin = new System.Windows.Forms.Padding(4);
            this.gcSkills.Location = new System.Drawing.Point(1, 31);
            this.gcSkills.MainView = this.gvSkills;
            this.gcSkills.Margin = new System.Windows.Forms.Padding(4);
            this.gcSkills.MenuManager = this.barMan;
            this.gcSkills.Name = "gcSkills";
            this.gcSkills.Size = new System.Drawing.Size(1163, 602);
            this.gcSkills.TabIndex = 5;
            this.gcSkills.ViewCollection.AddRange(new DevExpress.XtraGrid.Views.Base.BaseView[] {
            this.gvSkills});
            // 
            // gvSkills
            // 
            this.gvSkills.Columns.AddRange(new DevExpress.XtraGrid.Columns.GridColumn[] {
            this.colSkillId,
            this.colSkillName,
            this.colSkillDesc,
            this.colSkillEnabled});
            this.gvSkills.DetailHeight = 437;
            this.gvSkills.GridControl = this.gcSkills;
            this.gvSkills.Name = "gvSkills";
            this.gvSkills.OptionsBehavior.Editable = false;
            this.gvSkills.OptionsEditForm.PopupEditFormWidth = 1067;
            this.gvSkills.OptionsSelection.EnableAppearanceFocusedCell = false;
            this.gvSkills.OptionsView.ShowGroupPanel = false;
            this.gvSkills.OptionsView.ShowIndicator = false;
            this.gvSkills.OptionsView.ShowVerticalLines = DevExpress.Utils.DefaultBoolean.False;
            // 
            // colSkillId
            // 
            this.colSkillId.Caption = "ID";
            this.colSkillId.FieldName = "Id";
            this.colSkillId.MinWidth = 25;
            this.colSkillId.Name = "colSkillId";
            this.colSkillId.Width = 94;
            // 
            // colSkillName
            // 
            this.colSkillName.Caption = "名称";
            this.colSkillName.FieldName = "Name";
            this.colSkillName.MinWidth = 27;
            this.colSkillName.Name = "colSkillName";
            this.colSkillName.OptionsColumn.FixedWidth = true;
            this.colSkillName.Visible = true;
            this.colSkillName.VisibleIndex = 0;
            this.colSkillName.Width = 200;
            // 
            // colSkillDesc
            // 
            this.colSkillDesc.Caption = "描述";
            this.colSkillDesc.FieldName = "Description";
            this.colSkillDesc.MinWidth = 25;
            this.colSkillDesc.Name = "colSkillDesc";
            this.colSkillDesc.Visible = true;
            this.colSkillDesc.VisibleIndex = 1;
            this.colSkillDesc.Width = 808;
            // 
            // colSkillEnabled
            // 
            this.colSkillEnabled.Caption = "启用";
            this.colSkillEnabled.FieldName = "IsEnabled";
            this.colSkillEnabled.MinWidth = 27;
            this.colSkillEnabled.Name = "colSkillEnabled";
            this.colSkillEnabled.OptionsColumn.FixedWidth = true;
            this.colSkillEnabled.Visible = true;
            this.colSkillEnabled.VisibleIndex = 2;
            this.colSkillEnabled.Width = 53;
            // 
            // xtpMemoryMan
            // 
            this.xtpMemoryMan.Controls.Add(this.spcMemoryMan);
            this.xtpMemoryMan.ImageOptions.Image = global::OfficeCopilot.Settings.WinForms.Properties.Resources.bookmark_16x16;
            this.xtpMemoryMan.Name = "xtpMemoryMan";
            this.xtpMemoryMan.Size = new System.Drawing.Size(1165, 634);
            this.xtpMemoryMan.Text = "记忆管理器";
            // 
            // spcMemoryMan
            // 
            this.spcMemoryMan.Dock = System.Windows.Forms.DockStyle.Fill;
            this.spcMemoryMan.Location = new System.Drawing.Point(0, 0);
            this.spcMemoryMan.Name = "spcMemoryMan";
            // 
            // spcMemoryMan.Panel1
            // 
            this.spcMemoryMan.Panel1.Controls.Add(this.tlMemFileTree);
            this.spcMemoryMan.Panel1.Padding = new System.Windows.Forms.Padding(5);
            // 
            // spcMemoryMan.Panel2
            // 
            this.spcMemoryMan.Panel2.Controls.Add(this.xtcMemMarkDown);
            this.spcMemoryMan.Panel2.Controls.Add(this.sbdcMemBottom);
            this.spcMemoryMan.Size = new System.Drawing.Size(1165, 634);
            this.spcMemoryMan.SplitterDistance = 386;
            this.spcMemoryMan.TabIndex = 0;
            // 
            // tlMemFileTree
            // 
            this.tlMemFileTree.BorderStyle = DevExpress.XtraEditors.Controls.BorderStyles.NoBorder;
            this.tlMemFileTree.Dock = System.Windows.Forms.DockStyle.Fill;
            this.tlMemFileTree.Location = new System.Drawing.Point(5, 5);
            this.tlMemFileTree.MenuManager = this.barMan;
            this.tlMemFileTree.Name = "tlMemFileTree";
            this.tlMemFileTree.OptionsBehavior.Editable = false;
            this.tlMemFileTree.OptionsMenu.EnableColumnMenu = false;
            this.tlMemFileTree.OptionsMenu.EnableFooterMenu = false;
            this.tlMemFileTree.OptionsMenu.EnableNodeMenu = false;
            this.tlMemFileTree.OptionsView.ShowColumns = false;
            this.tlMemFileTree.OptionsView.ShowIndicator = false;
            this.tlMemFileTree.OptionsView.ShowTreeLines = DevExpress.Utils.DefaultBoolean.True;
            this.tlMemFileTree.RowHeight = 30;
            this.tlMemFileTree.Size = new System.Drawing.Size(376, 624);
            this.tlMemFileTree.TabIndex = 0;
            // 
            // xtcMemMarkDown
            // 
            this.xtcMemMarkDown.Dock = System.Windows.Forms.DockStyle.Fill;
            this.xtcMemMarkDown.HeaderLocation = DevExpress.XtraTab.TabHeaderLocation.Right;
            this.xtcMemMarkDown.Location = new System.Drawing.Point(0, 0);
            this.xtcMemMarkDown.Name = "xtcMemMarkDown";
            this.xtcMemMarkDown.SelectedTabPage = this.xtpMemWebPreview;
            this.xtcMemMarkDown.Size = new System.Drawing.Size(775, 604);
            this.xtcMemMarkDown.TabIndex = 1;
            this.xtcMemMarkDown.TabPages.AddRange(new DevExpress.XtraTab.XtraTabPage[] {
            this.xtpMemWebPreview,
            this.xtpMemMarkDown});
            // 
            // xtpMemWebPreview
            // 
            this.xtpMemWebPreview.Controls.Add(this.webPreview);
            this.xtpMemWebPreview.ImageOptions.Image = global::OfficeCopilot.Settings.WinForms.Properties.Resources.viewonweb_16x16;
            this.xtpMemWebPreview.Name = "xtpMemWebPreview";
            this.xtpMemWebPreview.Size = new System.Drawing.Size(738, 598);
            this.xtpMemWebPreview.Text = "Skill预览";
            // 
            // webPreview
            // 
            this.webPreview.Dock = System.Windows.Forms.DockStyle.Fill;
            this.webPreview.Location = new System.Drawing.Point(0, 0);
            this.webPreview.MinimumSize = new System.Drawing.Size(20, 20);
            this.webPreview.Name = "webPreview";
            this.webPreview.Size = new System.Drawing.Size(738, 598);
            this.webPreview.TabIndex = 13;
            // 
            // xtpMemMarkDown
            // 
            this.xtpMemMarkDown.Controls.Add(this.txtMarkDown);
            this.xtpMemMarkDown.ImageOptions.Image = global::OfficeCopilot.Settings.WinForms.Properties.Resources.scripts_16x16;
            this.xtpMemMarkDown.Name = "xtpMemMarkDown";
            this.xtpMemMarkDown.Size = new System.Drawing.Size(738, 598);
            this.xtpMemMarkDown.Text = "MarkDown编辑";
            // 
            // txtMarkDown
            // 
            this.txtMarkDown.ActiveViewType = DevExpress.XtraRichEdit.RichEditViewType.Simple;
            this.txtMarkDown.Dock = System.Windows.Forms.DockStyle.Fill;
            this.txtMarkDown.LayoutUnit = DevExpress.XtraRichEdit.DocumentLayoutUnit.Pixel;
            this.txtMarkDown.Location = new System.Drawing.Point(0, 0);
            this.txtMarkDown.Margin = new System.Windows.Forms.Padding(4);
            this.txtMarkDown.MenuManager = this.barMan;
            this.txtMarkDown.Name = "txtMarkDown";
            this.txtMarkDown.Options.Behavior.CreateNew = DevExpress.XtraRichEdit.DocumentCapability.Hidden;
            this.txtMarkDown.Options.Behavior.Open = DevExpress.XtraRichEdit.DocumentCapability.Hidden;
            this.txtMarkDown.Options.Behavior.Save = DevExpress.XtraRichEdit.DocumentCapability.Hidden;
            this.txtMarkDown.Options.Behavior.SaveAs = DevExpress.XtraRichEdit.DocumentCapability.Hidden;
            this.txtMarkDown.Options.HorizontalRuler.Visibility = DevExpress.XtraRichEdit.RichEditRulerVisibility.Hidden;
            this.txtMarkDown.Size = new System.Drawing.Size(738, 598);
            this.txtMarkDown.TabIndex = 0;
            this.txtMarkDown.Views.SimpleView.Padding = new DevExpress.Portable.PortablePadding(8);
            // 
            // xtpGeneral
            // 
            this.xtpGeneral.Controls.Add(this.sbdcSysConfigBottom);
            this.xtpGeneral.Controls.Add(this.grpLogging);
            this.xtpGeneral.Controls.Add(this.grpRuntime);
            this.xtpGeneral.Controls.Add(this.grpModelParams);
            this.xtpGeneral.ImageOptions.Image = ((System.Drawing.Image)(resources.GetObject("xtpGeneral.ImageOptions.Image")));
            this.xtpGeneral.Margin = new System.Windows.Forms.Padding(4);
            this.xtpGeneral.Name = "xtpGeneral";
            this.xtpGeneral.Padding = new System.Windows.Forms.Padding(1);
            this.xtpGeneral.Size = new System.Drawing.Size(1165, 634);
            this.xtpGeneral.Text = "系统设置";
            // 
            // grpLogging
            // 
            this.grpLogging.Controls.Add(this.seTranscriptMaxContentLength);
            this.grpLogging.Controls.Add(this.lbTranscriptMaxContentLength);
            this.grpLogging.Controls.Add(this.ceToolCallLogEnabled);
            this.grpLogging.Controls.Add(this.ceTranscriptIncludeHostContext);
            this.grpLogging.Controls.Add(this.ceTranscriptIncludeToolCalls);
            this.grpLogging.Controls.Add(this.seRetainedFileCount);
            this.grpLogging.Controls.Add(this.lbRetainedFileCount);
            this.grpLogging.Controls.Add(this.seMaxFileSizeMb);
            this.grpLogging.Controls.Add(this.lbMaxFileSizeMb);
            this.grpLogging.Controls.Add(this.cbeRuntimeLogLevel);
            this.grpLogging.Controls.Add(this.lbRuntimeLogLevel);
            this.grpLogging.Controls.Add(this.ceRuntimeLogEnabled);
            this.grpLogging.Controls.Add(this.cbeTranscriptLogMode);
            this.grpLogging.Controls.Add(this.lbTranscriptLogMode);
            this.grpLogging.Font = new System.Drawing.Font("Microsoft YaHei UI", 9F, System.Drawing.FontStyle.Bold);
            this.grpLogging.Location = new System.Drawing.Point(618, 32);
            this.grpLogging.Name = "grpLogging";
            this.grpLogging.Size = new System.Drawing.Size(525, 438);
            this.grpLogging.TabIndex = 13;
            this.grpLogging.TabStop = false;
            this.grpLogging.Text = "日志设置";
            // 
            // seTranscriptMaxContentLength
            // 
            this.seTranscriptMaxContentLength.EditValue = new decimal(new int[] {
            0,
            0,
            0,
            0});
            this.seTranscriptMaxContentLength.Location = new System.Drawing.Point(302, 262);
            this.seTranscriptMaxContentLength.Name = "seTranscriptMaxContentLength";
            this.seTranscriptMaxContentLength.Properties.Buttons.AddRange(new DevExpress.XtraEditors.Controls.EditorButton[] {
            new DevExpress.XtraEditors.Controls.EditorButton(DevExpress.XtraEditors.Controls.ButtonPredefines.Combo)});
            this.seTranscriptMaxContentLength.Properties.MaxValue = new decimal(new int[] {
            1000000,
            0,
            0,
            0});
            this.seTranscriptMaxContentLength.Size = new System.Drawing.Size(190, 24);
            this.seTranscriptMaxContentLength.TabIndex = 17;
            // 
            // lbTranscriptMaxContentLength
            // 
            this.lbTranscriptMaxContentLength.Location = new System.Drawing.Point(39, 265);
            this.lbTranscriptMaxContentLength.Name = "lbTranscriptMaxContentLength";
            this.lbTranscriptMaxContentLength.Size = new System.Drawing.Size(236, 18);
            this.lbTranscriptMaxContentLength.TabIndex = 16;
            this.lbTranscriptMaxContentLength.Text = "对话日志最大内容长度 (0=不限制):";
            // 
            // ceToolCallLogEnabled
            // 
            this.ceToolCallLogEnabled.EditValue = true;
            this.ceToolCallLogEnabled.Location = new System.Drawing.Point(39, 83);
            this.ceToolCallLogEnabled.Name = "ceToolCallLogEnabled";
            this.ceToolCallLogEnabled.Properties.Caption = "启用工具调用日志";
            this.ceToolCallLogEnabled.Size = new System.Drawing.Size(170, 22);
            this.ceToolCallLogEnabled.TabIndex = 15;
            // 
            // ceTranscriptIncludeHostContext
            // 
            this.ceTranscriptIncludeHostContext.EditValue = true;
            this.ceTranscriptIncludeHostContext.Location = new System.Drawing.Point(39, 177);
            this.ceTranscriptIncludeHostContext.Name = "ceTranscriptIncludeHostContext";
            this.ceTranscriptIncludeHostContext.Properties.Caption = "对话日志包含宿主上下文";
            this.ceTranscriptIncludeHostContext.Size = new System.Drawing.Size(218, 22);
            this.ceTranscriptIncludeHostContext.TabIndex = 13;
            // 
            // ceTranscriptIncludeToolCalls
            // 
            this.ceTranscriptIncludeToolCalls.EditValue = true;
            this.ceTranscriptIncludeToolCalls.Location = new System.Drawing.Point(39, 130);
            this.ceTranscriptIncludeToolCalls.Name = "ceTranscriptIncludeToolCalls";
            this.ceTranscriptIncludeToolCalls.Properties.Caption = "对话日志包含工具调用";
            this.ceTranscriptIncludeToolCalls.Size = new System.Drawing.Size(200, 22);
            this.ceTranscriptIncludeToolCalls.TabIndex = 12;
            // 
            // seRetainedFileCount
            // 
            this.seRetainedFileCount.EditValue = new decimal(new int[] {
            10,
            0,
            0,
            0});
            this.seRetainedFileCount.Location = new System.Drawing.Point(302, 385);
            this.seRetainedFileCount.Name = "seRetainedFileCount";
            this.seRetainedFileCount.Properties.Buttons.AddRange(new DevExpress.XtraEditors.Controls.EditorButton[] {
            new DevExpress.XtraEditors.Controls.EditorButton(DevExpress.XtraEditors.Controls.ButtonPredefines.Combo)});
            this.seRetainedFileCount.Properties.MaxValue = new decimal(new int[] {
            100,
            0,
            0,
            0});
            this.seRetainedFileCount.Properties.MinValue = new decimal(new int[] {
            1,
            0,
            0,
            0});
            this.seRetainedFileCount.Size = new System.Drawing.Size(190, 24);
            this.seRetainedFileCount.TabIndex = 11;
            // 
            // lbRetainedFileCount
            // 
            this.lbRetainedFileCount.Location = new System.Drawing.Point(39, 388);
            this.lbRetainedFileCount.Name = "lbRetainedFileCount";
            this.lbRetainedFileCount.Size = new System.Drawing.Size(125, 18);
            this.lbRetainedFileCount.TabIndex = 10;
            this.lbRetainedFileCount.Text = "日志文件保留数量:";
            // 
            // seMaxFileSizeMb
            // 
            this.seMaxFileSizeMb.EditValue = new decimal(new int[] {
            5,
            0,
            0,
            0});
            this.seMaxFileSizeMb.Location = new System.Drawing.Point(302, 344);
            this.seMaxFileSizeMb.Name = "seMaxFileSizeMb";
            this.seMaxFileSizeMb.Properties.Buttons.AddRange(new DevExpress.XtraEditors.Controls.EditorButton[] {
            new DevExpress.XtraEditors.Controls.EditorButton(DevExpress.XtraEditors.Controls.ButtonPredefines.Combo)});
            this.seMaxFileSizeMb.Properties.MaxValue = new decimal(new int[] {
            1000,
            0,
            0,
            0});
            this.seMaxFileSizeMb.Properties.MinValue = new decimal(new int[] {
            1,
            0,
            0,
            0});
            this.seMaxFileSizeMb.Size = new System.Drawing.Size(190, 24);
            this.seMaxFileSizeMb.TabIndex = 9;
            // 
            // lbMaxFileSizeMb
            // 
            this.lbMaxFileSizeMb.Location = new System.Drawing.Point(39, 347);
            this.lbMaxFileSizeMb.Name = "lbMaxFileSizeMb";
            this.lbMaxFileSizeMb.Size = new System.Drawing.Size(133, 18);
            this.lbMaxFileSizeMb.TabIndex = 8;
            this.lbMaxFileSizeMb.Text = "日志文件上限 (MB):";
            // 
            // cbeRuntimeLogLevel
            // 
            this.cbeRuntimeLogLevel.Location = new System.Drawing.Point(302, 303);
            this.cbeRuntimeLogLevel.Name = "cbeRuntimeLogLevel";
            this.cbeRuntimeLogLevel.Properties.Buttons.AddRange(new DevExpress.XtraEditors.Controls.EditorButton[] {
            new DevExpress.XtraEditors.Controls.EditorButton(DevExpress.XtraEditors.Controls.ButtonPredefines.Combo)});
            this.cbeRuntimeLogLevel.Properties.TextEditStyle = DevExpress.XtraEditors.Controls.TextEditStyles.DisableTextEditor;
            this.cbeRuntimeLogLevel.Size = new System.Drawing.Size(190, 24);
            this.cbeRuntimeLogLevel.TabIndex = 4;
            // 
            // lbRuntimeLogLevel
            // 
            this.lbRuntimeLogLevel.Location = new System.Drawing.Point(39, 306);
            this.lbRuntimeLogLevel.Name = "lbRuntimeLogLevel";
            this.lbRuntimeLogLevel.Size = new System.Drawing.Size(110, 18);
            this.lbRuntimeLogLevel.TabIndex = 3;
            this.lbRuntimeLogLevel.Text = "运行时日志级别:";
            // 
            // ceRuntimeLogEnabled
            // 
            this.ceRuntimeLogEnabled.EditValue = true;
            this.ceRuntimeLogEnabled.Location = new System.Drawing.Point(39, 36);
            this.ceRuntimeLogEnabled.Name = "ceRuntimeLogEnabled";
            this.ceRuntimeLogEnabled.Properties.Caption = "启用运行时日志";
            this.ceRuntimeLogEnabled.Size = new System.Drawing.Size(200, 22);
            this.ceRuntimeLogEnabled.TabIndex = 2;
            // 
            // cbeTranscriptLogMode
            // 
            this.cbeTranscriptLogMode.Location = new System.Drawing.Point(302, 221);
            this.cbeTranscriptLogMode.Name = "cbeTranscriptLogMode";
            this.cbeTranscriptLogMode.Properties.Buttons.AddRange(new DevExpress.XtraEditors.Controls.EditorButton[] {
            new DevExpress.XtraEditors.Controls.EditorButton(DevExpress.XtraEditors.Controls.ButtonPredefines.Combo)});
            this.cbeTranscriptLogMode.Properties.TextEditStyle = DevExpress.XtraEditors.Controls.TextEditStyles.DisableTextEditor;
            this.cbeTranscriptLogMode.Size = new System.Drawing.Size(190, 24);
            this.cbeTranscriptLogMode.TabIndex = 1;
            // 
            // lbTranscriptLogMode
            // 
            this.lbTranscriptLogMode.Location = new System.Drawing.Point(39, 224);
            this.lbTranscriptLogMode.Name = "lbTranscriptLogMode";
            this.lbTranscriptLogMode.Size = new System.Drawing.Size(110, 18);
            this.lbTranscriptLogMode.TabIndex = 0;
            this.lbTranscriptLogMode.Text = "对话日志详细度:";
            // 
            // grpRuntime
            // 
            this.grpRuntime.Controls.Add(this.ceAutoMemoryRecorder);
            this.grpRuntime.Controls.Add(this.ceAutoCompactEnabled);
            this.grpRuntime.Font = new System.Drawing.Font("Microsoft YaHei UI", 9F, System.Drawing.FontStyle.Bold);
            this.grpRuntime.Location = new System.Drawing.Point(34, 409);
            this.grpRuntime.Name = "grpRuntime";
            this.grpRuntime.Size = new System.Drawing.Size(553, 131);
            this.grpRuntime.TabIndex = 11;
            this.grpRuntime.TabStop = false;
            this.grpRuntime.Text = "运行时参数";
            // 
            // ceAutoMemoryRecorder
            // 
            this.ceAutoMemoryRecorder.EditValue = true;
            this.ceAutoMemoryRecorder.Location = new System.Drawing.Point(34, 38);
            this.ceAutoMemoryRecorder.Name = "ceAutoMemoryRecorder";
            this.ceAutoMemoryRecorder.Properties.Caption = "启用自动记忆归纳系统";
            this.ceAutoMemoryRecorder.Size = new System.Drawing.Size(320, 22);
            this.ceAutoMemoryRecorder.TabIndex = 7;
            // 
            // ceAutoCompactEnabled
            // 
            this.ceAutoCompactEnabled.EditValue = true;
            this.ceAutoCompactEnabled.Location = new System.Drawing.Point(34, 78);
            this.ceAutoCompactEnabled.Name = "ceAutoCompactEnabled";
            this.ceAutoCompactEnabled.Properties.Caption = "启用自动上下文压缩 (AutoCompact)";
            this.ceAutoCompactEnabled.Size = new System.Drawing.Size(320, 22);
            this.ceAutoCompactEnabled.TabIndex = 7;
            // 
            // grpModelParams
            // 
            this.grpModelParams.Controls.Add(this.lbHttpRequestTimeOut);
            this.grpModelParams.Controls.Add(this.lbNativeThresholdKB);
            this.grpModelParams.Controls.Add(this.lbInlineTextThresholdMB);
            this.grpModelParams.Controls.Add(this.lbAttachmentInlineCharBudget);
            this.grpModelParams.Controls.Add(this.lbMaxVisualPages);
            this.grpModelParams.Controls.Add(this.seAttachmentFileCountThreshold);
            this.grpModelParams.Controls.Add(this.ceAutoResizeBigImage);
            this.grpModelParams.Controls.Add(this.lbToolFetchFileThresholdMB);
            this.grpModelParams.Controls.Add(this.seToolFetchFileThresholdMB);
            this.grpModelParams.Controls.Add(this.lbAttachmentFileCountThreshold);
            this.grpModelParams.Controls.Add(this.seInlineTextThresholdMB);
            this.grpModelParams.Controls.Add(this.seAttachmentInlineCharBudget);
            this.grpModelParams.Controls.Add(this.seMaxVisualPages);
            this.grpModelParams.Controls.Add(this.spHttpRequestTimeOut);
            this.grpModelParams.Controls.Add(this.seNativeThresholdKB);
            this.grpModelParams.Font = new System.Drawing.Font("Microsoft YaHei UI", 9F, System.Drawing.FontStyle.Bold);
            this.grpModelParams.Location = new System.Drawing.Point(34, 30);
            this.grpModelParams.Name = "grpModelParams";
            this.grpModelParams.Size = new System.Drawing.Size(553, 349);
            this.grpModelParams.TabIndex = 10;
            this.grpModelParams.TabStop = false;
            this.grpModelParams.Text = "模型调用参数";
            // 
            // lbHttpRequestTimeOut
            // 
            this.lbHttpRequestTimeOut.Location = new System.Drawing.Point(34, 41);
            this.lbHttpRequestTimeOut.Margin = new System.Windows.Forms.Padding(4);
            this.lbHttpRequestTimeOut.Name = "lbHttpRequestTimeOut";
            this.lbHttpRequestTimeOut.Size = new System.Drawing.Size(132, 18);
            this.lbHttpRequestTimeOut.TabIndex = 4;
            this.lbHttpRequestTimeOut.Text = "请求超时时间(秒)：";
            // 
            // lbNativeThresholdKB
            // 
            this.lbNativeThresholdKB.Location = new System.Drawing.Point(34, 119);
            this.lbNativeThresholdKB.Margin = new System.Windows.Forms.Padding(4);
            this.lbNativeThresholdKB.Name = "lbNativeThresholdKB";
            this.lbNativeThresholdKB.Size = new System.Drawing.Size(176, 18);
            this.lbNativeThresholdKB.TabIndex = 4;
            this.lbNativeThresholdKB.Text = "Native文件尺寸上限(KB)：";
            // 
            // lbInlineTextThresholdMB
            // 
            this.lbInlineTextThresholdMB.Location = new System.Drawing.Point(34, 158);
            this.lbInlineTextThresholdMB.Margin = new System.Windows.Forms.Padding(4);
            this.lbInlineTextThresholdMB.Name = "lbInlineTextThresholdMB";
            this.lbInlineTextThresholdMB.Size = new System.Drawing.Size(203, 18);
            this.lbInlineTextThresholdMB.TabIndex = 4;
            this.lbInlineTextThresholdMB.Text = "InlineText文件尺寸上限(MB)：";
            // 
            // seAttachmentFileCountThreshold
            // 
            this.seAttachmentFileCountThreshold.EditValue = new decimal(new int[] {
            5,
            0,
            0,
            0});
            this.seAttachmentFileCountThreshold.Location = new System.Drawing.Point(327, 77);
            this.seAttachmentFileCountThreshold.Margin = new System.Windows.Forms.Padding(4);
            this.seAttachmentFileCountThreshold.Name = "seAttachmentFileCountThreshold";
            this.seAttachmentFileCountThreshold.Properties.Buttons.AddRange(new DevExpress.XtraEditors.Controls.EditorButton[] {
            new DevExpress.XtraEditors.Controls.EditorButton(DevExpress.XtraEditors.Controls.ButtonPredefines.Combo)});
            this.seAttachmentFileCountThreshold.Size = new System.Drawing.Size(187, 24);
            this.seAttachmentFileCountThreshold.TabIndex = 5;
            // 
            // ceAutoResizeBigImage
            // 
            this.ceAutoResizeBigImage.EditValue = true;
            this.ceAutoResizeBigImage.Location = new System.Drawing.Point(34, 306);
            this.ceAutoResizeBigImage.Margin = new System.Windows.Forms.Padding(4);
            this.ceAutoResizeBigImage.Name = "ceAutoResizeBigImage";
            this.ceAutoResizeBigImage.Properties.Caption = "自动缩小大图像到Native文件尺寸内";
            this.ceAutoResizeBigImage.Size = new System.Drawing.Size(280, 22);
            this.ceAutoResizeBigImage.TabIndex = 6;
            // 
            // lbToolFetchFileThresholdMB
            // 
            this.lbToolFetchFileThresholdMB.Location = new System.Drawing.Point(35, 197);
            this.lbToolFetchFileThresholdMB.Margin = new System.Windows.Forms.Padding(4);
            this.lbToolFetchFileThresholdMB.Name = "lbToolFetchFileThresholdMB";
            this.lbToolFetchFileThresholdMB.Size = new System.Drawing.Size(202, 18);
            this.lbToolFetchFileThresholdMB.TabIndex = 4;
            this.lbToolFetchFileThresholdMB.Text = "ToolFetch文件尺寸上限(MB)：";
            // 
            // seToolFetchFileThresholdMB
            // 
            this.seToolFetchFileThresholdMB.EditValue = new decimal(new int[] {
            10,
            0,
            0,
            0});
            this.seToolFetchFileThresholdMB.Location = new System.Drawing.Point(328, 194);
            this.seToolFetchFileThresholdMB.Margin = new System.Windows.Forms.Padding(4);
            this.seToolFetchFileThresholdMB.Name = "seToolFetchFileThresholdMB";
            this.seToolFetchFileThresholdMB.Properties.Buttons.AddRange(new DevExpress.XtraEditors.Controls.EditorButton[] {
            new DevExpress.XtraEditors.Controls.EditorButton(DevExpress.XtraEditors.Controls.ButtonPredefines.Combo)});
            this.seToolFetchFileThresholdMB.Size = new System.Drawing.Size(187, 24);
            this.seToolFetchFileThresholdMB.TabIndex = 5;
            // 
            // lbAttachmentFileCountThreshold
            // 
            this.lbAttachmentFileCountThreshold.Location = new System.Drawing.Point(34, 80);
            this.lbAttachmentFileCountThreshold.Margin = new System.Windows.Forms.Padding(4);
            this.lbAttachmentFileCountThreshold.Name = "lbAttachmentFileCountThreshold";
            this.lbAttachmentFileCountThreshold.Size = new System.Drawing.Size(165, 18);
            this.lbAttachmentFileCountThreshold.TabIndex = 4;
            this.lbAttachmentFileCountThreshold.Text = "参考附件文件个数上限：";
            // 
            // seInlineTextThresholdMB
            // 
            this.seInlineTextThresholdMB.EditValue = new decimal(new int[] {
            10,
            0,
            0,
            0});
            this.seInlineTextThresholdMB.Location = new System.Drawing.Point(327, 155);
            this.seInlineTextThresholdMB.Margin = new System.Windows.Forms.Padding(4);
            this.seInlineTextThresholdMB.Name = "seInlineTextThresholdMB";
            this.seInlineTextThresholdMB.Properties.Buttons.AddRange(new DevExpress.XtraEditors.Controls.EditorButton[] {
            new DevExpress.XtraEditors.Controls.EditorButton(DevExpress.XtraEditors.Controls.ButtonPredefines.Combo)});
            this.seInlineTextThresholdMB.Size = new System.Drawing.Size(187, 24);
            this.seInlineTextThresholdMB.TabIndex = 5;
            //
            // lbAttachmentInlineCharBudget
            //
            this.lbAttachmentInlineCharBudget.Location = new System.Drawing.Point(34, 236);
            this.lbAttachmentInlineCharBudget.Margin = new System.Windows.Forms.Padding(4);
            this.lbAttachmentInlineCharBudget.Name = "lbAttachmentInlineCharBudget";
            this.lbAttachmentInlineCharBudget.Size = new System.Drawing.Size(190, 18);
            this.lbAttachmentInlineCharBudget.TabIndex = 4;
            this.lbAttachmentInlineCharBudget.Text = "附件内联字符预算：";
            //
            // seAttachmentInlineCharBudget
            //
            this.seAttachmentInlineCharBudget.EditValue = new decimal(new int[] {
            12000,
            0,
            0,
            0});
            this.seAttachmentInlineCharBudget.Location = new System.Drawing.Point(327, 233);
            this.seAttachmentInlineCharBudget.Margin = new System.Windows.Forms.Padding(4);
            this.seAttachmentInlineCharBudget.Name = "seAttachmentInlineCharBudget";
            this.seAttachmentInlineCharBudget.Properties.Buttons.AddRange(new DevExpress.XtraEditors.Controls.EditorButton[] {
            new DevExpress.XtraEditors.Controls.EditorButton(DevExpress.XtraEditors.Controls.ButtonPredefines.Combo)});
            this.seAttachmentInlineCharBudget.Properties.MinValue = new decimal(new int[] { 1000, 0, 0, 0 });
            this.seAttachmentInlineCharBudget.Properties.MaxValue = new decimal(new int[] { 200000, 0, 0, 0 });
            this.seAttachmentInlineCharBudget.Size = new System.Drawing.Size(187, 24);
            this.seAttachmentInlineCharBudget.TabIndex = 5;
            //
            // lbMaxVisualPages
            //
            this.lbMaxVisualPages.Location = new System.Drawing.Point(34, 272);
            this.lbMaxVisualPages.Margin = new System.Windows.Forms.Padding(4);
            this.lbMaxVisualPages.Name = "lbMaxVisualPages";
            this.lbMaxVisualPages.Size = new System.Drawing.Size(190, 18);
            this.lbMaxVisualPages.TabIndex = 4;
            this.lbMaxVisualPages.Text = "每轮最大视觉页数：";
            //
            // seMaxVisualPages
            //
            this.seMaxVisualPages.EditValue = new decimal(new int[] {
            5,
            0,
            0,
            0});
            this.seMaxVisualPages.Location = new System.Drawing.Point(327, 269);
            this.seMaxVisualPages.Margin = new System.Windows.Forms.Padding(4);
            this.seMaxVisualPages.Name = "seMaxVisualPages";
            this.seMaxVisualPages.Properties.Buttons.AddRange(new DevExpress.XtraEditors.Controls.EditorButton[] {
            new DevExpress.XtraEditors.Controls.EditorButton(DevExpress.XtraEditors.Controls.ButtonPredefines.Combo)});
            this.seMaxVisualPages.Properties.MinValue = new decimal(new int[] { 0, 0, 0, 0 });
            this.seMaxVisualPages.Properties.MaxValue = new decimal(new int[] { 50, 0, 0, 0 });
            this.seMaxVisualPages.Size = new System.Drawing.Size(187, 24);
            this.seMaxVisualPages.TabIndex = 5;
            // 
            // spHttpRequestTimeOut
            // 
            this.spHttpRequestTimeOut.EditValue = new decimal(new int[] {
            600,
            0,
            0,
            0});
            this.spHttpRequestTimeOut.Location = new System.Drawing.Point(327, 38);
            this.spHttpRequestTimeOut.Margin = new System.Windows.Forms.Padding(4);
            this.spHttpRequestTimeOut.MenuManager = this.barMan;
            this.spHttpRequestTimeOut.Name = "spHttpRequestTimeOut";
            this.spHttpRequestTimeOut.Properties.Buttons.AddRange(new DevExpress.XtraEditors.Controls.EditorButton[] {
            new DevExpress.XtraEditors.Controls.EditorButton(DevExpress.XtraEditors.Controls.ButtonPredefines.Combo)});
            this.spHttpRequestTimeOut.Size = new System.Drawing.Size(186, 24);
            this.spHttpRequestTimeOut.TabIndex = 5;
            // 
            // seNativeThresholdKB
            // 
            this.seNativeThresholdKB.EditValue = new decimal(new int[] {
            1024,
            0,
            0,
            0});
            this.seNativeThresholdKB.Location = new System.Drawing.Point(327, 116);
            this.seNativeThresholdKB.Margin = new System.Windows.Forms.Padding(4);
            this.seNativeThresholdKB.Name = "seNativeThresholdKB";
            this.seNativeThresholdKB.Properties.Buttons.AddRange(new DevExpress.XtraEditors.Controls.EditorButton[] {
            new DevExpress.XtraEditors.Controls.EditorButton(DevExpress.XtraEditors.Controls.ButtonPredefines.Combo)});
            this.seNativeThresholdKB.Size = new System.Drawing.Size(187, 24);
            this.seNativeThresholdKB.TabIndex = 5;
            // 
            // fmConfig
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(8F, 15F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(1171, 670);
            this.Controls.Add(this.xtcMain);
            this.Controls.Add(this.barDockControlLeft);
            this.Controls.Add(this.barDockControlRight);
            this.Controls.Add(this.barDockControlBottom);
            this.Controls.Add(this.barDockControlTop);
            this.Margin = new System.Windows.Forms.Padding(4);
            this.Name = "fmConfig";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;
            this.Text = "参数设置";
            ((System.ComponentModel.ISupportInitialize)(this.xtcMain)).EndInit();
            this.xtcMain.ResumeLayout(false);
            this.xtpModels.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)(this.gcModels)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.gvModels)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.barMan)).EndInit();
            this.xtpTools.ResumeLayout(false);
            this.spcTools.Panel1.ResumeLayout(false);
            this.spcTools.Panel2.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)(this.spcTools)).EndInit();
            this.spcTools.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)(this.gcToolsGroup)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.gvToolsGroup)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.gcTools)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.gvTools)).EndInit();
            this.xtpMcp.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)(this.gcMcpServer)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.gvMcpServer)).EndInit();
            this.xtpSkills.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)(this.gcSkills)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.gvSkills)).EndInit();
            this.xtpMemoryMan.ResumeLayout(false);
            this.spcMemoryMan.Panel1.ResumeLayout(false);
            this.spcMemoryMan.Panel2.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)(this.spcMemoryMan)).EndInit();
            this.spcMemoryMan.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)(this.tlMemFileTree)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.xtcMemMarkDown)).EndInit();
            this.xtcMemMarkDown.ResumeLayout(false);
            this.xtpMemWebPreview.ResumeLayout(false);
            this.xtpMemMarkDown.ResumeLayout(false);
            this.xtpGeneral.ResumeLayout(false);
            this.grpLogging.ResumeLayout(false);
            this.grpLogging.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)(this.seTranscriptMaxContentLength.Properties)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.ceToolCallLogEnabled.Properties)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.ceTranscriptIncludeHostContext.Properties)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.ceTranscriptIncludeToolCalls.Properties)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.seRetainedFileCount.Properties)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.seMaxFileSizeMb.Properties)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.cbeRuntimeLogLevel.Properties)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.ceRuntimeLogEnabled.Properties)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.cbeTranscriptLogMode.Properties)).EndInit();
            this.grpRuntime.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)(this.ceAutoMemoryRecorder.Properties)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.ceAutoCompactEnabled.Properties)).EndInit();
            this.grpModelParams.ResumeLayout(false);
            this.grpModelParams.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)(this.seAttachmentFileCountThreshold.Properties)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.ceAutoResizeBigImage.Properties)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.seToolFetchFileThresholdMB.Properties)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.seInlineTextThresholdMB.Properties)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.seAttachmentInlineCharBudget.Properties)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.seMaxVisualPages.Properties)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.spHttpRequestTimeOut.Properties)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.seNativeThresholdKB.Properties)).EndInit();
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private DevExpress.XtraTab.XtraTabControl xtcMain;
        private DevExpress.XtraTab.XtraTabPage xtpGeneral;
        private DevExpress.XtraTab.XtraTabPage xtpModels;
        private DevExpress.XtraTab.XtraTabPage xtpTools;
        private DevExpress.XtraTab.XtraTabPage xtpMcp;
        private DevExpress.XtraBars.StandaloneBarDockControl sbdcModelTop;
        private DevExpress.XtraBars.BarManager barMan;
        private DevExpress.XtraBars.Bar barModelTop;
        private DevExpress.XtraBars.BarDockControl barDockControlTop;
        private DevExpress.XtraBars.BarDockControl barDockControlBottom;
        private DevExpress.XtraBars.BarDockControl barDockControlLeft;
        private DevExpress.XtraBars.BarDockControl barDockControlRight;
        private DevExpress.XtraBars.BarButtonItem btnAddModel;
        private DevExpress.XtraBars.BarButtonItem btnDeleteModel;
        private DevExpress.XtraBars.BarButtonItem btnExportListToJson;
        private DevExpress.XtraBars.BarButtonItem btnImportList;
        private DevExpress.XtraGrid.GridControl gcModels;
        private DevExpress.XtraGrid.Views.Grid.GridView gvModels;
        private DevExpress.XtraGrid.Columns.GridColumn colId;
        private DevExpress.XtraGrid.Columns.GridColumn colName;
        private DevExpress.XtraGrid.Columns.GridColumn colIsEnabled;
        private DevExpress.XtraGrid.Columns.GridColumn colProviderKind;
        private DevExpress.XtraGrid.Columns.GridColumn colEndpoint;
        private DevExpress.XtraGrid.Columns.GridColumn colApiKey;
        private DevExpress.XtraGrid.Columns.GridColumn colModelId;
        private DevExpress.XtraGrid.Columns.GridColumn colSupportsImageInput;
        private DevExpress.XtraBars.BarButtonItem btnShowPreview;
        private DevExpress.XtraEditors.LabelControl lbHttpRequestTimeOut;
        private DevExpress.XtraEditors.SpinEdit spHttpRequestTimeOut;
        private DevExpress.XtraEditors.LabelControl lbNativeThresholdKB;
        private DevExpress.XtraEditors.SpinEdit seAttachmentFileCountThreshold;
        private DevExpress.XtraEditors.SpinEdit seInlineTextThresholdMB;
        private DevExpress.XtraEditors.SpinEdit seNativeThresholdKB;
        private DevExpress.XtraEditors.LabelControl lbAttachmentFileCountThreshold;
        private DevExpress.XtraEditors.LabelControl lbInlineTextThresholdMB;
        private DevExpress.XtraEditors.LabelControl lbAttachmentInlineCharBudget;
        private DevExpress.XtraEditors.SpinEdit seAttachmentInlineCharBudget;
        private DevExpress.XtraEditors.LabelControl lbMaxVisualPages;
        private DevExpress.XtraEditors.SpinEdit seMaxVisualPages;
        private DevExpress.XtraEditors.SpinEdit seToolFetchFileThresholdMB;
        private DevExpress.XtraEditors.LabelControl lbToolFetchFileThresholdMB;
        private DevExpress.XtraEditors.CheckEdit ceAutoResizeBigImage;
        private System.Windows.Forms.SplitContainer spcTools;
        private DevExpress.XtraGrid.GridControl gcToolsGroup;
        private DevExpress.XtraGrid.Views.Grid.GridView gvToolsGroup;
        private DevExpress.XtraGrid.Columns.GridColumn colToolsGroupName;
        private DevExpress.XtraGrid.Columns.GridColumn colToolsGroupEnable;
        private DevExpress.XtraGrid.GridControl gcTools;
        private DevExpress.XtraGrid.Views.Grid.GridView gvTools;
        private DevExpress.XtraGrid.Columns.GridColumn colToolName;
        private DevExpress.XtraGrid.Columns.GridColumn colDescription;
        private DevExpress.XtraGrid.Columns.GridColumn colEnableTool;
        private DevExpress.XtraGrid.Columns.GridColumn colNeedConfirm;
        private DevExpress.XtraGrid.Columns.GridColumn colTestTool;
        private DevExpress.XtraBars.BarButtonItem btnEditModel;
        private DevExpress.XtraBars.Bar barMcpTop;
        private DevExpress.XtraBars.BarButtonItem btnNewMcp;
        private DevExpress.XtraBars.BarButtonItem btnEditMcp;
        private DevExpress.XtraBars.BarButtonItem btnDelMcp;
        private DevExpress.XtraBars.BarButtonItem btnShowDesc;
        private DevExpress.XtraBars.BarButtonItem btnImportMcp;
        private DevExpress.XtraBars.BarButtonItem btnExportMcp;
        private DevExpress.XtraBars.StandaloneBarDockControl sbdcMcpTop;
        private DevExpress.XtraGrid.GridControl gcMcpServer;
        private DevExpress.XtraGrid.Views.Grid.GridView gvMcpServer;
        private DevExpress.XtraGrid.Columns.GridColumn colMcpName;
        private DevExpress.XtraGrid.Columns.GridColumn colEnableMcp;
        private DevExpress.XtraGrid.Columns.GridColumn colMcpDesc;
        private DevExpress.XtraGrid.Columns.GridColumn colMcpId;
        private DevExpress.XtraTab.XtraTabPage xtpSkills;
        private DevExpress.XtraBars.Bar barSkillTop;
        private DevExpress.XtraBars.BarButtonItem btnNewSkill;
        private DevExpress.XtraBars.BarButtonItem btnEditSkill;
        private DevExpress.XtraBars.BarButtonItem btnDeleteSkill;
        private DevExpress.XtraBars.BarButtonItem btnExportSkills;
        private DevExpress.XtraBars.BarButtonItem btnImportSkills;
        private DevExpress.XtraBars.StandaloneBarDockControl sbdcSkillTop;
        private DevExpress.XtraGrid.GridControl gcSkills;
        private DevExpress.XtraGrid.Views.Grid.GridView gvSkills;
        private DevExpress.XtraGrid.Columns.GridColumn colSkillId;
        private DevExpress.XtraGrid.Columns.GridColumn colSkillName;
        private DevExpress.XtraGrid.Columns.GridColumn colSkillDesc;
        private DevExpress.XtraGrid.Columns.GridColumn colSkillEnabled;
        private DevExpress.XtraTab.XtraTabPage xtpMemoryMan;
        private System.Windows.Forms.SplitContainer spcMemoryMan;
        private DevExpress.XtraTreeList.TreeList tlMemFileTree;
        private DevExpress.XtraBars.StandaloneBarDockControl sbdcMemBottom;
        private DevExpress.XtraBars.Bar barMemBottom;
        private DevExpress.XtraBars.BarButtonItem btnSaveMem;
        private DevExpress.XtraBars.BarStaticItem bsiMemInfo;
        private DevExpress.XtraTab.XtraTabControl xtcMemMarkDown;
        private DevExpress.XtraTab.XtraTabPage xtpMemWebPreview;
        private System.Windows.Forms.WebBrowser webPreview;
        private DevExpress.XtraTab.XtraTabPage xtpMemMarkDown;
        private DevExpress.XtraRichEdit.RichEditControl txtMarkDown;
        // ---- 系统设置新增控件 (2026-08-03) ----
        private System.Windows.Forms.GroupBox grpModelParams;
        private System.Windows.Forms.GroupBox grpRuntime;
        private DevExpress.XtraEditors.CheckEdit ceAutoCompactEnabled;
        private System.Windows.Forms.GroupBox grpLogging;
        private DevExpress.XtraEditors.LabelControl lbTranscriptLogMode;
        private DevExpress.XtraEditors.ComboBoxEdit cbeTranscriptLogMode;
        private DevExpress.XtraEditors.CheckEdit ceRuntimeLogEnabled;
        private DevExpress.XtraEditors.LabelControl lbRuntimeLogLevel;
        private DevExpress.XtraEditors.ComboBoxEdit cbeRuntimeLogLevel;
        private DevExpress.XtraEditors.LabelControl lbMaxFileSizeMb;
        private DevExpress.XtraEditors.SpinEdit seMaxFileSizeMb;
        private DevExpress.XtraEditors.CheckEdit ceTranscriptIncludeToolCalls;
        private DevExpress.XtraEditors.CheckEdit ceTranscriptIncludeHostContext;
        private DevExpress.XtraEditors.CheckEdit ceToolCallLogEnabled;
        private DevExpress.XtraEditors.LabelControl lbTranscriptMaxContentLength;
        private DevExpress.XtraEditors.SpinEdit seTranscriptMaxContentLength;
        private DevExpress.XtraEditors.SpinEdit seRetainedFileCount;
        private DevExpress.XtraEditors.LabelControl lbRetainedFileCount;
        private DevExpress.XtraEditors.CheckEdit ceAutoMemoryRecorder;
        private DevExpress.XtraBars.Bar barGeneralBottom;
        private DevExpress.XtraBars.StandaloneBarDockControl sbdcSysConfigBottom;
        private DevExpress.XtraBars.BarButtonItem btnSaveGenenalConfig;
        private DevExpress.XtraBars.BarButtonItem btnCancelSaveGeneralConfig;
    }
}