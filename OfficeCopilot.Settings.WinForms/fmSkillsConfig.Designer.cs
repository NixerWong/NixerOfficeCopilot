namespace OfficeCopilot.Settings.WinForms
{
    partial class fmSkillsConfig
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
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(fmSkillsConfig));
            this.barMan = new DevExpress.XtraBars.BarManager(this.components);
            this.barSkillTop = new DevExpress.XtraBars.Bar();
            this.btnNewSkill = new DevExpress.XtraBars.BarButtonItem();
            this.btnEditSkill = new DevExpress.XtraBars.BarButtonItem();
            this.btnDeleteSkill = new DevExpress.XtraBars.BarButtonItem();
            this.btnExportSkills = new DevExpress.XtraBars.BarButtonItem();
            this.btnImportSkills = new DevExpress.XtraBars.BarButtonItem();
            this.sbdcSkillTop = new DevExpress.XtraBars.StandaloneBarDockControl();
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
            this.btnSaveMem = new DevExpress.XtraBars.BarButtonItem();
            this.bsiMemInfo = new DevExpress.XtraBars.BarStaticItem();
            this.btnSaveGenenalConfig = new DevExpress.XtraBars.BarButtonItem();
            this.btnCancelSaveGeneralConfig = new DevExpress.XtraBars.BarButtonItem();
            this.gcSkills = new DevExpress.XtraGrid.GridControl();
            this.gvSkills = new DevExpress.XtraGrid.Views.Grid.GridView();
            this.colSkillId = new DevExpress.XtraGrid.Columns.GridColumn();
            this.colSkillName = new DevExpress.XtraGrid.Columns.GridColumn();
            this.colSkillDesc = new DevExpress.XtraGrid.Columns.GridColumn();
            this.colSkillEnabled = new DevExpress.XtraGrid.Columns.GridColumn();
            ((System.ComponentModel.ISupportInitialize)(this.barMan)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.gcSkills)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.gvSkills)).BeginInit();
            this.SuspendLayout();
            // 
            // barMan
            // 
            this.barMan.Bars.AddRange(new DevExpress.XtraBars.Bar[] {
            this.barSkillTop});
            this.barMan.DockControls.Add(this.barDockControlTop);
            this.barMan.DockControls.Add(this.barDockControlBottom);
            this.barMan.DockControls.Add(this.barDockControlLeft);
            this.barMan.DockControls.Add(this.barDockControlRight);
            this.barMan.DockControls.Add(this.sbdcSkillTop);
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
            this.barSkillTop.OptionsBar.MinHeight = 35;
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
            this.sbdcSkillTop.Location = new System.Drawing.Point(0, 0);
            this.sbdcSkillTop.Manager = this.barMan;
            this.sbdcSkillTop.Margin = new System.Windows.Forms.Padding(2, 2, 2, 2);
            this.sbdcSkillTop.Name = "sbdcSkillTop";
            this.sbdcSkillTop.Size = new System.Drawing.Size(700, 32);
            this.sbdcSkillTop.Text = "sbdcSkillTop";
            // 
            // barDockControlTop
            // 
            this.barDockControlTop.CausesValidation = false;
            this.barDockControlTop.Dock = System.Windows.Forms.DockStyle.Top;
            this.barDockControlTop.Location = new System.Drawing.Point(0, 0);
            this.barDockControlTop.Manager = this.barMan;
            this.barDockControlTop.Size = new System.Drawing.Size(700, 0);
            // 
            // barDockControlBottom
            // 
            this.barDockControlBottom.CausesValidation = false;
            this.barDockControlBottom.Dock = System.Windows.Forms.DockStyle.Bottom;
            this.barDockControlBottom.Location = new System.Drawing.Point(0, 498);
            this.barDockControlBottom.Manager = this.barMan;
            this.barDockControlBottom.Size = new System.Drawing.Size(700, 0);
            // 
            // barDockControlLeft
            // 
            this.barDockControlLeft.CausesValidation = false;
            this.barDockControlLeft.Dock = System.Windows.Forms.DockStyle.Left;
            this.barDockControlLeft.Location = new System.Drawing.Point(0, 0);
            this.barDockControlLeft.Manager = this.barMan;
            this.barDockControlLeft.Size = new System.Drawing.Size(0, 498);
            // 
            // barDockControlRight
            // 
            this.barDockControlRight.CausesValidation = false;
            this.barDockControlRight.Dock = System.Windows.Forms.DockStyle.Right;
            this.barDockControlRight.Location = new System.Drawing.Point(700, 0);
            this.barDockControlRight.Manager = this.barMan;
            this.barDockControlRight.Size = new System.Drawing.Size(0, 498);
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
            // gcSkills
            // 
            this.gcSkills.Dock = System.Windows.Forms.DockStyle.Fill;
            this.gcSkills.Location = new System.Drawing.Point(0, 32);
            this.gcSkills.MainView = this.gvSkills;
            this.gcSkills.MenuManager = this.barMan;
            this.gcSkills.Name = "gcSkills";
            this.gcSkills.Size = new System.Drawing.Size(700, 466);
            this.gcSkills.TabIndex = 7;
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
            this.gvSkills.GridControl = this.gcSkills;
            this.gvSkills.Name = "gvSkills";
            this.gvSkills.OptionsBehavior.Editable = false;
            this.gvSkills.OptionsSelection.EnableAppearanceFocusedCell = false;
            this.gvSkills.OptionsView.ShowGroupPanel = false;
            this.gvSkills.OptionsView.ShowIndicator = false;
            this.gvSkills.OptionsView.ShowVerticalLines = DevExpress.Utils.DefaultBoolean.False;
            // 
            // colSkillId
            // 
            this.colSkillId.Caption = "ID";
            this.colSkillId.FieldName = "Id";
            this.colSkillId.MinWidth = 19;
            this.colSkillId.Name = "colSkillId";
            this.colSkillId.Width = 70;
            // 
            // colSkillName
            // 
            this.colSkillName.Caption = "名称";
            this.colSkillName.FieldName = "Name";
            this.colSkillName.Name = "colSkillName";
            this.colSkillName.OptionsColumn.FixedWidth = true;
            this.colSkillName.Visible = true;
            this.colSkillName.VisibleIndex = 0;
            this.colSkillName.Width = 150;
            // 
            // colSkillDesc
            // 
            this.colSkillDesc.Caption = "描述";
            this.colSkillDesc.FieldName = "Description";
            this.colSkillDesc.MinWidth = 19;
            this.colSkillDesc.Name = "colSkillDesc";
            this.colSkillDesc.Visible = true;
            this.colSkillDesc.VisibleIndex = 1;
            this.colSkillDesc.Width = 606;
            // 
            // colSkillEnabled
            // 
            this.colSkillEnabled.Caption = "启用";
            this.colSkillEnabled.FieldName = "IsEnabled";
            this.colSkillEnabled.Name = "colSkillEnabled";
            this.colSkillEnabled.OptionsColumn.FixedWidth = true;
            this.colSkillEnabled.Visible = true;
            this.colSkillEnabled.VisibleIndex = 2;
            this.colSkillEnabled.Width = 40;
            // 
            // fmSkillsConfig
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 12F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(700, 498);
            this.Controls.Add(this.gcSkills);
            this.Controls.Add(this.sbdcSkillTop);
            this.Controls.Add(this.barDockControlLeft);
            this.Controls.Add(this.barDockControlRight);
            this.Controls.Add(this.barDockControlBottom);
            this.Controls.Add(this.barDockControlTop);
            this.Icon = ((System.Drawing.Icon)(resources.GetObject("$this.Icon")));
            this.Margin = new System.Windows.Forms.Padding(2, 2, 2, 2);
            this.Name = "fmSkillsConfig";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            this.Text = "Skills技能管理";
            ((System.ComponentModel.ISupportInitialize)(this.barMan)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.gcSkills)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.gvSkills)).EndInit();
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
        private DevExpress.XtraBars.Bar barSkillTop;
        private DevExpress.XtraBars.BarButtonItem btnNewSkill;
        private DevExpress.XtraBars.BarButtonItem btnEditSkill;
        private DevExpress.XtraBars.BarButtonItem btnDeleteSkill;
        private DevExpress.XtraBars.BarButtonItem btnExportSkills;
        private DevExpress.XtraBars.BarButtonItem btnImportSkills;
        private DevExpress.XtraBars.StandaloneBarDockControl sbdcSkillTop;
        private DevExpress.XtraBars.BarButtonItem btnSaveMem;
        private DevExpress.XtraBars.BarStaticItem bsiMemInfo;
        private DevExpress.XtraBars.BarButtonItem btnSaveGenenalConfig;
        private DevExpress.XtraBars.BarButtonItem btnCancelSaveGeneralConfig;
        private DevExpress.XtraBars.BarDockControl barDockControlTop;
        private DevExpress.XtraBars.BarDockControl barDockControlBottom;
        private DevExpress.XtraBars.BarDockControl barDockControlLeft;
        private DevExpress.XtraBars.BarDockControl barDockControlRight;
        private DevExpress.XtraBars.BarButtonItem btnShowDesc;
        private DevExpress.XtraGrid.GridControl gcSkills;
        private DevExpress.XtraGrid.Views.Grid.GridView gvSkills;
        private DevExpress.XtraGrid.Columns.GridColumn colSkillId;
        private DevExpress.XtraGrid.Columns.GridColumn colSkillName;
        private DevExpress.XtraGrid.Columns.GridColumn colSkillDesc;
        private DevExpress.XtraGrid.Columns.GridColumn colSkillEnabled;
    }
}