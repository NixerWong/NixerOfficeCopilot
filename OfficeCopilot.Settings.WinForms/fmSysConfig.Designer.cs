namespace OfficeCopilot.Settings.WinForms
{
    partial class fmSysConfig
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
            this.components = new System.ComponentModel.Container();
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(fmSysConfig));
            this.barMan = new DevExpress.XtraBars.BarManager(this.components);
            this.barGeneralBottom = new DevExpress.XtraBars.Bar();
            this.btnSaveGenenalConfig = new DevExpress.XtraBars.BarButtonItem();
            this.btnCancelSaveGeneralConfig = new DevExpress.XtraBars.BarButtonItem();
            this.btnEditAskModeSysPrompt = new DevExpress.XtraBars.BarButtonItem();
            this.btnEditAgentModeSysPrompt = new DevExpress.XtraBars.BarButtonItem();
            this.sbdcSysConfigBottom = new DevExpress.XtraBars.StandaloneBarDockControl();
            this.barDockControlTop = new DevExpress.XtraBars.BarDockControl();
            this.barDockControlBottom = new DevExpress.XtraBars.BarDockControl();
            this.barDockControlLeft = new DevExpress.XtraBars.BarDockControl();
            this.barDockControlRight = new DevExpress.XtraBars.BarDockControl();
            this.btnAddModel = new DevExpress.XtraBars.BarButtonItem();
            this.btnDeleteModel = new DevExpress.XtraBars.BarButtonItem();
            this.btnExportListToJson = new DevExpress.XtraBars.BarButtonItem();
            this.btnImportList = new DevExpress.XtraBars.BarButtonItem();
            this.btnShowPreview = new DevExpress.XtraBars.BarButtonItem();
            this.btnEditModel = new DevExpress.XtraBars.BarButtonItem();
            this.btnNewMcp = new DevExpress.XtraBars.BarButtonItem();
            this.btnEditMcp = new DevExpress.XtraBars.BarButtonItem();
            this.btnDelMcp = new DevExpress.XtraBars.BarButtonItem();
            this.btnShowDesc = new DevExpress.XtraBars.BarButtonItem();
            this.btnImportMcp = new DevExpress.XtraBars.BarButtonItem();
            this.btnExportMcp = new DevExpress.XtraBars.BarButtonItem();
            this.btnNewSkill = new DevExpress.XtraBars.BarButtonItem();
            this.btnEditSkill = new DevExpress.XtraBars.BarButtonItem();
            this.btnDeleteSkill = new DevExpress.XtraBars.BarButtonItem();
            this.btnExportSkills = new DevExpress.XtraBars.BarButtonItem();
            this.btnImportSkills = new DevExpress.XtraBars.BarButtonItem();
            this.btnSaveMem = new DevExpress.XtraBars.BarButtonItem();
            this.bsiMemInfo = new DevExpress.XtraBars.BarStaticItem();
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
            this.cbeTrustMode = new DevExpress.XtraEditors.ComboBoxEdit();
            this.cbeTranscriptLogMode = new DevExpress.XtraEditors.ComboBoxEdit();
            this.lbTrustMode = new DevExpress.XtraEditors.LabelControl();
            this.lbTranscriptLogMode = new DevExpress.XtraEditors.LabelControl();
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
            this.grpRuntime = new System.Windows.Forms.GroupBox();
            this.ceAutoMemoryRecorder = new DevExpress.XtraEditors.CheckEdit();
            this.ceAutoCompactEnabled = new DevExpress.XtraEditors.CheckEdit();
            this.cbeLanguage = new DevExpress.XtraEditors.ComboBoxEdit();
            this.grpLanguage = new System.Windows.Forms.GroupBox();
            this.lbLanguage = new DevExpress.XtraEditors.LabelControl();
            ((System.ComponentModel.ISupportInitialize)(this.barMan)).BeginInit();
            this.grpLogging.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.seTranscriptMaxContentLength.Properties)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.ceToolCallLogEnabled.Properties)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.ceTranscriptIncludeHostContext.Properties)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.ceTranscriptIncludeToolCalls.Properties)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.seRetainedFileCount.Properties)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.seMaxFileSizeMb.Properties)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.cbeRuntimeLogLevel.Properties)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.ceRuntimeLogEnabled.Properties)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.cbeTrustMode.Properties)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.cbeTranscriptLogMode.Properties)).BeginInit();
            this.grpModelParams.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.seAttachmentFileCountThreshold.Properties)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.ceAutoResizeBigImage.Properties)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.seToolFetchFileThresholdMB.Properties)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.seInlineTextThresholdMB.Properties)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.seAttachmentInlineCharBudget.Properties)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.seMaxVisualPages.Properties)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.spHttpRequestTimeOut.Properties)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.seNativeThresholdKB.Properties)).BeginInit();
            this.grpRuntime.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.ceAutoMemoryRecorder.Properties)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.ceAutoCompactEnabled.Properties)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.cbeLanguage.Properties)).BeginInit();
            this.grpLanguage.SuspendLayout();
            this.SuspendLayout();
            // 
            // barMan
            // 
            this.barMan.Bars.AddRange(new DevExpress.XtraBars.Bar[] {
            this.barGeneralBottom});
            this.barMan.DockControls.Add(this.barDockControlTop);
            this.barMan.DockControls.Add(this.barDockControlBottom);
            this.barMan.DockControls.Add(this.barDockControlLeft);
            this.barMan.DockControls.Add(this.barDockControlRight);
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
            this.btnCancelSaveGeneralConfig,
            this.btnEditAskModeSysPrompt,
            this.btnEditAgentModeSysPrompt});
            this.barMan.MaxItemId = 23;
            // 
            // barGeneralBottom
            // 
            this.barGeneralBottom.BarName = "barGeneralBottom";
            this.barGeneralBottom.DockCol = 0;
            this.barGeneralBottom.DockRow = 0;
            this.barGeneralBottom.DockStyle = DevExpress.XtraBars.BarDockStyle.Standalone;
            this.barGeneralBottom.LinksPersistInfo.AddRange(new DevExpress.XtraBars.LinkPersistInfo[] {
            new DevExpress.XtraBars.LinkPersistInfo(DevExpress.XtraBars.BarLinkUserDefines.PaintStyle, this.btnSaveGenenalConfig, DevExpress.XtraBars.BarItemPaintStyle.CaptionGlyph),
            new DevExpress.XtraBars.LinkPersistInfo(DevExpress.XtraBars.BarLinkUserDefines.PaintStyle, this.btnCancelSaveGeneralConfig, "", true, true, true, 0, null, DevExpress.XtraBars.BarItemPaintStyle.CaptionGlyph),
            new DevExpress.XtraBars.LinkPersistInfo(DevExpress.XtraBars.BarLinkUserDefines.PaintStyle, this.btnEditAskModeSysPrompt, DevExpress.XtraBars.BarItemPaintStyle.CaptionGlyph),
            new DevExpress.XtraBars.LinkPersistInfo(DevExpress.XtraBars.BarLinkUserDefines.PaintStyle, this.btnEditAgentModeSysPrompt, "", true, true, true, 0, null, DevExpress.XtraBars.BarItemPaintStyle.CaptionGlyph)});
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
            // btnEditAskModeSysPrompt
            // 
            this.btnEditAskModeSysPrompt.Caption = "编辑Ask模式系统提示词";
            this.btnEditAskModeSysPrompt.Id = 21;
            this.btnEditAskModeSysPrompt.ImageOptions.Image = global::OfficeCopilot.Settings.WinForms.Properties.Resources.scripts_16x161;
            this.btnEditAskModeSysPrompt.ImageOptions.LargeImage = global::OfficeCopilot.Settings.WinForms.Properties.Resources.scripts_32x32;
            this.btnEditAskModeSysPrompt.Name = "btnEditAskModeSysPrompt";
            // 
            // btnEditAgentModeSysPrompt
            // 
            this.btnEditAgentModeSysPrompt.Caption = "编辑Agent模式系统提示词";
            this.btnEditAgentModeSysPrompt.Id = 22;
            this.btnEditAgentModeSysPrompt.ImageOptions.Image = global::OfficeCopilot.Settings.WinForms.Properties.Resources.scripts_16x161;
            this.btnEditAgentModeSysPrompt.Name = "btnEditAgentModeSysPrompt";
            // 
            // sbdcSysConfigBottom
            // 
            this.sbdcSysConfigBottom.CausesValidation = false;
            this.sbdcSysConfigBottom.Dock = System.Windows.Forms.DockStyle.Bottom;
            this.sbdcSysConfigBottom.Location = new System.Drawing.Point(0, 440);
            this.sbdcSysConfigBottom.Manager = this.barMan;
            this.sbdcSysConfigBottom.Margin = new System.Windows.Forms.Padding(2);
            this.sbdcSysConfigBottom.Name = "sbdcSysConfigBottom";
            this.sbdcSysConfigBottom.Size = new System.Drawing.Size(755, 32);
            this.sbdcSysConfigBottom.Text = "standaloneBarDockControl1";
            // 
            // barDockControlTop
            // 
            this.barDockControlTop.CausesValidation = false;
            this.barDockControlTop.Dock = System.Windows.Forms.DockStyle.Top;
            this.barDockControlTop.Location = new System.Drawing.Point(0, 0);
            this.barDockControlTop.Manager = this.barMan;
            this.barDockControlTop.Size = new System.Drawing.Size(755, 0);
            // 
            // barDockControlBottom
            // 
            this.barDockControlBottom.CausesValidation = false;
            this.barDockControlBottom.Dock = System.Windows.Forms.DockStyle.Bottom;
            this.barDockControlBottom.Location = new System.Drawing.Point(0, 472);
            this.barDockControlBottom.Manager = this.barMan;
            this.barDockControlBottom.Size = new System.Drawing.Size(755, 0);
            // 
            // barDockControlLeft
            // 
            this.barDockControlLeft.CausesValidation = false;
            this.barDockControlLeft.Dock = System.Windows.Forms.DockStyle.Left;
            this.barDockControlLeft.Location = new System.Drawing.Point(0, 0);
            this.barDockControlLeft.Manager = this.barMan;
            this.barDockControlLeft.Size = new System.Drawing.Size(0, 472);
            // 
            // barDockControlRight
            // 
            this.barDockControlRight.CausesValidation = false;
            this.barDockControlRight.Dock = System.Windows.Forms.DockStyle.Right;
            this.barDockControlRight.Location = new System.Drawing.Point(755, 0);
            this.barDockControlRight.Manager = this.barMan;
            this.barDockControlRight.Size = new System.Drawing.Size(0, 472);
            // 
            // btnAddModel
            // 
            this.btnAddModel.Caption = "新增模型";
            this.btnAddModel.Id = 0;
            this.btnAddModel.ImageOptions.Image = ((System.Drawing.Image)(resources.GetObject("btnAddModel.ImageOptions.Image")));
            this.btnAddModel.ImageOptions.LargeImage = ((System.Drawing.Image)(resources.GetObject("btnAddModel.ImageOptions.LargeImage")));
            this.btnAddModel.Name = "btnAddModel";
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
            // btnEditModel
            // 
            this.btnEditModel.Caption = "编辑选中模型";
            this.btnEditModel.Id = 5;
            this.btnEditModel.ImageOptions.Image = global::OfficeCopilot.Settings.WinForms.Properties.Resources.loadpagesetup_16x16;
            this.btnEditModel.ImageOptions.LargeImage = global::OfficeCopilot.Settings.WinForms.Properties.Resources.loadpagesetup_32x32;
            this.btnEditModel.Name = "btnEditModel";
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
            // btnShowDesc
            // 
            this.btnShowDesc.Caption = "查看";
            this.btnShowDesc.Id = 9;
            this.btnShowDesc.ImageOptions.Image = global::OfficeCopilot.Settings.WinForms.Properties.Resources.show_16x16;
            this.btnShowDesc.ImageOptions.LargeImage = global::OfficeCopilot.Settings.WinForms.Properties.Resources.show_32x32;
            this.btnShowDesc.Name = "btnShowDesc";
            // 
            // btnImportMcp
            // 
            this.btnImportMcp.Caption = "导入MCP配置";
            this.btnImportMcp.Id = 10;
            this.btnImportMcp.ImageOptions.Image = global::OfficeCopilot.Settings.WinForms.Properties.Resources.inbox_16x16;
            this.btnImportMcp.ImageOptions.LargeImage = global::OfficeCopilot.Settings.WinForms.Properties.Resources.inbox_32x32;
            this.btnImportMcp.Name = "btnImportMcp";
            // 
            // btnExportMcp
            // 
            this.btnExportMcp.Caption = "导出列表";
            this.btnExportMcp.Id = 11;
            this.btnExportMcp.ImageOptions.Image = global::OfficeCopilot.Settings.WinForms.Properties.Resources.export_16x16;
            this.btnExportMcp.ImageOptions.LargeImage = global::OfficeCopilot.Settings.WinForms.Properties.Resources.export_32x32;
            this.btnExportMcp.Name = "btnExportMcp";
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
            this.grpLogging.Location = new System.Drawing.Point(404, 14);
            this.grpLogging.Margin = new System.Windows.Forms.Padding(2);
            this.grpLogging.Name = "grpLogging";
            this.grpLogging.Padding = new System.Windows.Forms.Padding(2);
            this.grpLogging.Size = new System.Drawing.Size(333, 341);
            this.grpLogging.TabIndex = 14;
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
            this.seTranscriptMaxContentLength.Location = new System.Drawing.Point(221, 199);
            this.seTranscriptMaxContentLength.Margin = new System.Windows.Forms.Padding(2);
            this.seTranscriptMaxContentLength.Name = "seTranscriptMaxContentLength";
            this.seTranscriptMaxContentLength.Properties.Buttons.AddRange(new DevExpress.XtraEditors.Controls.EditorButton[] {
            new DevExpress.XtraEditors.Controls.EditorButton(DevExpress.XtraEditors.Controls.ButtonPredefines.Combo)});
            this.seTranscriptMaxContentLength.Properties.MaxValue = new decimal(new int[] {
            1000000,
            0,
            0,
            0});
            this.seTranscriptMaxContentLength.Size = new System.Drawing.Size(85, 20);
            this.seTranscriptMaxContentLength.TabIndex = 17;
            // 
            // lbTranscriptMaxContentLength
            // 
            this.lbTranscriptMaxContentLength.Location = new System.Drawing.Point(29, 202);
            this.lbTranscriptMaxContentLength.Margin = new System.Windows.Forms.Padding(2);
            this.lbTranscriptMaxContentLength.Name = "lbTranscriptMaxContentLength";
            this.lbTranscriptMaxContentLength.Size = new System.Drawing.Size(157, 14);
            this.lbTranscriptMaxContentLength.TabIndex = 16;
            this.lbTranscriptMaxContentLength.Text = "对话日志长度限制 (0不限制):";
            // 
            // ceToolCallLogEnabled
            // 
            this.ceToolCallLogEnabled.EditValue = true;
            this.ceToolCallLogEnabled.Location = new System.Drawing.Point(29, 65);
            this.ceToolCallLogEnabled.Margin = new System.Windows.Forms.Padding(2);
            this.ceToolCallLogEnabled.Name = "ceToolCallLogEnabled";
            this.ceToolCallLogEnabled.Properties.Caption = "启用工具调用日志";
            this.ceToolCallLogEnabled.Size = new System.Drawing.Size(128, 19);
            this.ceToolCallLogEnabled.TabIndex = 15;
            // 
            // ceTranscriptIncludeHostContext
            // 
            this.ceTranscriptIncludeHostContext.EditValue = true;
            this.ceTranscriptIncludeHostContext.Location = new System.Drawing.Point(29, 134);
            this.ceTranscriptIncludeHostContext.Margin = new System.Windows.Forms.Padding(2);
            this.ceTranscriptIncludeHostContext.Name = "ceTranscriptIncludeHostContext";
            this.ceTranscriptIncludeHostContext.Properties.Caption = "对话日志包含宿主上下文";
            this.ceTranscriptIncludeHostContext.Size = new System.Drawing.Size(164, 19);
            this.ceTranscriptIncludeHostContext.TabIndex = 13;
            // 
            // ceTranscriptIncludeToolCalls
            // 
            this.ceTranscriptIncludeToolCalls.EditValue = true;
            this.ceTranscriptIncludeToolCalls.Location = new System.Drawing.Point(29, 99);
            this.ceTranscriptIncludeToolCalls.Margin = new System.Windows.Forms.Padding(2);
            this.ceTranscriptIncludeToolCalls.Name = "ceTranscriptIncludeToolCalls";
            this.ceTranscriptIncludeToolCalls.Properties.Caption = "对话日志包含工具调用";
            this.ceTranscriptIncludeToolCalls.Size = new System.Drawing.Size(150, 19);
            this.ceTranscriptIncludeToolCalls.TabIndex = 12;
            // 
            // seRetainedFileCount
            // 
            this.seRetainedFileCount.EditValue = new decimal(new int[] {
            10,
            0,
            0,
            0});
            this.seRetainedFileCount.Location = new System.Drawing.Point(221, 299);
            this.seRetainedFileCount.Margin = new System.Windows.Forms.Padding(2);
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
            this.seRetainedFileCount.Size = new System.Drawing.Size(85, 20);
            this.seRetainedFileCount.TabIndex = 11;
            // 
            // lbRetainedFileCount
            // 
            this.lbRetainedFileCount.Location = new System.Drawing.Point(29, 302);
            this.lbRetainedFileCount.Margin = new System.Windows.Forms.Padding(2);
            this.lbRetainedFileCount.Name = "lbRetainedFileCount";
            this.lbRetainedFileCount.Size = new System.Drawing.Size(100, 14);
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
            this.seMaxFileSizeMb.Location = new System.Drawing.Point(221, 265);
            this.seMaxFileSizeMb.Margin = new System.Windows.Forms.Padding(2);
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
            this.seMaxFileSizeMb.Size = new System.Drawing.Size(85, 20);
            this.seMaxFileSizeMb.TabIndex = 9;
            // 
            // lbMaxFileSizeMb
            // 
            this.lbMaxFileSizeMb.Location = new System.Drawing.Point(29, 268);
            this.lbMaxFileSizeMb.Margin = new System.Windows.Forms.Padding(2);
            this.lbMaxFileSizeMb.Name = "lbMaxFileSizeMb";
            this.lbMaxFileSizeMb.Size = new System.Drawing.Size(106, 14);
            this.lbMaxFileSizeMb.TabIndex = 8;
            this.lbMaxFileSizeMb.Text = "日志文件上限 (MB):";
            // 
            // cbeRuntimeLogLevel
            // 
            this.cbeRuntimeLogLevel.Location = new System.Drawing.Point(221, 232);
            this.cbeRuntimeLogLevel.Margin = new System.Windows.Forms.Padding(2);
            this.cbeRuntimeLogLevel.Name = "cbeRuntimeLogLevel";
            this.cbeRuntimeLogLevel.Properties.Buttons.AddRange(new DevExpress.XtraEditors.Controls.EditorButton[] {
            new DevExpress.XtraEditors.Controls.EditorButton(DevExpress.XtraEditors.Controls.ButtonPredefines.Combo)});
            this.cbeRuntimeLogLevel.Properties.TextEditStyle = DevExpress.XtraEditors.Controls.TextEditStyles.DisableTextEditor;
            this.cbeRuntimeLogLevel.Size = new System.Drawing.Size(85, 20);
            this.cbeRuntimeLogLevel.TabIndex = 4;
            // 
            // lbRuntimeLogLevel
            // 
            this.lbRuntimeLogLevel.Location = new System.Drawing.Point(29, 235);
            this.lbRuntimeLogLevel.Margin = new System.Windows.Forms.Padding(2);
            this.lbRuntimeLogLevel.Name = "lbRuntimeLogLevel";
            this.lbRuntimeLogLevel.Size = new System.Drawing.Size(88, 14);
            this.lbRuntimeLogLevel.TabIndex = 3;
            this.lbRuntimeLogLevel.Text = "运行时日志级别:";
            // 
            // ceRuntimeLogEnabled
            // 
            this.ceRuntimeLogEnabled.EditValue = true;
            this.ceRuntimeLogEnabled.Location = new System.Drawing.Point(29, 29);
            this.ceRuntimeLogEnabled.Margin = new System.Windows.Forms.Padding(2);
            this.ceRuntimeLogEnabled.Name = "ceRuntimeLogEnabled";
            this.ceRuntimeLogEnabled.Properties.Caption = "启用运行时日志";
            this.ceRuntimeLogEnabled.Size = new System.Drawing.Size(150, 19);
            this.ceRuntimeLogEnabled.TabIndex = 2;
            // 
            // cbeTrustMode
            // 
            this.cbeTrustMode.Location = new System.Drawing.Point(243, 87);
            this.cbeTrustMode.Margin = new System.Windows.Forms.Padding(2);
            this.cbeTrustMode.Name = "cbeTrustMode";
            this.cbeTrustMode.Properties.Buttons.AddRange(new DevExpress.XtraEditors.Controls.EditorButton[] {
            new DevExpress.XtraEditors.Controls.EditorButton(DevExpress.XtraEditors.Controls.ButtonPredefines.Combo)});
            this.cbeTrustMode.Properties.TextEditStyle = DevExpress.XtraEditors.Controls.TextEditStyles.DisableTextEditor;
            this.cbeTrustMode.Size = new System.Drawing.Size(84, 20);
            this.cbeTrustMode.TabIndex = 1;
            // 
            // cbeTranscriptLogMode
            // 
            this.cbeTranscriptLogMode.Location = new System.Drawing.Point(221, 166);
            this.cbeTranscriptLogMode.Margin = new System.Windows.Forms.Padding(2);
            this.cbeTranscriptLogMode.Name = "cbeTranscriptLogMode";
            this.cbeTranscriptLogMode.Properties.Buttons.AddRange(new DevExpress.XtraEditors.Controls.EditorButton[] {
            new DevExpress.XtraEditors.Controls.EditorButton(DevExpress.XtraEditors.Controls.ButtonPredefines.Combo)});
            this.cbeTranscriptLogMode.Properties.TextEditStyle = DevExpress.XtraEditors.Controls.TextEditStyles.DisableTextEditor;
            this.cbeTranscriptLogMode.Size = new System.Drawing.Size(85, 20);
            this.cbeTranscriptLogMode.TabIndex = 1;
            // 
            // lbTrustMode
            // 
            this.lbTrustMode.Location = new System.Drawing.Point(26, 90);
            this.lbTrustMode.Margin = new System.Windows.Forms.Padding(2);
            this.lbTrustMode.Name = "lbTrustMode";
            this.lbTrustMode.Size = new System.Drawing.Size(100, 14);
            this.lbTrustMode.TabIndex = 0;
            this.lbTrustMode.Text = "工具执行权限模式:";
            // 
            // lbTranscriptLogMode
            // 
            this.lbTranscriptLogMode.Location = new System.Drawing.Point(29, 169);
            this.lbTranscriptLogMode.Margin = new System.Windows.Forms.Padding(2);
            this.lbTranscriptLogMode.Name = "lbTranscriptLogMode";
            this.lbTranscriptLogMode.Size = new System.Drawing.Size(88, 14);
            this.lbTranscriptLogMode.TabIndex = 0;
            this.lbTranscriptLogMode.Text = "对话日志详细度:";
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
            this.grpModelParams.Location = new System.Drawing.Point(14, 14);
            this.grpModelParams.Margin = new System.Windows.Forms.Padding(2);
            this.grpModelParams.Name = "grpModelParams";
            this.grpModelParams.Padding = new System.Windows.Forms.Padding(2);
            this.grpModelParams.Size = new System.Drawing.Size(362, 285);
            this.grpModelParams.TabIndex = 15;
            this.grpModelParams.TabStop = false;
            this.grpModelParams.Text = "模型调用参数";
            // 
            // lbHttpRequestTimeOut
            // 
            this.lbHttpRequestTimeOut.Location = new System.Drawing.Point(26, 33);
            this.lbHttpRequestTimeOut.Name = "lbHttpRequestTimeOut";
            this.lbHttpRequestTimeOut.Size = new System.Drawing.Size(106, 14);
            this.lbHttpRequestTimeOut.TabIndex = 4;
            this.lbHttpRequestTimeOut.Text = "请求超时时间(秒)：";
            // 
            // lbNativeThresholdKB
            // 
            this.lbNativeThresholdKB.Location = new System.Drawing.Point(25, 95);
            this.lbNativeThresholdKB.Name = "lbNativeThresholdKB";
            this.lbNativeThresholdKB.Size = new System.Drawing.Size(142, 14);
            this.lbNativeThresholdKB.TabIndex = 4;
            this.lbNativeThresholdKB.Text = "Native文件尺寸上限(KB)：";
            // 
            // lbInlineTextThresholdMB
            // 
            this.lbInlineTextThresholdMB.Location = new System.Drawing.Point(26, 126);
            this.lbInlineTextThresholdMB.Name = "lbInlineTextThresholdMB";
            this.lbInlineTextThresholdMB.Size = new System.Drawing.Size(165, 14);
            this.lbInlineTextThresholdMB.TabIndex = 4;
            this.lbInlineTextThresholdMB.Text = "InlineText文件尺寸上限(MB)：";
            // 
            // lbAttachmentInlineCharBudget
            // 
            this.lbAttachmentInlineCharBudget.Location = new System.Drawing.Point(26, 189);
            this.lbAttachmentInlineCharBudget.Name = "lbAttachmentInlineCharBudget";
            this.lbAttachmentInlineCharBudget.Size = new System.Drawing.Size(108, 14);
            this.lbAttachmentInlineCharBudget.TabIndex = 4;
            this.lbAttachmentInlineCharBudget.Text = "附件内联字符预算：";
            // 
            // lbMaxVisualPages
            // 
            this.lbMaxVisualPages.Location = new System.Drawing.Point(26, 220);
            this.lbMaxVisualPages.Name = "lbMaxVisualPages";
            this.lbMaxVisualPages.Size = new System.Drawing.Size(108, 14);
            this.lbMaxVisualPages.TabIndex = 4;
            this.lbMaxVisualPages.Text = "每轮最大视觉页数：";
            // 
            // seAttachmentFileCountThreshold
            // 
            this.seAttachmentFileCountThreshold.EditValue = new decimal(new int[] {
            5,
            0,
            0,
            0});
            this.seAttachmentFileCountThreshold.Location = new System.Drawing.Point(243, 62);
            this.seAttachmentFileCountThreshold.Name = "seAttachmentFileCountThreshold";
            this.seAttachmentFileCountThreshold.Properties.Buttons.AddRange(new DevExpress.XtraEditors.Controls.EditorButton[] {
            new DevExpress.XtraEditors.Controls.EditorButton(DevExpress.XtraEditors.Controls.ButtonPredefines.Combo)});
            this.seAttachmentFileCountThreshold.Size = new System.Drawing.Size(85, 20);
            this.seAttachmentFileCountThreshold.TabIndex = 5;
            // 
            // ceAutoResizeBigImage
            // 
            this.ceAutoResizeBigImage.EditValue = true;
            this.ceAutoResizeBigImage.Location = new System.Drawing.Point(26, 251);
            this.ceAutoResizeBigImage.Name = "ceAutoResizeBigImage";
            this.ceAutoResizeBigImage.Properties.Caption = "自动缩小大图像到Native文件尺寸内";
            this.ceAutoResizeBigImage.Size = new System.Drawing.Size(210, 19);
            this.ceAutoResizeBigImage.TabIndex = 6;
            // 
            // lbToolFetchFileThresholdMB
            // 
            this.lbToolFetchFileThresholdMB.Location = new System.Drawing.Point(26, 158);
            this.lbToolFetchFileThresholdMB.Name = "lbToolFetchFileThresholdMB";
            this.lbToolFetchFileThresholdMB.Size = new System.Drawing.Size(165, 14);
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
            this.seToolFetchFileThresholdMB.Location = new System.Drawing.Point(244, 155);
            this.seToolFetchFileThresholdMB.Name = "seToolFetchFileThresholdMB";
            this.seToolFetchFileThresholdMB.Properties.Buttons.AddRange(new DevExpress.XtraEditors.Controls.EditorButton[] {
            new DevExpress.XtraEditors.Controls.EditorButton(DevExpress.XtraEditors.Controls.ButtonPredefines.Combo)});
            this.seToolFetchFileThresholdMB.Size = new System.Drawing.Size(84, 20);
            this.seToolFetchFileThresholdMB.TabIndex = 5;
            // 
            // lbAttachmentFileCountThreshold
            // 
            this.lbAttachmentFileCountThreshold.Location = new System.Drawing.Point(26, 64);
            this.lbAttachmentFileCountThreshold.Name = "lbAttachmentFileCountThreshold";
            this.lbAttachmentFileCountThreshold.Size = new System.Drawing.Size(132, 14);
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
            this.seInlineTextThresholdMB.Location = new System.Drawing.Point(243, 124);
            this.seInlineTextThresholdMB.Name = "seInlineTextThresholdMB";
            this.seInlineTextThresholdMB.Properties.Buttons.AddRange(new DevExpress.XtraEditors.Controls.EditorButton[] {
            new DevExpress.XtraEditors.Controls.EditorButton(DevExpress.XtraEditors.Controls.ButtonPredefines.Combo)});
            this.seInlineTextThresholdMB.Size = new System.Drawing.Size(85, 20);
            this.seInlineTextThresholdMB.TabIndex = 5;
            // 
            // seAttachmentInlineCharBudget
            // 
            this.seAttachmentInlineCharBudget.EditValue = new decimal(new int[] {
            12000,
            0,
            0,
            0});
            this.seAttachmentInlineCharBudget.Location = new System.Drawing.Point(243, 186);
            this.seAttachmentInlineCharBudget.Name = "seAttachmentInlineCharBudget";
            this.seAttachmentInlineCharBudget.Properties.Buttons.AddRange(new DevExpress.XtraEditors.Controls.EditorButton[] {
            new DevExpress.XtraEditors.Controls.EditorButton(DevExpress.XtraEditors.Controls.ButtonPredefines.Combo)});
            this.seAttachmentInlineCharBudget.Properties.MaxValue = new decimal(new int[] {
            200000,
            0,
            0,
            0});
            this.seAttachmentInlineCharBudget.Properties.MinValue = new decimal(new int[] {
            1000,
            0,
            0,
            0});
            this.seAttachmentInlineCharBudget.Size = new System.Drawing.Size(85, 20);
            this.seAttachmentInlineCharBudget.TabIndex = 5;
            // 
            // seMaxVisualPages
            // 
            this.seMaxVisualPages.EditValue = new decimal(new int[] {
            5,
            0,
            0,
            0});
            this.seMaxVisualPages.Location = new System.Drawing.Point(243, 217);
            this.seMaxVisualPages.Name = "seMaxVisualPages";
            this.seMaxVisualPages.Properties.Buttons.AddRange(new DevExpress.XtraEditors.Controls.EditorButton[] {
            new DevExpress.XtraEditors.Controls.EditorButton(DevExpress.XtraEditors.Controls.ButtonPredefines.Combo)});
            this.seMaxVisualPages.Properties.MaxValue = new decimal(new int[] {
            50,
            0,
            0,
            0});
            this.seMaxVisualPages.Size = new System.Drawing.Size(85, 20);
            this.seMaxVisualPages.TabIndex = 5;
            // 
            // spHttpRequestTimeOut
            // 
            this.spHttpRequestTimeOut.EditValue = new decimal(new int[] {
            600,
            0,
            0,
            0});
            this.spHttpRequestTimeOut.Location = new System.Drawing.Point(243, 30);
            this.spHttpRequestTimeOut.MenuManager = this.barMan;
            this.spHttpRequestTimeOut.Name = "spHttpRequestTimeOut";
            this.spHttpRequestTimeOut.Properties.Buttons.AddRange(new DevExpress.XtraEditors.Controls.EditorButton[] {
            new DevExpress.XtraEditors.Controls.EditorButton(DevExpress.XtraEditors.Controls.ButtonPredefines.Combo)});
            this.spHttpRequestTimeOut.Size = new System.Drawing.Size(84, 20);
            this.spHttpRequestTimeOut.TabIndex = 5;
            // 
            // seNativeThresholdKB
            // 
            this.seNativeThresholdKB.EditValue = new decimal(new int[] {
            1024,
            0,
            0,
            0});
            this.seNativeThresholdKB.Location = new System.Drawing.Point(243, 93);
            this.seNativeThresholdKB.Name = "seNativeThresholdKB";
            this.seNativeThresholdKB.Properties.Buttons.AddRange(new DevExpress.XtraEditors.Controls.EditorButton[] {
            new DevExpress.XtraEditors.Controls.EditorButton(DevExpress.XtraEditors.Controls.ButtonPredefines.Combo)});
            this.seNativeThresholdKB.Size = new System.Drawing.Size(85, 20);
            this.seNativeThresholdKB.TabIndex = 5;
            // 
            // grpRuntime
            // 
            this.grpRuntime.Controls.Add(this.ceAutoMemoryRecorder);
            this.grpRuntime.Controls.Add(this.ceAutoCompactEnabled);
            this.grpRuntime.Controls.Add(this.lbTrustMode);
            this.grpRuntime.Controls.Add(this.cbeTrustMode);
            this.grpRuntime.Font = new System.Drawing.Font("Microsoft YaHei UI", 9F, System.Drawing.FontStyle.Bold);
            this.grpRuntime.Location = new System.Drawing.Point(14, 300);
            this.grpRuntime.Margin = new System.Windows.Forms.Padding(2);
            this.grpRuntime.Name = "grpRuntime";
            this.grpRuntime.Padding = new System.Windows.Forms.Padding(2);
            this.grpRuntime.Size = new System.Drawing.Size(362, 125);
            this.grpRuntime.TabIndex = 16;
            this.grpRuntime.TabStop = false;
            this.grpRuntime.Text = "运行时参数";
            // 
            // ceAutoMemoryRecorder
            // 
            this.ceAutoMemoryRecorder.EditValue = true;
            this.ceAutoMemoryRecorder.Location = new System.Drawing.Point(26, 24);
            this.ceAutoMemoryRecorder.Margin = new System.Windows.Forms.Padding(2);
            this.ceAutoMemoryRecorder.Name = "ceAutoMemoryRecorder";
            this.ceAutoMemoryRecorder.Properties.Caption = "启用自动记忆归纳系统";
            this.ceAutoMemoryRecorder.Size = new System.Drawing.Size(240, 19);
            this.ceAutoMemoryRecorder.TabIndex = 7;
            // 
            // ceAutoCompactEnabled
            // 
            this.ceAutoCompactEnabled.EditValue = true;
            this.ceAutoCompactEnabled.Location = new System.Drawing.Point(26, 56);
            this.ceAutoCompactEnabled.Margin = new System.Windows.Forms.Padding(2);
            this.ceAutoCompactEnabled.Name = "ceAutoCompactEnabled";
            this.ceAutoCompactEnabled.Properties.Caption = "启用自动上下文压缩 (AutoCompact)";
            this.ceAutoCompactEnabled.Size = new System.Drawing.Size(240, 19);
            this.ceAutoCompactEnabled.TabIndex = 7;
            // 
            // cbeLanguage
            // 
            this.cbeLanguage.Location = new System.Drawing.Point(221, 26);
            this.cbeLanguage.Margin = new System.Windows.Forms.Padding(2);
            this.cbeLanguage.Name = "cbeLanguage";
            this.cbeLanguage.Properties.Buttons.AddRange(new DevExpress.XtraEditors.Controls.EditorButton[] {
            new DevExpress.XtraEditors.Controls.EditorButton(DevExpress.XtraEditors.Controls.ButtonPredefines.Combo)});
            this.cbeLanguage.Properties.TextEditStyle = DevExpress.XtraEditors.Controls.TextEditStyles.DisableTextEditor;
            this.cbeLanguage.Size = new System.Drawing.Size(85, 20);
            this.cbeLanguage.TabIndex = 1;
            // 
            // grpLanguage
            // 
            this.grpLanguage.Controls.Add(this.cbeLanguage);
            this.grpLanguage.Controls.Add(this.lbLanguage);
            this.grpLanguage.Location = new System.Drawing.Point(404, 361);
            this.grpLanguage.Name = "grpLanguage";
            this.grpLanguage.Size = new System.Drawing.Size(333, 64);
            this.grpLanguage.TabIndex = 22;
            this.grpLanguage.TabStop = false;
            this.grpLanguage.Text = "语言";
            // 
            // lbLanguage
            // 
            this.lbLanguage.Location = new System.Drawing.Point(29, 29);
            this.lbLanguage.Margin = new System.Windows.Forms.Padding(2);
            this.lbLanguage.Name = "lbLanguage";
            this.lbLanguage.Size = new System.Drawing.Size(52, 14);
            this.lbLanguage.TabIndex = 10;
            this.lbLanguage.Text = "界面语言:";
            // 
            // fmSysConfig
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 12F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(755, 472);
            this.Controls.Add(this.grpLanguage);
            this.Controls.Add(this.grpRuntime);
            this.Controls.Add(this.grpModelParams);
            this.Controls.Add(this.grpLogging);
            this.Controls.Add(this.sbdcSysConfigBottom);
            this.Controls.Add(this.barDockControlLeft);
            this.Controls.Add(this.barDockControlRight);
            this.Controls.Add(this.barDockControlBottom);
            this.Controls.Add(this.barDockControlTop);
            this.Icon = ((System.Drawing.Icon)(resources.GetObject("$this.Icon")));
            this.Margin = new System.Windows.Forms.Padding(2);
            this.Name = "fmSysConfig";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            this.Text = "系统参数设置";
            ((System.ComponentModel.ISupportInitialize)(this.barMan)).EndInit();
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
            ((System.ComponentModel.ISupportInitialize)(this.cbeTrustMode.Properties)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.cbeTranscriptLogMode.Properties)).EndInit();
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
            this.grpRuntime.ResumeLayout(false);
            this.grpRuntime.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)(this.ceAutoMemoryRecorder.Properties)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.ceAutoCompactEnabled.Properties)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.cbeLanguage.Properties)).EndInit();
            this.grpLanguage.ResumeLayout(false);
            this.grpLanguage.PerformLayout();
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private DevExpress.XtraBars.BarManager barMan;
        private DevExpress.XtraBars.BarButtonItem btnAddModel;
        private DevExpress.XtraBars.BarButtonItem btnEditModel;
        private DevExpress.XtraBars.BarButtonItem btnDeleteModel;
        private DevExpress.XtraBars.BarButtonItem btnExportListToJson;
        private DevExpress.XtraBars.BarButtonItem btnImportList;
        private DevExpress.XtraBars.BarButtonItem btnShowPreview;
        private DevExpress.XtraBars.BarButtonItem btnNewMcp;
        private DevExpress.XtraBars.BarButtonItem btnEditMcp;
        private DevExpress.XtraBars.BarButtonItem btnDelMcp;
        private DevExpress.XtraBars.BarButtonItem btnExportMcp;
        private DevExpress.XtraBars.BarButtonItem btnImportMcp;
        private DevExpress.XtraBars.BarButtonItem btnNewSkill;
        private DevExpress.XtraBars.BarButtonItem btnEditSkill;
        private DevExpress.XtraBars.BarButtonItem btnDeleteSkill;
        private DevExpress.XtraBars.BarButtonItem btnExportSkills;
        private DevExpress.XtraBars.BarButtonItem btnImportSkills;
        private DevExpress.XtraBars.BarButtonItem btnSaveMem;
        private DevExpress.XtraBars.BarStaticItem bsiMemInfo;
        private DevExpress.XtraBars.Bar barGeneralBottom;
        private DevExpress.XtraBars.BarButtonItem btnSaveGenenalConfig;
        private DevExpress.XtraBars.BarButtonItem btnCancelSaveGeneralConfig;
        private DevExpress.XtraBars.StandaloneBarDockControl sbdcSysConfigBottom;
        private DevExpress.XtraBars.BarDockControl barDockControlTop;
        private DevExpress.XtraBars.BarDockControl barDockControlBottom;
        private DevExpress.XtraBars.BarDockControl barDockControlLeft;
        private DevExpress.XtraBars.BarDockControl barDockControlRight;
        private DevExpress.XtraBars.BarButtonItem btnShowDesc;
        private System.Windows.Forms.GroupBox grpLogging;
        private DevExpress.XtraEditors.SpinEdit seTranscriptMaxContentLength;
        private DevExpress.XtraEditors.LabelControl lbTranscriptMaxContentLength;
        private DevExpress.XtraEditors.CheckEdit ceToolCallLogEnabled;
        private DevExpress.XtraEditors.CheckEdit ceTranscriptIncludeHostContext;
        private DevExpress.XtraEditors.CheckEdit ceTranscriptIncludeToolCalls;
        private DevExpress.XtraEditors.SpinEdit seRetainedFileCount;
        private DevExpress.XtraEditors.LabelControl lbRetainedFileCount;
        private DevExpress.XtraEditors.SpinEdit seMaxFileSizeMb;
        private DevExpress.XtraEditors.LabelControl lbMaxFileSizeMb;
        private DevExpress.XtraEditors.ComboBoxEdit cbeRuntimeLogLevel;
        private DevExpress.XtraEditors.LabelControl lbRuntimeLogLevel;
        private DevExpress.XtraEditors.CheckEdit ceRuntimeLogEnabled;
        private DevExpress.XtraEditors.ComboBoxEdit cbeTranscriptLogMode;
        private DevExpress.XtraEditors.LabelControl lbTranscriptLogMode;
        private System.Windows.Forms.GroupBox grpModelParams;
        private DevExpress.XtraEditors.LabelControl lbHttpRequestTimeOut;
        private DevExpress.XtraEditors.LabelControl lbNativeThresholdKB;
        private DevExpress.XtraEditors.LabelControl lbInlineTextThresholdMB;
        private DevExpress.XtraEditors.LabelControl lbAttachmentInlineCharBudget;
        private DevExpress.XtraEditors.SpinEdit seAttachmentInlineCharBudget;
        private DevExpress.XtraEditors.LabelControl lbMaxVisualPages;
        private DevExpress.XtraEditors.SpinEdit seMaxVisualPages;
        private DevExpress.XtraEditors.SpinEdit seAttachmentFileCountThreshold;
        private DevExpress.XtraEditors.CheckEdit ceAutoResizeBigImage;
        private DevExpress.XtraEditors.LabelControl lbToolFetchFileThresholdMB;
        private DevExpress.XtraEditors.SpinEdit seToolFetchFileThresholdMB;
        private DevExpress.XtraEditors.LabelControl lbAttachmentFileCountThreshold;
        private DevExpress.XtraEditors.SpinEdit seInlineTextThresholdMB;
        private DevExpress.XtraEditors.SpinEdit spHttpRequestTimeOut;
        private DevExpress.XtraEditors.SpinEdit seNativeThresholdKB;
        private System.Windows.Forms.GroupBox grpRuntime;
        private DevExpress.XtraEditors.CheckEdit ceAutoMemoryRecorder;
        private DevExpress.XtraEditors.CheckEdit ceAutoCompactEnabled;
        private DevExpress.XtraBars.BarButtonItem btnEditAskModeSysPrompt;
        private DevExpress.XtraBars.BarButtonItem btnEditAgentModeSysPrompt;
        private DevExpress.XtraEditors.ComboBoxEdit cbeTrustMode;
        private DevExpress.XtraEditors.LabelControl lbTrustMode;
        private System.Windows.Forms.GroupBox grpLanguage;
        private DevExpress.XtraEditors.ComboBoxEdit cbeLanguage;
        private DevExpress.XtraEditors.LabelControl lbLanguage;
    }
}