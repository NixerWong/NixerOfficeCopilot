namespace OfficeCopilot.Settings.WinForms
{
    partial class fmMemoryConfig
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
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(fmMemoryConfig));
            this.spcMemoryMan = new System.Windows.Forms.SplitContainer();
            this.tlMemFileTree = new DevExpress.XtraTreeList.TreeList();
            this.sbdcMemoryBottom = new DevExpress.XtraBars.StandaloneBarDockControl();
            this.barMan = new DevExpress.XtraBars.BarManager(this.components);
            this.barMemBottom = new DevExpress.XtraBars.Bar();
            this.btnSaveMem = new DevExpress.XtraBars.BarButtonItem();
            this.bsiMemInfo = new DevExpress.XtraBars.BarStaticItem();
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
            this.btnSaveGenenalConfig = new DevExpress.XtraBars.BarButtonItem();
            this.btnCancelSaveGeneralConfig = new DevExpress.XtraBars.BarButtonItem();
            this.xtcMemMarkDown = new DevExpress.XtraTab.XtraTabControl();
            this.xtpMemWebPreview = new DevExpress.XtraTab.XtraTabPage();
            this.webPreview = new System.Windows.Forms.WebBrowser();
            this.xtpMemMarkDown = new DevExpress.XtraTab.XtraTabPage();
            this.txtMarkDown = new DevExpress.XtraEditors.MemoEdit();
            ((System.ComponentModel.ISupportInitialize)(this.spcMemoryMan)).BeginInit();
            this.spcMemoryMan.Panel1.SuspendLayout();
            this.spcMemoryMan.Panel2.SuspendLayout();
            this.spcMemoryMan.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.tlMemFileTree)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.barMan)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.xtcMemMarkDown)).BeginInit();
            this.xtcMemMarkDown.SuspendLayout();
            this.xtpMemWebPreview.SuspendLayout();
            this.xtpMemMarkDown.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.txtMarkDown.Properties)).BeginInit();
            this.SuspendLayout();
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
            this.spcMemoryMan.Panel2.Controls.Add(this.sbdcMemoryBottom);
            this.spcMemoryMan.Size = new System.Drawing.Size(1000, 676);
            this.spcMemoryMan.SplitterDistance = 331;
            this.spcMemoryMan.TabIndex = 1;
            // 
            // tlMemFileTree
            // 
            this.tlMemFileTree.BorderStyle = DevExpress.XtraEditors.Controls.BorderStyles.NoBorder;
            this.tlMemFileTree.Dock = System.Windows.Forms.DockStyle.Fill;
            this.tlMemFileTree.Location = new System.Drawing.Point(5, 5);
            this.tlMemFileTree.Name = "tlMemFileTree";
            this.tlMemFileTree.OptionsBehavior.Editable = false;
            this.tlMemFileTree.OptionsMenu.EnableColumnMenu = false;
            this.tlMemFileTree.OptionsMenu.EnableFooterMenu = false;
            this.tlMemFileTree.OptionsMenu.EnableNodeMenu = false;
            this.tlMemFileTree.OptionsView.ShowColumns = false;
            this.tlMemFileTree.OptionsView.ShowIndicator = false;
            this.tlMemFileTree.OptionsView.ShowTreeLines = DevExpress.Utils.DefaultBoolean.True;
            this.tlMemFileTree.RowHeight = 30;
            this.tlMemFileTree.Size = new System.Drawing.Size(321, 666);
            this.tlMemFileTree.TabIndex = 0;
            // 
            // sbdcMemoryBottom
            // 
            this.sbdcMemoryBottom.CausesValidation = false;
            this.sbdcMemoryBottom.Dock = System.Windows.Forms.DockStyle.Bottom;
            this.sbdcMemoryBottom.Location = new System.Drawing.Point(0, 636);
            this.sbdcMemoryBottom.Manager = this.barMan;
            this.sbdcMemoryBottom.Name = "sbdcMemoryBottom";
            this.sbdcMemoryBottom.Size = new System.Drawing.Size(665, 40);
            this.sbdcMemoryBottom.Text = "standaloneBarDockControl1";
            // 
            // barMan
            // 
            this.barMan.Bars.AddRange(new DevExpress.XtraBars.Bar[] {
            this.barMemBottom});
            this.barMan.DockControls.Add(this.barDockControlTop);
            this.barMan.DockControls.Add(this.barDockControlBottom);
            this.barMan.DockControls.Add(this.barDockControlLeft);
            this.barMan.DockControls.Add(this.barDockControlRight);
            this.barMan.DockControls.Add(this.sbdcMemoryBottom);
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
            this.barMemBottom.OptionsBar.MinHeight = 40;
            this.barMemBottom.OptionsBar.UseWholeRow = true;
            this.barMemBottom.StandaloneBarDockControl = this.sbdcMemoryBottom;
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
            // barDockControlTop
            // 
            this.barDockControlTop.CausesValidation = false;
            this.barDockControlTop.Dock = System.Windows.Forms.DockStyle.Top;
            this.barDockControlTop.Location = new System.Drawing.Point(0, 0);
            this.barDockControlTop.Manager = this.barMan;
            this.barDockControlTop.Margin = new System.Windows.Forms.Padding(4);
            this.barDockControlTop.Size = new System.Drawing.Size(1000, 0);
            // 
            // barDockControlBottom
            // 
            this.barDockControlBottom.CausesValidation = false;
            this.barDockControlBottom.Dock = System.Windows.Forms.DockStyle.Bottom;
            this.barDockControlBottom.Location = new System.Drawing.Point(0, 676);
            this.barDockControlBottom.Manager = this.barMan;
            this.barDockControlBottom.Margin = new System.Windows.Forms.Padding(4);
            this.barDockControlBottom.Size = new System.Drawing.Size(1000, 0);
            // 
            // barDockControlLeft
            // 
            this.barDockControlLeft.CausesValidation = false;
            this.barDockControlLeft.Dock = System.Windows.Forms.DockStyle.Left;
            this.barDockControlLeft.Location = new System.Drawing.Point(0, 0);
            this.barDockControlLeft.Manager = this.barMan;
            this.barDockControlLeft.Margin = new System.Windows.Forms.Padding(4);
            this.barDockControlLeft.Size = new System.Drawing.Size(0, 676);
            // 
            // barDockControlRight
            // 
            this.barDockControlRight.CausesValidation = false;
            this.barDockControlRight.Dock = System.Windows.Forms.DockStyle.Right;
            this.barDockControlRight.Location = new System.Drawing.Point(1000, 0);
            this.barDockControlRight.Manager = this.barMan;
            this.barDockControlRight.Margin = new System.Windows.Forms.Padding(4);
            this.barDockControlRight.Size = new System.Drawing.Size(0, 676);
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
            // xtcMemMarkDown
            // 
            this.xtcMemMarkDown.Dock = System.Windows.Forms.DockStyle.Fill;
            this.xtcMemMarkDown.HeaderLocation = DevExpress.XtraTab.TabHeaderLocation.Right;
            this.xtcMemMarkDown.Location = new System.Drawing.Point(0, 0);
            this.xtcMemMarkDown.Name = "xtcMemMarkDown";
            this.xtcMemMarkDown.SelectedTabPage = this.xtpMemWebPreview;
            this.xtcMemMarkDown.Size = new System.Drawing.Size(665, 636);
            this.xtcMemMarkDown.TabIndex = 2;
            this.xtcMemMarkDown.TabPages.AddRange(new DevExpress.XtraTab.XtraTabPage[] {
            this.xtpMemWebPreview,
            this.xtpMemMarkDown});
            // 
            // xtpMemWebPreview
            // 
            this.xtpMemWebPreview.Controls.Add(this.webPreview);
            this.xtpMemWebPreview.ImageOptions.Image = global::OfficeCopilot.Settings.WinForms.Properties.Resources.viewonweb_16x16;
            this.xtpMemWebPreview.Name = "xtpMemWebPreview";
            this.xtpMemWebPreview.Size = new System.Drawing.Size(628, 630);
            this.xtpMemWebPreview.Text = "Skill预览";
            // 
            // webPreview
            // 
            this.webPreview.Dock = System.Windows.Forms.DockStyle.Fill;
            this.webPreview.Location = new System.Drawing.Point(0, 0);
            this.webPreview.MinimumSize = new System.Drawing.Size(20, 20);
            this.webPreview.Name = "webPreview";
            this.webPreview.Size = new System.Drawing.Size(628, 630);
            this.webPreview.TabIndex = 13;
            // 
            // xtpMemMarkDown
            // 
            this.xtpMemMarkDown.Controls.Add(this.txtMarkDown);
            this.xtpMemMarkDown.ImageOptions.Image = global::OfficeCopilot.Settings.WinForms.Properties.Resources.scripts_16x16;
            this.xtpMemMarkDown.Name = "xtpMemMarkDown";
            this.xtpMemMarkDown.Size = new System.Drawing.Size(628, 630);
            this.xtpMemMarkDown.Text = "MarkDown编辑";
            // 
            // txtMarkDown
            // 
            this.txtMarkDown.Dock = System.Windows.Forms.DockStyle.Fill;
            this.txtMarkDown.Location = new System.Drawing.Point(0, 0);
            this.txtMarkDown.MenuManager = this.barMan;
            this.txtMarkDown.Name = "txtMarkDown";
            this.txtMarkDown.Size = new System.Drawing.Size(628, 630);
            this.txtMarkDown.TabIndex = 0;
            // 
            // fmMemoryConfig
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(8F, 15F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(1000, 676);
            this.Controls.Add(this.spcMemoryMan);
            this.Controls.Add(this.barDockControlLeft);
            this.Controls.Add(this.barDockControlRight);
            this.Controls.Add(this.barDockControlBottom);
            this.Controls.Add(this.barDockControlTop);
            this.Icon = ((System.Drawing.Icon)(resources.GetObject("$this.Icon")));
            this.Name = "fmMemoryConfig";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            this.Text = "记忆管理";
            this.spcMemoryMan.Panel1.ResumeLayout(false);
            this.spcMemoryMan.Panel2.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)(this.spcMemoryMan)).EndInit();
            this.spcMemoryMan.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)(this.tlMemFileTree)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.barMan)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.xtcMemMarkDown)).EndInit();
            this.xtcMemMarkDown.ResumeLayout(false);
            this.xtpMemWebPreview.ResumeLayout(false);
            this.xtpMemMarkDown.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)(this.txtMarkDown.Properties)).EndInit();
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.SplitContainer spcMemoryMan;
        private DevExpress.XtraTreeList.TreeList tlMemFileTree;
        private DevExpress.XtraBars.BarManager barMan;
        private DevExpress.XtraBars.Bar barMemBottom;
        private DevExpress.XtraBars.BarButtonItem btnSaveMem;
        private DevExpress.XtraBars.BarStaticItem bsiMemInfo;
        private DevExpress.XtraBars.StandaloneBarDockControl sbdcMemoryBottom;
        private DevExpress.XtraBars.BarDockControl barDockControlTop;
        private DevExpress.XtraBars.BarDockControl barDockControlBottom;
        private DevExpress.XtraBars.BarDockControl barDockControlLeft;
        private DevExpress.XtraBars.BarDockControl barDockControlRight;
        private DevExpress.XtraBars.BarButtonItem btnAddModel;
        private DevExpress.XtraBars.BarButtonItem btnDeleteModel;
        private DevExpress.XtraBars.BarButtonItem btnExportListToJson;
        private DevExpress.XtraBars.BarButtonItem btnImportList;
        private DevExpress.XtraBars.BarButtonItem btnShowPreview;
        private DevExpress.XtraBars.BarButtonItem btnEditModel;
        private DevExpress.XtraBars.BarButtonItem btnNewMcp;
        private DevExpress.XtraBars.BarButtonItem btnEditMcp;
        private DevExpress.XtraBars.BarButtonItem btnDelMcp;
        private DevExpress.XtraBars.BarButtonItem btnShowDesc;
        private DevExpress.XtraBars.BarButtonItem btnImportMcp;
        private DevExpress.XtraBars.BarButtonItem btnExportMcp;
        private DevExpress.XtraBars.BarButtonItem btnNewSkill;
        private DevExpress.XtraBars.BarButtonItem btnEditSkill;
        private DevExpress.XtraBars.BarButtonItem btnDeleteSkill;
        private DevExpress.XtraBars.BarButtonItem btnExportSkills;
        private DevExpress.XtraBars.BarButtonItem btnImportSkills;
        private DevExpress.XtraBars.BarButtonItem btnSaveGenenalConfig;
        private DevExpress.XtraBars.BarButtonItem btnCancelSaveGeneralConfig;
        private DevExpress.XtraTab.XtraTabControl xtcMemMarkDown;
        private DevExpress.XtraTab.XtraTabPage xtpMemWebPreview;
        private System.Windows.Forms.WebBrowser webPreview;
        private DevExpress.XtraTab.XtraTabPage xtpMemMarkDown;
        private DevExpress.XtraEditors.MemoEdit txtMarkDown;
    }
}