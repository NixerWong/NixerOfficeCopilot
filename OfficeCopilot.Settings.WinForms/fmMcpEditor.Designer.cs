namespace OfficeCopilot.Settings.WinForms
{
    partial class fmMcpEditor
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
            this.barMcpEditorButton = new DevExpress.XtraBars.Bar();
            this.btnTest = new DevExpress.XtraBars.BarButtonItem();
            this.btnSave = new DevExpress.XtraBars.BarButtonItem();
            this.btnCancel = new DevExpress.XtraBars.BarButtonItem();
            this.barDockControlTop = new DevExpress.XtraBars.BarDockControl();
            this.barDockControlBottom = new DevExpress.XtraBars.BarDockControl();
            this.barDockControlLeft = new DevExpress.XtraBars.BarDockControl();
            this.barDockControlRight = new DevExpress.XtraBars.BarDockControl();
            this.barStaticItem1 = new DevExpress.XtraBars.BarStaticItem();
            this.panelMcpEditor = new DevExpress.XtraEditors.PanelControl();
            this.spcPameters = new System.Windows.Forms.SplitContainer();
            this.pgParameters = new DevExpress.XtraVerticalGrid.PropertyGridControl();
            this.gcParameters = new DevExpress.XtraGrid.GridControl();
            this.gvHttpHeaders = new DevExpress.XtraGrid.Views.Grid.GridView();
            this.lineHolder3 = new System.Windows.Forms.Label();
            this.cmbMcpType = new DevExpress.XtraEditors.ComboBoxEdit();
            this.lineHolder2 = new System.Windows.Forms.Label();
            this.txtDesc = new DevExpress.XtraEditors.MemoEdit();
            this.lineHolder1 = new System.Windows.Forms.Label();
            this.txtMcpName = new DevExpress.XtraEditors.TextEdit();
            ((System.ComponentModel.ISupportInitialize)(this.barMan)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.panelMcpEditor)).BeginInit();
            this.panelMcpEditor.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.spcPameters)).BeginInit();
            this.spcPameters.Panel1.SuspendLayout();
            this.spcPameters.Panel2.SuspendLayout();
            this.spcPameters.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.pgParameters)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.gcParameters)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.gvHttpHeaders)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.cmbMcpType.Properties)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.txtDesc.Properties)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.txtMcpName.Properties)).BeginInit();
            this.SuspendLayout();
            // 
            // barMan
            // 
            this.barMan.Bars.AddRange(new DevExpress.XtraBars.Bar[] {
            this.barMcpEditorButton});
            this.barMan.DockControls.Add(this.barDockControlTop);
            this.barMan.DockControls.Add(this.barDockControlBottom);
            this.barMan.DockControls.Add(this.barDockControlLeft);
            this.barMan.DockControls.Add(this.barDockControlRight);
            this.barMan.Form = this;
            this.barMan.Items.AddRange(new DevExpress.XtraBars.BarItem[] {
            this.btnTest,
            this.btnSave,
            this.btnCancel});
            this.barMan.MaxItemId = 4;
            this.barMan.StatusBar = this.barMcpEditorButton;
            // 
            // barMcpEditorButton
            // 
            this.barMcpEditorButton.BarName = "Status bar";
            this.barMcpEditorButton.CanDockStyle = DevExpress.XtraBars.BarCanDockStyle.Bottom;
            this.barMcpEditorButton.DockCol = 0;
            this.barMcpEditorButton.DockRow = 0;
            this.barMcpEditorButton.DockStyle = DevExpress.XtraBars.BarDockStyle.Bottom;
            this.barMcpEditorButton.LinksPersistInfo.AddRange(new DevExpress.XtraBars.LinkPersistInfo[] {
            new DevExpress.XtraBars.LinkPersistInfo(DevExpress.XtraBars.BarLinkUserDefines.PaintStyle, this.btnTest, DevExpress.XtraBars.BarItemPaintStyle.CaptionGlyph),
            new DevExpress.XtraBars.LinkPersistInfo(DevExpress.XtraBars.BarLinkUserDefines.PaintStyle, this.btnSave, "", true, true, true, 0, null, DevExpress.XtraBars.BarItemPaintStyle.CaptionGlyph),
            new DevExpress.XtraBars.LinkPersistInfo(DevExpress.XtraBars.BarLinkUserDefines.PaintStyle, this.btnCancel, DevExpress.XtraBars.BarItemPaintStyle.CaptionGlyph)});
            this.barMcpEditorButton.OptionsBar.AllowQuickCustomization = false;
            this.barMcpEditorButton.OptionsBar.DrawDragBorder = false;
            this.barMcpEditorButton.OptionsBar.MinHeight = 30;
            this.barMcpEditorButton.OptionsBar.UseWholeRow = true;
            this.barMcpEditorButton.Text = "Status bar";
            // 
            // btnTest
            // 
            this.btnTest.Alignment = DevExpress.XtraBars.BarItemLinkAlignment.Right;
            this.btnTest.Caption = "测试";
            this.btnTest.Id = 2;
            this.btnTest.ImageOptions.Image = global::OfficeCopilot.Settings.WinForms.Properties.Resources.showtestreport_16x161;
            this.btnTest.ImageOptions.LargeImage = global::OfficeCopilot.Settings.WinForms.Properties.Resources.showtestreport_32x321;
            this.btnTest.Name = "btnTest";
            this.btnTest.ItemClick += new DevExpress.XtraBars.ItemClickEventHandler(this.btnTest_ItemClick);
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
            this.barDockControlTop.Size = new System.Drawing.Size(581, 0);
            // 
            // barDockControlBottom
            // 
            this.barDockControlBottom.CausesValidation = false;
            this.barDockControlBottom.Dock = System.Windows.Forms.DockStyle.Bottom;
            this.barDockControlBottom.Location = new System.Drawing.Point(0, 464);
            this.barDockControlBottom.Manager = this.barMan;
            this.barDockControlBottom.Margin = new System.Windows.Forms.Padding(4);
            this.barDockControlBottom.Size = new System.Drawing.Size(581, 36);
            // 
            // barDockControlLeft
            // 
            this.barDockControlLeft.CausesValidation = false;
            this.barDockControlLeft.Dock = System.Windows.Forms.DockStyle.Left;
            this.barDockControlLeft.Location = new System.Drawing.Point(0, 0);
            this.barDockControlLeft.Manager = this.barMan;
            this.barDockControlLeft.Margin = new System.Windows.Forms.Padding(4);
            this.barDockControlLeft.Size = new System.Drawing.Size(0, 464);
            // 
            // barDockControlRight
            // 
            this.barDockControlRight.CausesValidation = false;
            this.barDockControlRight.Dock = System.Windows.Forms.DockStyle.Right;
            this.barDockControlRight.Location = new System.Drawing.Point(581, 0);
            this.barDockControlRight.Manager = this.barMan;
            this.barDockControlRight.Margin = new System.Windows.Forms.Padding(4);
            this.barDockControlRight.Size = new System.Drawing.Size(0, 464);
            // 
            // barStaticItem1
            // 
            this.barStaticItem1.Caption = "barStaticItem1";
            this.barStaticItem1.Id = 3;
            this.barStaticItem1.Name = "barStaticItem1";
            // 
            // panelMcpEditor
            // 
            this.panelMcpEditor.Controls.Add(this.spcPameters);
            this.panelMcpEditor.Controls.Add(this.lineHolder3);
            this.panelMcpEditor.Controls.Add(this.cmbMcpType);
            this.panelMcpEditor.Controls.Add(this.lineHolder2);
            this.panelMcpEditor.Controls.Add(this.txtDesc);
            this.panelMcpEditor.Controls.Add(this.lineHolder1);
            this.panelMcpEditor.Controls.Add(this.txtMcpName);
            this.panelMcpEditor.Dock = System.Windows.Forms.DockStyle.Fill;
            this.panelMcpEditor.Location = new System.Drawing.Point(0, 0);
            this.panelMcpEditor.Margin = new System.Windows.Forms.Padding(4);
            this.panelMcpEditor.Name = "panelMcpEditor";
            this.panelMcpEditor.Padding = new System.Windows.Forms.Padding(9);
            this.panelMcpEditor.Size = new System.Drawing.Size(581, 464);
            this.panelMcpEditor.TabIndex = 5;
            // 
            // spcPameters
            // 
            this.spcPameters.Dock = System.Windows.Forms.DockStyle.Fill;
            this.spcPameters.Location = new System.Drawing.Point(11, 154);
            this.spcPameters.Margin = new System.Windows.Forms.Padding(4);
            this.spcPameters.Name = "spcPameters";
            this.spcPameters.Orientation = System.Windows.Forms.Orientation.Horizontal;
            // 
            // spcPameters.Panel1
            // 
            this.spcPameters.Panel1.Controls.Add(this.pgParameters);
            // 
            // spcPameters.Panel2
            // 
            this.spcPameters.Panel2.Controls.Add(this.gcParameters);
            this.spcPameters.Size = new System.Drawing.Size(559, 299);
            this.spcPameters.SplitterDistance = 152;
            this.spcPameters.SplitterWidth = 5;
            this.spcPameters.TabIndex = 11;
            // 
            // pgParameters
            // 
            this.pgParameters.BandsInterval = 3;
            this.pgParameters.Cursor = System.Windows.Forms.Cursors.Default;
            this.pgParameters.Dock = System.Windows.Forms.DockStyle.Fill;
            this.pgParameters.Location = new System.Drawing.Point(0, 0);
            this.pgParameters.Margin = new System.Windows.Forms.Padding(4);
            this.pgParameters.MenuManager = this.barMan;
            this.pgParameters.Name = "pgParameters";
            this.pgParameters.OptionsView.AllowReadOnlyRowAppearance = DevExpress.Utils.DefaultBoolean.True;
            this.pgParameters.OptionsView.FixedLineWidth = 3;
            this.pgParameters.OptionsView.MinRowAutoHeight = 12;
            this.pgParameters.RecordWidth = 160;
            this.pgParameters.RowHeaderWidth = 40;
            this.pgParameters.Size = new System.Drawing.Size(559, 152);
            this.pgParameters.TabIndex = 0;
            // 
            // gcParameters
            // 
            this.gcParameters.Dock = System.Windows.Forms.DockStyle.Fill;
            this.gcParameters.EmbeddedNavigator.Margin = new System.Windows.Forms.Padding(4);
            this.gcParameters.Location = new System.Drawing.Point(0, 0);
            this.gcParameters.MainView = this.gvHttpHeaders;
            this.gcParameters.Margin = new System.Windows.Forms.Padding(4);
            this.gcParameters.Name = "gcParameters";
            this.gcParameters.Size = new System.Drawing.Size(559, 142);
            this.gcParameters.TabIndex = 2;
            this.gcParameters.ViewCollection.AddRange(new DevExpress.XtraGrid.Views.Base.BaseView[] {
            this.gvHttpHeaders});
            // 
            // gvHttpHeaders
            // 
            this.gvHttpHeaders.DetailHeight = 437;
            this.gvHttpHeaders.GridControl = this.gcParameters;
            this.gvHttpHeaders.Name = "gvHttpHeaders";
            this.gvHttpHeaders.OptionsBehavior.AllowAddRows = DevExpress.Utils.DefaultBoolean.True;
            this.gvHttpHeaders.OptionsBehavior.AllowDeleteRows = DevExpress.Utils.DefaultBoolean.True;
            this.gvHttpHeaders.OptionsEditForm.PopupEditFormWidth = 1067;
            this.gvHttpHeaders.OptionsView.NewItemRowPosition = DevExpress.XtraGrid.Views.Grid.NewItemRowPosition.Bottom;
            this.gvHttpHeaders.OptionsView.ShowGroupPanel = false;
            this.gvHttpHeaders.OptionsView.ShowIndicator = false;
            // 
            // lineHolder3
            // 
            this.lineHolder3.Dock = System.Windows.Forms.DockStyle.Top;
            this.lineHolder3.Location = new System.Drawing.Point(11, 145);
            this.lineHolder3.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
            this.lineHolder3.Name = "lineHolder3";
            this.lineHolder3.Size = new System.Drawing.Size(559, 9);
            this.lineHolder3.TabIndex = 10;
            this.lineHolder3.Text = " ";
            // 
            // cmbMcpType
            // 
            this.cmbMcpType.Dock = System.Windows.Forms.DockStyle.Top;
            this.cmbMcpType.Location = new System.Drawing.Point(11, 121);
            this.cmbMcpType.Margin = new System.Windows.Forms.Padding(4);
            this.cmbMcpType.MenuManager = this.barMan;
            this.cmbMcpType.Name = "cmbMcpType";
            this.cmbMcpType.Properties.Buttons.AddRange(new DevExpress.XtraEditors.Controls.EditorButton[] {
            new DevExpress.XtraEditors.Controls.EditorButton(DevExpress.XtraEditors.Controls.ButtonPredefines.Combo)});
            this.cmbMcpType.Properties.NullValuePrompt = "MCP类型";
            this.cmbMcpType.Size = new System.Drawing.Size(559, 24);
            this.cmbMcpType.TabIndex = 9;
            // 
            // lineHolder2
            // 
            this.lineHolder2.Dock = System.Windows.Forms.DockStyle.Top;
            this.lineHolder2.Location = new System.Drawing.Point(11, 112);
            this.lineHolder2.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
            this.lineHolder2.Name = "lineHolder2";
            this.lineHolder2.Size = new System.Drawing.Size(559, 9);
            this.lineHolder2.TabIndex = 9;
            this.lineHolder2.Text = " ";
            // 
            // txtDesc
            // 
            this.txtDesc.Dock = System.Windows.Forms.DockStyle.Top;
            this.txtDesc.Location = new System.Drawing.Point(11, 44);
            this.txtDesc.Margin = new System.Windows.Forms.Padding(4);
            this.txtDesc.MenuManager = this.barMan;
            this.txtDesc.Name = "txtDesc";
            this.txtDesc.Properties.NullValuePrompt = "功能说明";
            this.txtDesc.Size = new System.Drawing.Size(559, 68);
            this.txtDesc.TabIndex = 8;
            // 
            // lineHolder1
            // 
            this.lineHolder1.Dock = System.Windows.Forms.DockStyle.Top;
            this.lineHolder1.Location = new System.Drawing.Point(11, 35);
            this.lineHolder1.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
            this.lineHolder1.Name = "lineHolder1";
            this.lineHolder1.Size = new System.Drawing.Size(559, 9);
            this.lineHolder1.TabIndex = 7;
            this.lineHolder1.Text = " ";
            // 
            // txtMcpName
            // 
            this.txtMcpName.Dock = System.Windows.Forms.DockStyle.Top;
            this.txtMcpName.Location = new System.Drawing.Point(11, 11);
            this.txtMcpName.Margin = new System.Windows.Forms.Padding(4);
            this.txtMcpName.MenuManager = this.barMan;
            this.txtMcpName.Name = "txtMcpName";
            this.txtMcpName.Properties.NullValuePrompt = "MCP名称";
            this.txtMcpName.Size = new System.Drawing.Size(559, 24);
            this.txtMcpName.TabIndex = 5;
            // 
            // fmMcpEditor
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(8F, 15F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(581, 500);
            this.Controls.Add(this.panelMcpEditor);
            this.Controls.Add(this.barDockControlLeft);
            this.Controls.Add(this.barDockControlRight);
            this.Controls.Add(this.barDockControlBottom);
            this.Controls.Add(this.barDockControlTop);
            this.Margin = new System.Windows.Forms.Padding(4);
            this.Name = "fmMcpEditor";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            this.Text = "fmMcpEditor";
            ((System.ComponentModel.ISupportInitialize)(this.barMan)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.panelMcpEditor)).EndInit();
            this.panelMcpEditor.ResumeLayout(false);
            this.spcPameters.Panel1.ResumeLayout(false);
            this.spcPameters.Panel2.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)(this.spcPameters)).EndInit();
            this.spcPameters.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)(this.pgParameters)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.gcParameters)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.gvHttpHeaders)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.cmbMcpType.Properties)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.txtDesc.Properties)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.txtMcpName.Properties)).EndInit();
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private DevExpress.XtraBars.BarManager barMan;
        private DevExpress.XtraBars.Bar barMcpEditorButton;
        private DevExpress.XtraBars.BarButtonItem btnSave;
        private DevExpress.XtraBars.BarDockControl barDockControlTop;
        private DevExpress.XtraBars.BarDockControl barDockControlBottom;
        private DevExpress.XtraBars.BarDockControl barDockControlLeft;
        private DevExpress.XtraBars.BarDockControl barDockControlRight;
        private DevExpress.XtraBars.BarButtonItem btnCancel;
        private DevExpress.XtraBars.BarButtonItem btnTest;
        private DevExpress.XtraBars.BarStaticItem barStaticItem1;
        private DevExpress.XtraEditors.PanelControl panelMcpEditor;
        private DevExpress.XtraEditors.TextEdit txtMcpName;
        private System.Windows.Forms.Label lineHolder3;
        private DevExpress.XtraEditors.MemoEdit txtDesc;
        private System.Windows.Forms.Label lineHolder1;
        private System.Windows.Forms.Label lineHolder2;
        private DevExpress.XtraEditors.ComboBoxEdit cmbMcpType;
        private System.Windows.Forms.SplitContainer spcPameters;
        private DevExpress.XtraVerticalGrid.PropertyGridControl pgParameters;
        private DevExpress.XtraGrid.GridControl gcParameters;
        private DevExpress.XtraGrid.Views.Grid.GridView gvHttpHeaders;
    }
}