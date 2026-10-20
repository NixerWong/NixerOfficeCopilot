namespace OfficeCopilot.Settings.WinForms
{
    partial class fmSkillEditor
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
            this.barMan = new DevExpress.XtraBars.BarManager(this.components);
            this.barSkillEditorBottom = new DevExpress.XtraBars.Bar();
            this.btnSave = new DevExpress.XtraBars.BarButtonItem();
            this.btnCancel = new DevExpress.XtraBars.BarButtonItem();
            this.barDockControlTop = new DevExpress.XtraBars.BarDockControl();
            this.barDockControlBottom = new DevExpress.XtraBars.BarDockControl();
            this.barDockControlLeft = new DevExpress.XtraBars.BarDockControl();
            this.barDockControlRight = new DevExpress.XtraBars.BarDockControl();
            this.txtName = new DevExpress.XtraEditors.TextEdit();
            this.splitEditor = new System.Windows.Forms.SplitContainer();
            this.pgcSkillBasicParams = new DevExpress.XtraVerticalGrid.PropertyGridControl();
            this.lineHolder2 = new System.Windows.Forms.Label();
            this.txtDescription = new DevExpress.XtraEditors.MemoEdit();
            this.lineHolder1 = new System.Windows.Forms.Label();
            this.xtcSkillMarkDown = new DevExpress.XtraTab.XtraTabControl();
            this.xtpWebPreview = new DevExpress.XtraTab.XtraTabPage();
            this.webPreview = new System.Windows.Forms.WebBrowser();
            this.xtpMarkDown = new DevExpress.XtraTab.XtraTabPage();
            this.txtMarkDown = new DevExpress.XtraEditors.MemoEdit();
            ((System.ComponentModel.ISupportInitialize)(this.barMan)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.txtName.Properties)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.splitEditor)).BeginInit();
            this.splitEditor.Panel1.SuspendLayout();
            this.splitEditor.Panel2.SuspendLayout();
            this.splitEditor.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.pgcSkillBasicParams)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.txtDescription.Properties)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.xtcSkillMarkDown)).BeginInit();
            this.xtcSkillMarkDown.SuspendLayout();
            this.xtpWebPreview.SuspendLayout();
            this.xtpMarkDown.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.txtMarkDown.Properties)).BeginInit();
            this.SuspendLayout();
            // 
            // barMan
            // 
            this.barMan.Bars.AddRange(new DevExpress.XtraBars.Bar[] {
            this.barSkillEditorBottom});
            this.barMan.DockControls.Add(this.barDockControlTop);
            this.barMan.DockControls.Add(this.barDockControlBottom);
            this.barMan.DockControls.Add(this.barDockControlLeft);
            this.barMan.DockControls.Add(this.barDockControlRight);
            this.barMan.Form = this;
            this.barMan.Items.AddRange(new DevExpress.XtraBars.BarItem[] {
            this.btnSave,
            this.btnCancel});
            this.barMan.MaxItemId = 2;
            this.barMan.StatusBar = this.barSkillEditorBottom;
            // 
            // barSkillEditorBottom
            // 
            this.barSkillEditorBottom.BarName = "Status bar";
            this.barSkillEditorBottom.CanDockStyle = DevExpress.XtraBars.BarCanDockStyle.Bottom;
            this.barSkillEditorBottom.DockCol = 0;
            this.barSkillEditorBottom.DockRow = 0;
            this.barSkillEditorBottom.DockStyle = DevExpress.XtraBars.BarDockStyle.Bottom;
            this.barSkillEditorBottom.LinksPersistInfo.AddRange(new DevExpress.XtraBars.LinkPersistInfo[] {
            new DevExpress.XtraBars.LinkPersistInfo(DevExpress.XtraBars.BarLinkUserDefines.PaintStyle, this.btnSave, "", true, true, true, 0, null, DevExpress.XtraBars.BarItemPaintStyle.CaptionGlyph),
            new DevExpress.XtraBars.LinkPersistInfo(DevExpress.XtraBars.BarLinkUserDefines.PaintStyle, this.btnCancel, DevExpress.XtraBars.BarItemPaintStyle.CaptionGlyph)});
            this.barSkillEditorBottom.OptionsBar.AllowQuickCustomization = false;
            this.barSkillEditorBottom.OptionsBar.DrawDragBorder = false;
            this.barSkillEditorBottom.OptionsBar.MinHeight = 30;
            this.barSkillEditorBottom.OptionsBar.UseWholeRow = true;
            this.barSkillEditorBottom.Text = "Status bar";
            // 
            // btnSave
            // 
            this.btnSave.Alignment = DevExpress.XtraBars.BarItemLinkAlignment.Right;
            this.btnSave.Caption = "保存";
            this.btnSave.Id = 0;
            this.btnSave.ImageOptions.Image = global::OfficeCopilot.Settings.WinForms.Properties.Resources.save_16x16;
            this.btnSave.ImageOptions.LargeImage = global::OfficeCopilot.Settings.WinForms.Properties.Resources.save_32x32;
            this.btnSave.Name = "btnSave";
            this.btnSave.ItemClick += new DevExpress.XtraBars.ItemClickEventHandler(this.btnSave_ItemClick);
            // 
            // btnCancel
            // 
            this.btnCancel.Alignment = DevExpress.XtraBars.BarItemLinkAlignment.Right;
            this.btnCancel.Caption = "取消";
            this.btnCancel.Id = 1;
            this.btnCancel.ImageOptions.Image = global::OfficeCopilot.Settings.WinForms.Properties.Resources.cancel_16x16;
            this.btnCancel.ImageOptions.LargeImage = global::OfficeCopilot.Settings.WinForms.Properties.Resources.cancel_32x32;
            this.btnCancel.Name = "btnCancel";
            this.btnCancel.ItemClick += new DevExpress.XtraBars.ItemClickEventHandler(this.btnCancel_ItemClick);
            // 
            // barDockControlTop
            // 
            this.barDockControlTop.CausesValidation = false;
            this.barDockControlTop.Dock = System.Windows.Forms.DockStyle.Top;
            this.barDockControlTop.Location = new System.Drawing.Point(0, 0);
            this.barDockControlTop.Manager = this.barMan;
            this.barDockControlTop.Margin = new System.Windows.Forms.Padding(4);
            this.barDockControlTop.Size = new System.Drawing.Size(1005, 0);
            // 
            // barDockControlBottom
            // 
            this.barDockControlBottom.CausesValidation = false;
            this.barDockControlBottom.Dock = System.Windows.Forms.DockStyle.Bottom;
            this.barDockControlBottom.Location = new System.Drawing.Point(0, 742);
            this.barDockControlBottom.Manager = this.barMan;
            this.barDockControlBottom.Margin = new System.Windows.Forms.Padding(4);
            this.barDockControlBottom.Size = new System.Drawing.Size(1005, 36);
            // 
            // barDockControlLeft
            // 
            this.barDockControlLeft.CausesValidation = false;
            this.barDockControlLeft.Dock = System.Windows.Forms.DockStyle.Left;
            this.barDockControlLeft.Location = new System.Drawing.Point(0, 0);
            this.barDockControlLeft.Manager = this.barMan;
            this.barDockControlLeft.Margin = new System.Windows.Forms.Padding(4);
            this.barDockControlLeft.Size = new System.Drawing.Size(0, 742);
            // 
            // barDockControlRight
            // 
            this.barDockControlRight.CausesValidation = false;
            this.barDockControlRight.Dock = System.Windows.Forms.DockStyle.Right;
            this.barDockControlRight.Location = new System.Drawing.Point(1005, 0);
            this.barDockControlRight.Manager = this.barMan;
            this.barDockControlRight.Margin = new System.Windows.Forms.Padding(4);
            this.barDockControlRight.Size = new System.Drawing.Size(0, 742);
            // 
            // txtName
            // 
            this.txtName.Dock = System.Windows.Forms.DockStyle.Top;
            this.txtName.Location = new System.Drawing.Point(9, 9);
            this.txtName.Margin = new System.Windows.Forms.Padding(4);
            this.txtName.MenuManager = this.barMan;
            this.txtName.Name = "txtName";
            this.txtName.Properties.NullValuePrompt = "Skill 名称";
            this.txtName.Size = new System.Drawing.Size(329, 24);
            this.txtName.TabIndex = 1;
            // 
            // splitEditor
            // 
            this.splitEditor.Dock = System.Windows.Forms.DockStyle.Fill;
            this.splitEditor.FixedPanel = System.Windows.Forms.FixedPanel.Panel1;
            this.splitEditor.Location = new System.Drawing.Point(0, 0);
            this.splitEditor.Margin = new System.Windows.Forms.Padding(4);
            this.splitEditor.Name = "splitEditor";
            // 
            // splitEditor.Panel1
            // 
            this.splitEditor.Panel1.BackColor = System.Drawing.Color.White;
            this.splitEditor.Panel1.Controls.Add(this.pgcSkillBasicParams);
            this.splitEditor.Panel1.Controls.Add(this.lineHolder2);
            this.splitEditor.Panel1.Controls.Add(this.txtDescription);
            this.splitEditor.Panel1.Controls.Add(this.lineHolder1);
            this.splitEditor.Panel1.Controls.Add(this.txtName);
            this.splitEditor.Panel1.Padding = new System.Windows.Forms.Padding(9);
            // 
            // splitEditor.Panel2
            // 
            this.splitEditor.Panel2.BackColor = System.Drawing.Color.White;
            this.splitEditor.Panel2.Controls.Add(this.xtcSkillMarkDown);
            this.splitEditor.Size = new System.Drawing.Size(1005, 742);
            this.splitEditor.SplitterDistance = 347;
            this.splitEditor.SplitterWidth = 5;
            this.splitEditor.TabIndex = 8;
            // 
            // pgcSkillBasicParams
            // 
            this.pgcSkillBasicParams.BandsInterval = 3;
            this.pgcSkillBasicParams.Cursor = System.Windows.Forms.Cursors.Default;
            this.pgcSkillBasicParams.Dock = System.Windows.Forms.DockStyle.Fill;
            this.pgcSkillBasicParams.Location = new System.Drawing.Point(9, 171);
            this.pgcSkillBasicParams.Margin = new System.Windows.Forms.Padding(4);
            this.pgcSkillBasicParams.Name = "pgcSkillBasicParams";
            this.pgcSkillBasicParams.OptionsView.AllowReadOnlyRowAppearance = DevExpress.Utils.DefaultBoolean.True;
            this.pgcSkillBasicParams.OptionsView.FixedLineWidth = 3;
            this.pgcSkillBasicParams.OptionsView.MinRowAutoHeight = 31;
            this.pgcSkillBasicParams.RecordWidth = 85;
            this.pgcSkillBasicParams.RowHeaderWidth = 115;
            this.pgcSkillBasicParams.Size = new System.Drawing.Size(329, 562);
            this.pgcSkillBasicParams.TabIndex = 7;
            // 
            // lineHolder2
            // 
            this.lineHolder2.Dock = System.Windows.Forms.DockStyle.Top;
            this.lineHolder2.Location = new System.Drawing.Point(9, 162);
            this.lineHolder2.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
            this.lineHolder2.Name = "lineHolder2";
            this.lineHolder2.Size = new System.Drawing.Size(329, 9);
            this.lineHolder2.TabIndex = 6;
            this.lineHolder2.Text = " ";
            // 
            // txtDescription
            // 
            this.txtDescription.Dock = System.Windows.Forms.DockStyle.Top;
            this.txtDescription.Location = new System.Drawing.Point(9, 42);
            this.txtDescription.Margin = new System.Windows.Forms.Padding(4);
            this.txtDescription.MenuManager = this.barMan;
            this.txtDescription.Name = "txtDescription";
            this.txtDescription.Properties.NullValuePrompt = "Skill 功能描述";
            this.txtDescription.Size = new System.Drawing.Size(329, 120);
            this.txtDescription.TabIndex = 5;
            // 
            // lineHolder1
            // 
            this.lineHolder1.Dock = System.Windows.Forms.DockStyle.Top;
            this.lineHolder1.Location = new System.Drawing.Point(9, 33);
            this.lineHolder1.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
            this.lineHolder1.Name = "lineHolder1";
            this.lineHolder1.Size = new System.Drawing.Size(329, 9);
            this.lineHolder1.TabIndex = 4;
            this.lineHolder1.Text = " ";
            // 
            // xtcSkillMarkDown
            // 
            this.xtcSkillMarkDown.Dock = System.Windows.Forms.DockStyle.Fill;
            this.xtcSkillMarkDown.HeaderLocation = DevExpress.XtraTab.TabHeaderLocation.Right;
            this.xtcSkillMarkDown.Location = new System.Drawing.Point(0, 0);
            this.xtcSkillMarkDown.Name = "xtcSkillMarkDown";
            this.xtcSkillMarkDown.SelectedTabPage = this.xtpWebPreview;
            this.xtcSkillMarkDown.Size = new System.Drawing.Size(653, 742);
            this.xtcSkillMarkDown.TabIndex = 0;
            this.xtcSkillMarkDown.TabPages.AddRange(new DevExpress.XtraTab.XtraTabPage[] {
            this.xtpWebPreview,
            this.xtpMarkDown});
            // 
            // xtpWebPreview
            // 
            this.xtpWebPreview.Controls.Add(this.webPreview);
            this.xtpWebPreview.ImageOptions.Image = global::OfficeCopilot.Settings.WinForms.Properties.Resources.viewonweb_16x16;
            this.xtpWebPreview.Name = "xtpWebPreview";
            this.xtpWebPreview.Size = new System.Drawing.Size(616, 736);
            this.xtpWebPreview.Text = "Skill预览";
            // 
            // webPreview
            // 
            this.webPreview.Dock = System.Windows.Forms.DockStyle.Fill;
            this.webPreview.Location = new System.Drawing.Point(0, 0);
            this.webPreview.MinimumSize = new System.Drawing.Size(20, 20);
            this.webPreview.Name = "webPreview";
            this.webPreview.Size = new System.Drawing.Size(616, 736);
            this.webPreview.TabIndex = 13;
            // 
            // xtpMarkDown
            // 
            this.xtpMarkDown.Controls.Add(this.txtMarkDown);
            this.xtpMarkDown.ImageOptions.Image = global::OfficeCopilot.Settings.WinForms.Properties.Resources.scripts_16x16;
            this.xtpMarkDown.Name = "xtpMarkDown";
            this.xtpMarkDown.Size = new System.Drawing.Size(616, 736);
            this.xtpMarkDown.Text = "MarkDown编辑";
            // 
            // txtMarkDown
            // 
            this.txtMarkDown.Dock = System.Windows.Forms.DockStyle.Fill;
            this.txtMarkDown.Location = new System.Drawing.Point(0, 0);
            this.txtMarkDown.MenuManager = this.barMan;
            this.txtMarkDown.Name = "txtMarkDown";
            this.txtMarkDown.Size = new System.Drawing.Size(616, 736);
            this.txtMarkDown.TabIndex = 0;
            // 
            // fmSkillEditor
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(8F, 15F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(1005, 778);
            this.Controls.Add(this.splitEditor);
            this.Controls.Add(this.barDockControlLeft);
            this.Controls.Add(this.barDockControlRight);
            this.Controls.Add(this.barDockControlBottom);
            this.Controls.Add(this.barDockControlTop);
            this.Margin = new System.Windows.Forms.Padding(4);
            this.Name = "fmSkillEditor";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            this.Text = "Skill 编辑器";
            ((System.ComponentModel.ISupportInitialize)(this.barMan)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.txtName.Properties)).EndInit();
            this.splitEditor.Panel1.ResumeLayout(false);
            this.splitEditor.Panel2.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)(this.splitEditor)).EndInit();
            this.splitEditor.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)(this.pgcSkillBasicParams)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.txtDescription.Properties)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.xtcSkillMarkDown)).EndInit();
            this.xtcSkillMarkDown.ResumeLayout(false);
            this.xtpWebPreview.ResumeLayout(false);
            this.xtpMarkDown.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)(this.txtMarkDown.Properties)).EndInit();
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private DevExpress.XtraBars.BarManager barMan;
        private DevExpress.XtraBars.Bar barSkillEditorBottom;
        private DevExpress.XtraBars.BarButtonItem btnSave;
        private DevExpress.XtraBars.BarButtonItem btnCancel;
        private DevExpress.XtraBars.BarDockControl barDockControlTop;
        private DevExpress.XtraBars.BarDockControl barDockControlBottom;
        private DevExpress.XtraBars.BarDockControl barDockControlLeft;
        private DevExpress.XtraBars.BarDockControl barDockControlRight;
        private DevExpress.XtraEditors.TextEdit txtName;
        private System.Windows.Forms.SplitContainer splitEditor;
        private System.Windows.Forms.WebBrowser webPreview;
        private DevExpress.XtraTab.XtraTabControl xtcSkillMarkDown;
        private DevExpress.XtraTab.XtraTabPage xtpMarkDown;
        private DevExpress.XtraTab.XtraTabPage xtpWebPreview;
        private DevExpress.XtraEditors.MemoEdit txtDescription;
        private System.Windows.Forms.Label lineHolder1;
        private System.Windows.Forms.Label lineHolder2;
        private DevExpress.XtraVerticalGrid.PropertyGridControl pgcSkillBasicParams;
        private DevExpress.XtraEditors.MemoEdit txtMarkDown;
    }
}
