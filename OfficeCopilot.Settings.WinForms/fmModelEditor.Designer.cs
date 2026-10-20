namespace OfficeCopilot.Settings.WinForms
{
    partial class fmModelEditor
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
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(fmModelEditor));
            this.gcModelParams = new DevExpress.XtraEditors.GroupControl();
            this.spcBasicParams = new System.Windows.Forms.SplitContainer();
            this.txtSystemPrompt = new DevExpress.XtraEditors.MemoEdit();
            this.lineHolder = new System.Windows.Forms.Label();
            this.txtModelName = new DevExpress.XtraEditors.TextEdit();
            this.gcAdditionalParameters = new DevExpress.XtraEditors.GroupControl();
            this.gcAdditionalParams = new DevExpress.XtraGrid.GridControl();
            this.gvAdditionalParams = new DevExpress.XtraGrid.Views.Grid.GridView();
            this.lineHolder1 = new System.Windows.Forms.Label();
            this.gcBasicParameters = new DevExpress.XtraEditors.GroupControl();
            this.pgcBasicParams = new DevExpress.XtraVerticalGrid.PropertyGridControl();
            this.barMan = new DevExpress.XtraBars.BarManager(this.components);
            this.barModelEdit = new DevExpress.XtraBars.Bar();
            this.btnTestConn = new DevExpress.XtraBars.BarButtonItem();
            this.btnSave = new DevExpress.XtraBars.BarButtonItem();
            this.btnCancel = new DevExpress.XtraBars.BarButtonItem();
            this.bciEnableModel = new DevExpress.XtraBars.BarCheckItem();
            this.barDockControlTop = new DevExpress.XtraBars.BarDockControl();
            this.barDockControlBottom = new DevExpress.XtraBars.BarDockControl();
            this.barDockControlLeft = new DevExpress.XtraBars.BarDockControl();
            this.barDockControlRight = new DevExpress.XtraBars.BarDockControl();
            this.gcHttpHeadersSet = new DevExpress.XtraEditors.GroupControl();
            this.gcHttpHeaders = new DevExpress.XtraGrid.GridControl();
            this.gvHttpHeaders = new DevExpress.XtraGrid.Views.Grid.GridView();
            ((System.ComponentModel.ISupportInitialize)(this.gcModelParams)).BeginInit();
            this.gcModelParams.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.spcBasicParams)).BeginInit();
            this.spcBasicParams.Panel1.SuspendLayout();
            this.spcBasicParams.Panel2.SuspendLayout();
            this.spcBasicParams.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.txtSystemPrompt.Properties)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.txtModelName.Properties)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.gcAdditionalParameters)).BeginInit();
            this.gcAdditionalParameters.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.gcAdditionalParams)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.gvAdditionalParams)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.gcBasicParameters)).BeginInit();
            this.gcBasicParameters.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.pgcBasicParams)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.barMan)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.gcHttpHeadersSet)).BeginInit();
            this.gcHttpHeadersSet.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.gcHttpHeaders)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.gvHttpHeaders)).BeginInit();
            this.SuspendLayout();
            // 
            // gcModelParams
            // 
            this.gcModelParams.CaptionImageOptions.Image = global::OfficeCopilot.Settings.WinForms.Properties.Resources.build_16x16;
            this.gcModelParams.CaptionImageOptions.Padding = new System.Windows.Forms.Padding(3);
            this.gcModelParams.Controls.Add(this.spcBasicParams);
            this.gcModelParams.Dock = System.Windows.Forms.DockStyle.Top;
            this.gcModelParams.Location = new System.Drawing.Point(0, 0);
            this.gcModelParams.Margin = new System.Windows.Forms.Padding(4, 4, 4, 4);
            this.gcModelParams.Name = "gcModelParams";
            this.gcModelParams.Size = new System.Drawing.Size(1129, 571);
            this.gcModelParams.TabIndex = 0;
            this.gcModelParams.Text = "模型参数设置";
            // 
            // spcBasicParams
            // 
            this.spcBasicParams.Dock = System.Windows.Forms.DockStyle.Fill;
            this.spcBasicParams.Location = new System.Drawing.Point(2, 31);
            this.spcBasicParams.Margin = new System.Windows.Forms.Padding(4, 4, 4, 4);
            this.spcBasicParams.Name = "spcBasicParams";
            // 
            // spcBasicParams.Panel1
            // 
            this.spcBasicParams.Panel1.Controls.Add(this.txtSystemPrompt);
            this.spcBasicParams.Panel1.Controls.Add(this.lineHolder);
            this.spcBasicParams.Panel1.Controls.Add(this.txtModelName);
            this.spcBasicParams.Panel1.Padding = new System.Windows.Forms.Padding(9, 9, 4, 9);
            // 
            // spcBasicParams.Panel2
            // 
            this.spcBasicParams.Panel2.Controls.Add(this.gcAdditionalParameters);
            this.spcBasicParams.Panel2.Controls.Add(this.lineHolder1);
            this.spcBasicParams.Panel2.Controls.Add(this.gcBasicParameters);
            this.spcBasicParams.Panel2.Padding = new System.Windows.Forms.Padding(4, 9, 9, 9);
            this.spcBasicParams.Size = new System.Drawing.Size(1125, 538);
            this.spcBasicParams.SplitterDistance = 413;
            this.spcBasicParams.SplitterWidth = 5;
            this.spcBasicParams.TabIndex = 0;
            // 
            // txtSystemPrompt
            // 
            this.txtSystemPrompt.Dock = System.Windows.Forms.DockStyle.Fill;
            this.txtSystemPrompt.Location = new System.Drawing.Point(9, 42);
            this.txtSystemPrompt.Margin = new System.Windows.Forms.Padding(4, 4, 4, 4);
            this.txtSystemPrompt.Name = "txtSystemPrompt";
            this.txtSystemPrompt.Properties.NullValuePrompt = "模型附加提示词（追加在宿主系统提示词之后，可留空）";
            this.txtSystemPrompt.Size = new System.Drawing.Size(400, 487);
            this.txtSystemPrompt.TabIndex = 2;
            // 
            // lineHolder
            // 
            this.lineHolder.Dock = System.Windows.Forms.DockStyle.Top;
            this.lineHolder.Location = new System.Drawing.Point(9, 33);
            this.lineHolder.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
            this.lineHolder.Name = "lineHolder";
            this.lineHolder.Size = new System.Drawing.Size(400, 9);
            this.lineHolder.TabIndex = 1;
            this.lineHolder.Text = " ";
            // 
            // txtModelName
            // 
            this.txtModelName.Dock = System.Windows.Forms.DockStyle.Top;
            this.txtModelName.Location = new System.Drawing.Point(9, 9);
            this.txtModelName.Margin = new System.Windows.Forms.Padding(4, 4, 4, 4);
            this.txtModelName.Name = "txtModelName";
            this.txtModelName.Properties.NullValuePrompt = "模型名称";
            this.txtModelName.Size = new System.Drawing.Size(400, 24);
            this.txtModelName.TabIndex = 0;
            // 
            // gcAdditionalParameters
            // 
            this.gcAdditionalParameters.Controls.Add(this.gcAdditionalParams);
            this.gcAdditionalParameters.Dock = System.Windows.Forms.DockStyle.Fill;
            this.gcAdditionalParameters.Location = new System.Drawing.Point(4, 343);
            this.gcAdditionalParameters.Margin = new System.Windows.Forms.Padding(4, 4, 4, 4);
            this.gcAdditionalParameters.Name = "gcAdditionalParameters";
            this.gcAdditionalParameters.Size = new System.Drawing.Size(694, 186);
            this.gcAdditionalParameters.TabIndex = 6;
            this.gcAdditionalParameters.Text = "附加参数";
            // 
            // gcAdditionalParams
            // 
            this.gcAdditionalParams.Dock = System.Windows.Forms.DockStyle.Fill;
            this.gcAdditionalParams.EmbeddedNavigator.Margin = new System.Windows.Forms.Padding(4, 4, 4, 4);
            this.gcAdditionalParams.Location = new System.Drawing.Point(2, 27);
            this.gcAdditionalParams.MainView = this.gvAdditionalParams;
            this.gcAdditionalParams.Margin = new System.Windows.Forms.Padding(4, 4, 4, 4);
            this.gcAdditionalParams.Name = "gcAdditionalParams";
            this.gcAdditionalParams.Size = new System.Drawing.Size(690, 157);
            this.gcAdditionalParams.TabIndex = 0;
            this.gcAdditionalParams.ViewCollection.AddRange(new DevExpress.XtraGrid.Views.Base.BaseView[] {
            this.gvAdditionalParams});
            // 
            // gvAdditionalParams
            // 
            this.gvAdditionalParams.DetailHeight = 437;
            this.gvAdditionalParams.GridControl = this.gcAdditionalParams;
            this.gvAdditionalParams.Name = "gvAdditionalParams";
            this.gvAdditionalParams.OptionsBehavior.AllowAddRows = DevExpress.Utils.DefaultBoolean.True;
            this.gvAdditionalParams.OptionsBehavior.AllowDeleteRows = DevExpress.Utils.DefaultBoolean.True;
            this.gvAdditionalParams.OptionsEditForm.PopupEditFormWidth = 1067;
            this.gvAdditionalParams.OptionsView.NewItemRowPosition = DevExpress.XtraGrid.Views.Grid.NewItemRowPosition.Bottom;
            this.gvAdditionalParams.OptionsView.ShowGroupPanel = false;
            this.gvAdditionalParams.OptionsView.ShowIndicator = false;
            // 
            // lineHolder1
            // 
            this.lineHolder1.Dock = System.Windows.Forms.DockStyle.Top;
            this.lineHolder1.Location = new System.Drawing.Point(4, 334);
            this.lineHolder1.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
            this.lineHolder1.Name = "lineHolder1";
            this.lineHolder1.Size = new System.Drawing.Size(694, 9);
            this.lineHolder1.TabIndex = 5;
            this.lineHolder1.Text = " ";
            // 
            // gcBasicParameters
            // 
            this.gcBasicParameters.Controls.Add(this.pgcBasicParams);
            this.gcBasicParameters.Dock = System.Windows.Forms.DockStyle.Top;
            this.gcBasicParameters.Location = new System.Drawing.Point(4, 9);
            this.gcBasicParameters.Margin = new System.Windows.Forms.Padding(4, 4, 4, 4);
            this.gcBasicParameters.Name = "gcBasicParameters";
            this.gcBasicParameters.Size = new System.Drawing.Size(694, 325);
            this.gcBasicParameters.TabIndex = 4;
            this.gcBasicParameters.Text = "基本参数";
            // 
            // pgcBasicParams
            // 
            this.pgcBasicParams.BandsInterval = 3;
            this.pgcBasicParams.Cursor = System.Windows.Forms.Cursors.Default;
            this.pgcBasicParams.Dock = System.Windows.Forms.DockStyle.Fill;
            this.pgcBasicParams.Location = new System.Drawing.Point(2, 27);
            this.pgcBasicParams.Margin = new System.Windows.Forms.Padding(4, 4, 4, 4);
            this.pgcBasicParams.Name = "pgcBasicParams";
            this.pgcBasicParams.OptionsView.AllowReadOnlyRowAppearance = DevExpress.Utils.DefaultBoolean.True;
            this.pgcBasicParams.OptionsView.FixedLineWidth = 3;
            this.pgcBasicParams.OptionsView.MinRowAutoHeight = 31;
            this.pgcBasicParams.RecordWidth = 155;
            this.pgcBasicParams.RowHeaderWidth = 45;
            this.pgcBasicParams.Size = new System.Drawing.Size(690, 296);
            this.pgcBasicParams.TabIndex = 0;
            // 
            // barMan
            // 
            this.barMan.Bars.AddRange(new DevExpress.XtraBars.Bar[] {
            this.barModelEdit});
            this.barMan.DockControls.Add(this.barDockControlTop);
            this.barMan.DockControls.Add(this.barDockControlBottom);
            this.barMan.DockControls.Add(this.barDockControlLeft);
            this.barMan.DockControls.Add(this.barDockControlRight);
            this.barMan.Form = this;
            this.barMan.Items.AddRange(new DevExpress.XtraBars.BarItem[] {
            this.btnSave,
            this.btnCancel,
            this.btnTestConn,
            this.bciEnableModel});
            this.barMan.MaxItemId = 4;
            this.barMan.StatusBar = this.barModelEdit;
            // 
            // barModelEdit
            // 
            this.barModelEdit.BarAppearance.Pressed.BorderColor = System.Drawing.Color.FromArgb(((int)(((byte)(64)))), ((int)(((byte)(64)))), ((int)(((byte)(64)))));
            this.barModelEdit.BarAppearance.Pressed.Options.UseBorderColor = true;
            this.barModelEdit.BarName = "barModelEdit";
            this.barModelEdit.CanDockStyle = DevExpress.XtraBars.BarCanDockStyle.Bottom;
            this.barModelEdit.DockCol = 0;
            this.barModelEdit.DockRow = 0;
            this.barModelEdit.DockStyle = DevExpress.XtraBars.BarDockStyle.Bottom;
            this.barModelEdit.LinksPersistInfo.AddRange(new DevExpress.XtraBars.LinkPersistInfo[] {
            new DevExpress.XtraBars.LinkPersistInfo(DevExpress.XtraBars.BarLinkUserDefines.PaintStyle, this.btnTestConn, DevExpress.XtraBars.BarItemPaintStyle.CaptionGlyph),
            new DevExpress.XtraBars.LinkPersistInfo(DevExpress.XtraBars.BarLinkUserDefines.PaintStyle, this.btnSave, "", true, true, true, 0, null, DevExpress.XtraBars.BarItemPaintStyle.CaptionGlyph),
            new DevExpress.XtraBars.LinkPersistInfo(DevExpress.XtraBars.BarLinkUserDefines.PaintStyle, this.btnCancel, DevExpress.XtraBars.BarItemPaintStyle.CaptionGlyph),
            new DevExpress.XtraBars.LinkPersistInfo(this.bciEnableModel)});
            this.barModelEdit.OptionsBar.AllowQuickCustomization = false;
            this.barModelEdit.OptionsBar.DrawDragBorder = false;
            this.barModelEdit.OptionsBar.MinHeight = 30;
            this.barModelEdit.OptionsBar.UseWholeRow = true;
            this.barModelEdit.Text = "保存退出";
            // 
            // btnTestConn
            // 
            this.btnTestConn.Alignment = DevExpress.XtraBars.BarItemLinkAlignment.Right;
            this.btnTestConn.Caption = "测试连接";
            this.btnTestConn.Id = 2;
            this.btnTestConn.ImageOptions.Image = ((System.Drawing.Image)(resources.GetObject("btnTestConn.ImageOptions.Image")));
            this.btnTestConn.Name = "btnTestConn";
            // 
            // btnSave
            // 
            this.btnSave.Alignment = DevExpress.XtraBars.BarItemLinkAlignment.Right;
            this.btnSave.Caption = "保存";
            this.btnSave.Id = 0;
            this.btnSave.ImageOptions.Image = ((System.Drawing.Image)(resources.GetObject("btnSave.ImageOptions.Image")));
            this.btnSave.ImageOptions.LargeImage = ((System.Drawing.Image)(resources.GetObject("btnSave.ImageOptions.LargeImage")));
            this.btnSave.Name = "btnSave";
            // 
            // btnCancel
            // 
            this.btnCancel.Alignment = DevExpress.XtraBars.BarItemLinkAlignment.Right;
            this.btnCancel.Caption = "取消";
            this.btnCancel.Id = 1;
            this.btnCancel.ImageOptions.Image = ((System.Drawing.Image)(resources.GetObject("btnCancel.ImageOptions.Image")));
            this.btnCancel.ImageOptions.LargeImage = ((System.Drawing.Image)(resources.GetObject("btnCancel.ImageOptions.LargeImage")));
            this.btnCancel.Name = "btnCancel";
            // 
            // bciEnableModel
            // 
            this.bciEnableModel.BindableChecked = true;
            this.bciEnableModel.Caption = "启用该模型";
            this.bciEnableModel.CheckBoxVisibility = DevExpress.XtraBars.CheckBoxVisibility.BeforeText;
            this.bciEnableModel.Checked = true;
            this.bciEnableModel.Id = 3;
            this.bciEnableModel.Name = "bciEnableModel";
            // 
            // barDockControlTop
            // 
            this.barDockControlTop.CausesValidation = false;
            this.barDockControlTop.Dock = System.Windows.Forms.DockStyle.Top;
            this.barDockControlTop.Location = new System.Drawing.Point(0, 0);
            this.barDockControlTop.Manager = this.barMan;
            this.barDockControlTop.Margin = new System.Windows.Forms.Padding(4, 4, 4, 4);
            this.barDockControlTop.Size = new System.Drawing.Size(1129, 0);
            // 
            // barDockControlBottom
            // 
            this.barDockControlBottom.CausesValidation = false;
            this.barDockControlBottom.Dock = System.Windows.Forms.DockStyle.Bottom;
            this.barDockControlBottom.Location = new System.Drawing.Point(0, 733);
            this.barDockControlBottom.Manager = this.barMan;
            this.barDockControlBottom.Margin = new System.Windows.Forms.Padding(4, 4, 4, 4);
            this.barDockControlBottom.Size = new System.Drawing.Size(1129, 36);
            // 
            // barDockControlLeft
            // 
            this.barDockControlLeft.CausesValidation = false;
            this.barDockControlLeft.Dock = System.Windows.Forms.DockStyle.Left;
            this.barDockControlLeft.Location = new System.Drawing.Point(0, 0);
            this.barDockControlLeft.Manager = this.barMan;
            this.barDockControlLeft.Margin = new System.Windows.Forms.Padding(4, 4, 4, 4);
            this.barDockControlLeft.Size = new System.Drawing.Size(0, 733);
            // 
            // barDockControlRight
            // 
            this.barDockControlRight.CausesValidation = false;
            this.barDockControlRight.Dock = System.Windows.Forms.DockStyle.Right;
            this.barDockControlRight.Location = new System.Drawing.Point(1129, 0);
            this.barDockControlRight.Manager = this.barMan;
            this.barDockControlRight.Margin = new System.Windows.Forms.Padding(4, 4, 4, 4);
            this.barDockControlRight.Size = new System.Drawing.Size(0, 733);
            // 
            // gcHttpHeadersSet
            // 
            this.gcHttpHeadersSet.CaptionImageOptions.Image = global::OfficeCopilot.Settings.WinForms.Properties.Resources.groupfieldcollection_16x16;
            this.gcHttpHeadersSet.CaptionImageOptions.Padding = new System.Windows.Forms.Padding(3);
            this.gcHttpHeadersSet.Controls.Add(this.gcHttpHeaders);
            this.gcHttpHeadersSet.Dock = System.Windows.Forms.DockStyle.Fill;
            this.gcHttpHeadersSet.Location = new System.Drawing.Point(0, 571);
            this.gcHttpHeadersSet.Margin = new System.Windows.Forms.Padding(4, 4, 4, 4);
            this.gcHttpHeadersSet.Name = "gcHttpHeadersSet";
            this.gcHttpHeadersSet.Size = new System.Drawing.Size(1129, 162);
            this.gcHttpHeadersSet.TabIndex = 12;
            this.gcHttpHeadersSet.Text = "Http请求头设置";
            // 
            // gcHttpHeaders
            // 
            this.gcHttpHeaders.Dock = System.Windows.Forms.DockStyle.Fill;
            this.gcHttpHeaders.EmbeddedNavigator.Margin = new System.Windows.Forms.Padding(4, 4, 4, 4);
            this.gcHttpHeaders.Location = new System.Drawing.Point(2, 31);
            this.gcHttpHeaders.MainView = this.gvHttpHeaders;
            this.gcHttpHeaders.Margin = new System.Windows.Forms.Padding(4, 4, 4, 4);
            this.gcHttpHeaders.Name = "gcHttpHeaders";
            this.gcHttpHeaders.Size = new System.Drawing.Size(1125, 129);
            this.gcHttpHeaders.TabIndex = 1;
            this.gcHttpHeaders.ViewCollection.AddRange(new DevExpress.XtraGrid.Views.Base.BaseView[] {
            this.gvHttpHeaders});
            // 
            // gvHttpHeaders
            // 
            this.gvHttpHeaders.DetailHeight = 437;
            this.gvHttpHeaders.GridControl = this.gcHttpHeaders;
            this.gvHttpHeaders.Name = "gvHttpHeaders";
            this.gvHttpHeaders.OptionsBehavior.AllowAddRows = DevExpress.Utils.DefaultBoolean.True;
            this.gvHttpHeaders.OptionsBehavior.AllowDeleteRows = DevExpress.Utils.DefaultBoolean.True;
            this.gvHttpHeaders.OptionsEditForm.PopupEditFormWidth = 1067;
            this.gvHttpHeaders.OptionsView.NewItemRowPosition = DevExpress.XtraGrid.Views.Grid.NewItemRowPosition.Bottom;
            this.gvHttpHeaders.OptionsView.ShowGroupPanel = false;
            this.gvHttpHeaders.OptionsView.ShowIndicator = false;
            // 
            // fmModelEditor
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(8F, 15F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(1129, 769);
            this.Controls.Add(this.gcHttpHeadersSet);
            this.Controls.Add(this.gcModelParams);
            this.Controls.Add(this.barDockControlLeft);
            this.Controls.Add(this.barDockControlRight);
            this.Controls.Add(this.barDockControlBottom);
            this.Controls.Add(this.barDockControlTop);
            this.Margin = new System.Windows.Forms.Padding(4, 4, 4, 4);
            this.Name = "fmModelEditor";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            this.Text = "fmModelEditor";
            ((System.ComponentModel.ISupportInitialize)(this.gcModelParams)).EndInit();
            this.gcModelParams.ResumeLayout(false);
            this.spcBasicParams.Panel1.ResumeLayout(false);
            this.spcBasicParams.Panel2.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)(this.spcBasicParams)).EndInit();
            this.spcBasicParams.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)(this.txtSystemPrompt.Properties)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.txtModelName.Properties)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.gcAdditionalParameters)).EndInit();
            this.gcAdditionalParameters.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)(this.gcAdditionalParams)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.gvAdditionalParams)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.gcBasicParameters)).EndInit();
            this.gcBasicParameters.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)(this.pgcBasicParams)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.barMan)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.gcHttpHeadersSet)).EndInit();
            this.gcHttpHeadersSet.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)(this.gcHttpHeaders)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.gvHttpHeaders)).EndInit();
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private DevExpress.XtraEditors.GroupControl gcModelParams;
        private System.Windows.Forms.SplitContainer spcBasicParams;
        private DevExpress.XtraEditors.TextEdit txtModelName;
        private DevExpress.XtraEditors.MemoEdit txtSystemPrompt;
        private System.Windows.Forms.Label lineHolder;
        private DevExpress.XtraEditors.GroupControl gcBasicParameters;
        private DevExpress.XtraVerticalGrid.PropertyGridControl pgcBasicParams;
        private DevExpress.XtraEditors.GroupControl gcAdditionalParameters;
        private System.Windows.Forms.Label lineHolder1;
        private DevExpress.XtraGrid.GridControl gcAdditionalParams;
        private DevExpress.XtraGrid.Views.Grid.GridView gvAdditionalParams;
        private DevExpress.XtraBars.BarManager barMan;
        private DevExpress.XtraBars.Bar barModelEdit;
        private DevExpress.XtraBars.BarButtonItem btnTestConn;
        private DevExpress.XtraBars.BarButtonItem btnSave;
        private DevExpress.XtraBars.BarButtonItem btnCancel;
        private DevExpress.XtraBars.BarDockControl barDockControlTop;
        private DevExpress.XtraBars.BarDockControl barDockControlBottom;
        private DevExpress.XtraBars.BarDockControl barDockControlLeft;
        private DevExpress.XtraBars.BarDockControl barDockControlRight;
        private DevExpress.XtraEditors.GroupControl gcHttpHeadersSet;
        private DevExpress.XtraGrid.GridControl gcHttpHeaders;
        private DevExpress.XtraGrid.Views.Grid.GridView gvHttpHeaders;
        private DevExpress.XtraBars.BarCheckItem bciEnableModel;
    }
}