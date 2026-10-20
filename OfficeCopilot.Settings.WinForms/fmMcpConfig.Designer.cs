namespace OfficeCopilot.Settings.WinForms
{
    partial class fmMcpConfig
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
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(fmMcpConfig));
            this.barMan = new DevExpress.XtraBars.BarManager(this.components);
            this.barMcpTop = new DevExpress.XtraBars.Bar();
            this.btnNewMcp = new DevExpress.XtraBars.BarButtonItem();
            this.btnEditMcp = new DevExpress.XtraBars.BarButtonItem();
            this.btnDelMcp = new DevExpress.XtraBars.BarButtonItem();
            this.btnExportMcp = new DevExpress.XtraBars.BarButtonItem();
            this.btnImportMcp = new DevExpress.XtraBars.BarButtonItem();
            this.sbdcMcpTop = new DevExpress.XtraBars.StandaloneBarDockControl();
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
            this.btnShowDesc = new DevExpress.XtraBars.BarButtonItem();
            this.btnNewSkill = new DevExpress.XtraBars.BarButtonItem();
            this.btnEditSkill = new DevExpress.XtraBars.BarButtonItem();
            this.btnDeleteSkill = new DevExpress.XtraBars.BarButtonItem();
            this.btnExportSkills = new DevExpress.XtraBars.BarButtonItem();
            this.btnImportSkills = new DevExpress.XtraBars.BarButtonItem();
            this.btnSaveMem = new DevExpress.XtraBars.BarButtonItem();
            this.bsiMemInfo = new DevExpress.XtraBars.BarStaticItem();
            this.btnSaveGenenalConfig = new DevExpress.XtraBars.BarButtonItem();
            this.btnCancelSaveGeneralConfig = new DevExpress.XtraBars.BarButtonItem();
            this.gcMcpServer = new DevExpress.XtraGrid.GridControl();
            this.gvMcpServer = new DevExpress.XtraGrid.Views.Grid.GridView();
            this.colMcpId = new DevExpress.XtraGrid.Columns.GridColumn();
            this.colMcpName = new DevExpress.XtraGrid.Columns.GridColumn();
            this.colMcpDesc = new DevExpress.XtraGrid.Columns.GridColumn();
            this.colEnableMcp = new DevExpress.XtraGrid.Columns.GridColumn();
            ((System.ComponentModel.ISupportInitialize)(this.barMan)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.gcMcpServer)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.gvMcpServer)).BeginInit();
            this.SuspendLayout();
            // 
            // barMan
            // 
            this.barMan.Bars.AddRange(new DevExpress.XtraBars.Bar[] {
            this.barMcpTop});
            this.barMan.DockControls.Add(this.barDockControlTop);
            this.barMan.DockControls.Add(this.barDockControlBottom);
            this.barMan.DockControls.Add(this.barDockControlLeft);
            this.barMan.DockControls.Add(this.barDockControlRight);
            this.barMan.DockControls.Add(this.sbdcMcpTop);
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
            this.barMcpTop.OptionsBar.MinHeight = 35;
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
            this.sbdcMcpTop.Location = new System.Drawing.Point(0, 0);
            this.sbdcMcpTop.Manager = this.barMan;
            this.sbdcMcpTop.Margin = new System.Windows.Forms.Padding(0);
            this.sbdcMcpTop.Name = "sbdcMcpTop";
            this.sbdcMcpTop.Size = new System.Drawing.Size(742, 32);
            this.sbdcMcpTop.Text = "standaloneBarDockControl1";
            // 
            // barDockControlTop
            // 
            this.barDockControlTop.CausesValidation = false;
            this.barDockControlTop.Dock = System.Windows.Forms.DockStyle.Top;
            this.barDockControlTop.Location = new System.Drawing.Point(0, 0);
            this.barDockControlTop.Manager = this.barMan;
            this.barDockControlTop.Size = new System.Drawing.Size(742, 0);
            // 
            // barDockControlBottom
            // 
            this.barDockControlBottom.CausesValidation = false;
            this.barDockControlBottom.Dock = System.Windows.Forms.DockStyle.Bottom;
            this.barDockControlBottom.Location = new System.Drawing.Point(0, 402);
            this.barDockControlBottom.Manager = this.barMan;
            this.barDockControlBottom.Size = new System.Drawing.Size(742, 0);
            // 
            // barDockControlLeft
            // 
            this.barDockControlLeft.CausesValidation = false;
            this.barDockControlLeft.Dock = System.Windows.Forms.DockStyle.Left;
            this.barDockControlLeft.Location = new System.Drawing.Point(0, 0);
            this.barDockControlLeft.Manager = this.barMan;
            this.barDockControlLeft.Size = new System.Drawing.Size(0, 402);
            // 
            // barDockControlRight
            // 
            this.barDockControlRight.CausesValidation = false;
            this.barDockControlRight.Dock = System.Windows.Forms.DockStyle.Right;
            this.barDockControlRight.Location = new System.Drawing.Point(742, 0);
            this.barDockControlRight.Manager = this.barMan;
            this.barDockControlRight.Size = new System.Drawing.Size(0, 402);
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
            // btnShowDesc
            // 
            this.btnShowDesc.Caption = "查看";
            this.btnShowDesc.Id = 9;
            this.btnShowDesc.ImageOptions.Image = global::OfficeCopilot.Settings.WinForms.Properties.Resources.show_16x16;
            this.btnShowDesc.ImageOptions.LargeImage = global::OfficeCopilot.Settings.WinForms.Properties.Resources.show_32x32;
            this.btnShowDesc.Name = "btnShowDesc";
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
            // gcMcpServer
            // 
            this.gcMcpServer.Dock = System.Windows.Forms.DockStyle.Fill;
            this.gcMcpServer.Location = new System.Drawing.Point(0, 32);
            this.gcMcpServer.MainView = this.gvMcpServer;
            this.gcMcpServer.MenuManager = this.barMan;
            this.gcMcpServer.Name = "gcMcpServer";
            this.gcMcpServer.Size = new System.Drawing.Size(742, 370);
            this.gcMcpServer.TabIndex = 6;
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
            this.gvMcpServer.GridControl = this.gcMcpServer;
            this.gvMcpServer.Name = "gvMcpServer";
            this.gvMcpServer.OptionsBehavior.Editable = false;
            this.gvMcpServer.OptionsSelection.EnableAppearanceFocusedCell = false;
            this.gvMcpServer.OptionsView.ShowGroupPanel = false;
            this.gvMcpServer.OptionsView.ShowIndicator = false;
            this.gvMcpServer.OptionsView.ShowVerticalLines = DevExpress.Utils.DefaultBoolean.False;
            // 
            // colMcpId
            // 
            this.colMcpId.Caption = "ID";
            this.colMcpId.FieldName = "Id";
            this.colMcpId.MinWidth = 19;
            this.colMcpId.Name = "colMcpId";
            this.colMcpId.Width = 70;
            // 
            // colMcpName
            // 
            this.colMcpName.Caption = "名称";
            this.colMcpName.Name = "colMcpName";
            this.colMcpName.OptionsColumn.FixedWidth = true;
            this.colMcpName.OptionsColumn.ReadOnly = true;
            this.colMcpName.Visible = true;
            this.colMcpName.VisibleIndex = 0;
            this.colMcpName.Width = 150;
            // 
            // colMcpDesc
            // 
            this.colMcpDesc.Caption = "描述";
            this.colMcpDesc.FieldName = "Description";
            this.colMcpDesc.MinWidth = 19;
            this.colMcpDesc.Name = "colMcpDesc";
            this.colMcpDesc.Visible = true;
            this.colMcpDesc.VisibleIndex = 1;
            this.colMcpDesc.Width = 681;
            // 
            // colEnableMcp
            // 
            this.colEnableMcp.Caption = "启用";
            this.colEnableMcp.Name = "colEnableMcp";
            this.colEnableMcp.OptionsColumn.FixedWidth = true;
            this.colEnableMcp.Visible = true;
            this.colEnableMcp.VisibleIndex = 2;
            this.colEnableMcp.Width = 40;
            // 
            // fmMcpConfig
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 12F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(742, 402);
            this.Controls.Add(this.gcMcpServer);
            this.Controls.Add(this.sbdcMcpTop);
            this.Controls.Add(this.barDockControlLeft);
            this.Controls.Add(this.barDockControlRight);
            this.Controls.Add(this.barDockControlBottom);
            this.Controls.Add(this.barDockControlTop);
            this.Icon = ((System.Drawing.Icon)(resources.GetObject("$this.Icon")));
            this.Margin = new System.Windows.Forms.Padding(2, 2, 2, 2);
            this.Name = "fmMcpConfig";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            this.Text = "MCP管理";
            ((System.ComponentModel.ISupportInitialize)(this.barMan)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.gcMcpServer)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.gvMcpServer)).EndInit();
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
        private DevExpress.XtraBars.Bar barMcpTop;
        private DevExpress.XtraBars.BarButtonItem btnNewMcp;
        private DevExpress.XtraBars.BarButtonItem btnEditMcp;
        private DevExpress.XtraBars.BarButtonItem btnDelMcp;
        private DevExpress.XtraBars.BarButtonItem btnExportMcp;
        private DevExpress.XtraBars.BarButtonItem btnImportMcp;
        private DevExpress.XtraBars.StandaloneBarDockControl sbdcMcpTop;
        private DevExpress.XtraBars.BarButtonItem btnNewSkill;
        private DevExpress.XtraBars.BarButtonItem btnEditSkill;
        private DevExpress.XtraBars.BarButtonItem btnDeleteSkill;
        private DevExpress.XtraBars.BarButtonItem btnExportSkills;
        private DevExpress.XtraBars.BarButtonItem btnImportSkills;
        private DevExpress.XtraBars.BarButtonItem btnSaveMem;
        private DevExpress.XtraBars.BarStaticItem bsiMemInfo;
        private DevExpress.XtraBars.BarButtonItem btnSaveGenenalConfig;
        private DevExpress.XtraBars.BarButtonItem btnCancelSaveGeneralConfig;
        private DevExpress.XtraBars.BarDockControl barDockControlTop;
        private DevExpress.XtraBars.BarDockControl barDockControlBottom;
        private DevExpress.XtraBars.BarDockControl barDockControlLeft;
        private DevExpress.XtraBars.BarDockControl barDockControlRight;
        private DevExpress.XtraBars.BarButtonItem btnShowDesc;
        private DevExpress.XtraGrid.GridControl gcMcpServer;
        private DevExpress.XtraGrid.Views.Grid.GridView gvMcpServer;
        private DevExpress.XtraGrid.Columns.GridColumn colMcpId;
        private DevExpress.XtraGrid.Columns.GridColumn colMcpName;
        private DevExpress.XtraGrid.Columns.GridColumn colMcpDesc;
        private DevExpress.XtraGrid.Columns.GridColumn colEnableMcp;
    }
}