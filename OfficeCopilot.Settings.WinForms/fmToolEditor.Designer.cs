namespace OfficeCopilot.Settings.WinForms
{
    partial class fmToolEditor
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
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(fmToolEditor));
            this.xtcToolEditor = new DevExpress.XtraTab.XtraTabControl();
            this.xtpToolEditor = new DevExpress.XtraTab.XtraTabPage();
            this.scMain = new System.Windows.Forms.SplitContainer();
            this.txtDescription = new DevExpress.XtraEditors.MemoEdit();
            this.barMan = new DevExpress.XtraBars.BarManager(this.components);
            this.barModelEdit = new DevExpress.XtraBars.Bar();
            this.btnRunTest = new DevExpress.XtraBars.BarButtonItem();
            this.btnSave = new DevExpress.XtraBars.BarButtonItem();
            this.btnCancel = new DevExpress.XtraBars.BarButtonItem();
            this.bsiInformation = new DevExpress.XtraBars.BarStaticItem();
            this.bar1 = new DevExpress.XtraBars.Bar();
            this.btnCheckSyntax = new DevExpress.XtraBars.BarButtonItem();
            this.btnIndentFormat = new DevExpress.XtraBars.BarButtonItem();
            this.sbdcInputSchemaJson = new DevExpress.XtraBars.StandaloneBarDockControl();
            this.bar2 = new DevExpress.XtraBars.Bar();
            this.btnFormatIndent = new DevExpress.XtraBars.BarButtonItem();
            this.sbdcToolTest = new DevExpress.XtraBars.StandaloneBarDockControl();
            this.barDockControlTop = new DevExpress.XtraBars.BarDockControl();
            this.barDockControlBottom = new DevExpress.XtraBars.BarDockControl();
            this.barDockControlLeft = new DevExpress.XtraBars.BarDockControl();
            this.barDockControlRight = new DevExpress.XtraBars.BarDockControl();
            this.lineHolder2 = new System.Windows.Forms.Label();
            this.txtDisplayName = new DevExpress.XtraEditors.TextEdit();
            this.lineHolder1 = new System.Windows.Forms.Label();
            this.txtName = new DevExpress.XtraEditors.TextEdit();
            this.scSchemaJson = new System.Windows.Forms.SplitContainer();
            this.txtInputSchemaJson = new DevExpress.XtraEditors.MemoEdit();
            this.txtOutputSchemaJson = new DevExpress.XtraEditors.MemoEdit();
            this.xtpToolTester = new DevExpress.XtraTab.XtraTabPage();
            this.scTest = new System.Windows.Forms.SplitContainer();
            this.txtArguments = new DevExpress.XtraEditors.MemoEdit();
            this.txtOutput = new DevExpress.XtraEditors.MemoEdit();
            ((System.ComponentModel.ISupportInitialize)(this.xtcToolEditor)).BeginInit();
            this.xtcToolEditor.SuspendLayout();
            this.xtpToolEditor.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.scMain)).BeginInit();
            this.scMain.Panel1.SuspendLayout();
            this.scMain.Panel2.SuspendLayout();
            this.scMain.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.txtDescription.Properties)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.barMan)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.txtDisplayName.Properties)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.txtName.Properties)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.scSchemaJson)).BeginInit();
            this.scSchemaJson.Panel1.SuspendLayout();
            this.scSchemaJson.Panel2.SuspendLayout();
            this.scSchemaJson.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.txtInputSchemaJson.Properties)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.txtOutputSchemaJson.Properties)).BeginInit();
            this.xtpToolTester.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.scTest)).BeginInit();
            this.scTest.Panel1.SuspendLayout();
            this.scTest.Panel2.SuspendLayout();
            this.scTest.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.txtArguments.Properties)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.txtOutput.Properties)).BeginInit();
            this.SuspendLayout();
            // 
            // xtcToolEditor
            // 
            this.xtcToolEditor.Dock = System.Windows.Forms.DockStyle.Fill;
            this.xtcToolEditor.HeaderLocation = DevExpress.XtraTab.TabHeaderLocation.Right;
            this.xtcToolEditor.HeaderOrientation = DevExpress.XtraTab.TabOrientation.Vertical;
            this.xtcToolEditor.Location = new System.Drawing.Point(0, 0);
            this.xtcToolEditor.Margin = new System.Windows.Forms.Padding(4, 4, 4, 4);
            this.xtcToolEditor.Name = "xtcToolEditor";
            this.xtcToolEditor.SelectedTabPage = this.xtpToolEditor;
            this.xtcToolEditor.Size = new System.Drawing.Size(1067, 526);
            this.xtcToolEditor.TabIndex = 0;
            this.xtcToolEditor.TabPages.AddRange(new DevExpress.XtraTab.XtraTabPage[] {
            this.xtpToolEditor,
            this.xtpToolTester});
            // 
            // xtpToolEditor
            // 
            this.xtpToolEditor.Controls.Add(this.scMain);
            this.xtpToolEditor.Margin = new System.Windows.Forms.Padding(4, 4, 4, 4);
            this.xtpToolEditor.Name = "xtpToolEditor";
            this.xtpToolEditor.Size = new System.Drawing.Size(1030, 520);
            this.xtpToolEditor.Text = "工具定义设置";
            // 
            // scMain
            // 
            this.scMain.Dock = System.Windows.Forms.DockStyle.Fill;
            this.scMain.Location = new System.Drawing.Point(0, 0);
            this.scMain.Margin = new System.Windows.Forms.Padding(4, 4, 4, 4);
            this.scMain.Name = "scMain";
            // 
            // scMain.Panel1
            // 
            this.scMain.Panel1.Controls.Add(this.txtDescription);
            this.scMain.Panel1.Controls.Add(this.lineHolder2);
            this.scMain.Panel1.Controls.Add(this.txtDisplayName);
            this.scMain.Panel1.Controls.Add(this.lineHolder1);
            this.scMain.Panel1.Controls.Add(this.txtName);
            this.scMain.Panel1.Padding = new System.Windows.Forms.Padding(9, 9, 4, 9);
            // 
            // scMain.Panel2
            // 
            this.scMain.Panel2.Controls.Add(this.scSchemaJson);
            this.scMain.Panel2.Controls.Add(this.sbdcInputSchemaJson);
            this.scMain.Panel2.Padding = new System.Windows.Forms.Padding(3, 9, 9, 9);
            this.scMain.Size = new System.Drawing.Size(1030, 520);
            this.scMain.SplitterDistance = 262;
            this.scMain.SplitterWidth = 5;
            this.scMain.TabIndex = 0;
            // 
            // txtDescription
            // 
            this.txtDescription.Dock = System.Windows.Forms.DockStyle.Fill;
            this.txtDescription.Location = new System.Drawing.Point(9, 75);
            this.txtDescription.Margin = new System.Windows.Forms.Padding(4, 4, 4, 4);
            this.txtDescription.MenuManager = this.barMan;
            this.txtDescription.Name = "txtDescription";
            this.txtDescription.Properties.NullValuePrompt = "详细说明";
            this.txtDescription.Size = new System.Drawing.Size(249, 436);
            this.txtDescription.TabIndex = 5;
            // 
            // barMan
            // 
            this.barMan.Bars.AddRange(new DevExpress.XtraBars.Bar[] {
            this.barModelEdit,
            this.bar1,
            this.bar2});
            this.barMan.DockControls.Add(this.barDockControlTop);
            this.barMan.DockControls.Add(this.barDockControlBottom);
            this.barMan.DockControls.Add(this.barDockControlLeft);
            this.barMan.DockControls.Add(this.barDockControlRight);
            this.barMan.DockControls.Add(this.sbdcInputSchemaJson);
            this.barMan.DockControls.Add(this.sbdcToolTest);
            this.barMan.Form = this;
            this.barMan.Items.AddRange(new DevExpress.XtraBars.BarItem[] {
            this.btnSave,
            this.btnCancel,
            this.btnRunTest,
            this.btnCheckSyntax,
            this.btnIndentFormat,
            this.bsiInformation,
            this.btnFormatIndent});
            this.barMan.MaxItemId = 10;
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
            new DevExpress.XtraBars.LinkPersistInfo(DevExpress.XtraBars.BarLinkUserDefines.PaintStyle, this.btnRunTest, DevExpress.XtraBars.BarItemPaintStyle.CaptionGlyph),
            new DevExpress.XtraBars.LinkPersistInfo(DevExpress.XtraBars.BarLinkUserDefines.PaintStyle, this.btnSave, "", true, true, true, 0, null, DevExpress.XtraBars.BarItemPaintStyle.CaptionGlyph),
            new DevExpress.XtraBars.LinkPersistInfo(DevExpress.XtraBars.BarLinkUserDefines.PaintStyle, this.btnCancel, DevExpress.XtraBars.BarItemPaintStyle.CaptionGlyph),
            new DevExpress.XtraBars.LinkPersistInfo(this.bsiInformation, true)});
            this.barModelEdit.OptionsBar.AllowQuickCustomization = false;
            this.barModelEdit.OptionsBar.DrawDragBorder = false;
            this.barModelEdit.OptionsBar.MinHeight = 30;
            this.barModelEdit.OptionsBar.UseWholeRow = true;
            this.barModelEdit.Text = "保存退出";
            // 
            // btnRunTest
            // 
            this.btnRunTest.Alignment = DevExpress.XtraBars.BarItemLinkAlignment.Right;
            this.btnRunTest.Caption = "调用测试";
            this.btnRunTest.Id = 4;
            this.btnRunTest.ImageOptions.Image = global::OfficeCopilot.Settings.WinForms.Properties.Resources.showtestreport_16x16;
            this.btnRunTest.ImageOptions.LargeImage = global::OfficeCopilot.Settings.WinForms.Properties.Resources.showtestreport_32x32;
            this.btnRunTest.Name = "btnRunTest";
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
            // bsiInformation
            // 
            this.bsiInformation.AutoSize = DevExpress.XtraBars.BarStaticItemSize.Spring;
            this.bsiInformation.Border = DevExpress.XtraEditors.Controls.BorderStyles.NoBorder;
            this.bsiInformation.Id = 7;
            this.bsiInformation.Name = "bsiInformation";
            // 
            // bar1
            // 
            this.bar1.BarName = "JsonToolBar";
            this.bar1.DockCol = 0;
            this.bar1.DockRow = 0;
            this.bar1.DockStyle = DevExpress.XtraBars.BarDockStyle.Standalone;
            this.bar1.LinksPersistInfo.AddRange(new DevExpress.XtraBars.LinkPersistInfo[] {
            new DevExpress.XtraBars.LinkPersistInfo(DevExpress.XtraBars.BarLinkUserDefines.PaintStyle, this.btnCheckSyntax, DevExpress.XtraBars.BarItemPaintStyle.CaptionGlyph),
            new DevExpress.XtraBars.LinkPersistInfo(DevExpress.XtraBars.BarLinkUserDefines.PaintStyle, this.btnIndentFormat, "", true, true, true, 0, null, DevExpress.XtraBars.BarItemPaintStyle.CaptionGlyph)});
            this.bar1.OptionsBar.AllowQuickCustomization = false;
            this.bar1.OptionsBar.DisableClose = true;
            this.bar1.OptionsBar.DisableCustomization = true;
            this.bar1.OptionsBar.DrawDragBorder = false;
            this.bar1.OptionsBar.MinHeight = 30;
            this.bar1.OptionsBar.UseWholeRow = true;
            this.bar1.StandaloneBarDockControl = this.sbdcInputSchemaJson;
            this.bar1.Text = "JsonToolBar";
            // 
            // btnCheckSyntax
            // 
            this.btnCheckSyntax.Caption = "检查语法";
            this.btnCheckSyntax.Id = 5;
            this.btnCheckSyntax.ImageOptions.Image = global::OfficeCopilot.Settings.WinForms.Properties.Resources.apply_16x16;
            this.btnCheckSyntax.ImageOptions.LargeImage = global::OfficeCopilot.Settings.WinForms.Properties.Resources.apply_32x32;
            this.btnCheckSyntax.Name = "btnCheckSyntax";
            // 
            // btnIndentFormat
            // 
            this.btnIndentFormat.Caption = "整理缩进";
            this.btnIndentFormat.Id = 6;
            this.btnIndentFormat.ImageOptions.Image = global::OfficeCopilot.Settings.WinForms.Properties.Resources.indentincrease_16x16;
            this.btnIndentFormat.ImageOptions.LargeImage = global::OfficeCopilot.Settings.WinForms.Properties.Resources.indentincrease_32x32;
            this.btnIndentFormat.Name = "btnIndentFormat";
            // 
            // sbdcInputSchemaJson
            // 
            this.sbdcInputSchemaJson.CausesValidation = false;
            this.sbdcInputSchemaJson.Dock = System.Windows.Forms.DockStyle.Top;
            this.sbdcInputSchemaJson.Location = new System.Drawing.Point(3, 9);
            this.sbdcInputSchemaJson.Manager = this.barMan;
            this.sbdcInputSchemaJson.Margin = new System.Windows.Forms.Padding(4, 4, 4, 4);
            this.sbdcInputSchemaJson.Name = "sbdcInputSchemaJson";
            this.sbdcInputSchemaJson.Size = new System.Drawing.Size(751, 38);
            this.sbdcInputSchemaJson.Text = "standaloneBarDockControl1";
            // 
            // bar2
            // 
            this.bar2.BarName = "TestToolBar";
            this.bar2.DockCol = 0;
            this.bar2.DockRow = 0;
            this.bar2.DockStyle = DevExpress.XtraBars.BarDockStyle.Standalone;
            this.bar2.LinksPersistInfo.AddRange(new DevExpress.XtraBars.LinkPersistInfo[] {
            new DevExpress.XtraBars.LinkPersistInfo(DevExpress.XtraBars.BarLinkUserDefines.PaintStyle, this.btnFormatIndent, DevExpress.XtraBars.BarItemPaintStyle.CaptionGlyph)});
            this.bar2.OptionsBar.AllowQuickCustomization = false;
            this.bar2.OptionsBar.DisableClose = true;
            this.bar2.OptionsBar.DisableCustomization = true;
            this.bar2.OptionsBar.DrawDragBorder = false;
            this.bar2.OptionsBar.MinHeight = 30;
            this.bar2.OptionsBar.RotateWhenVertical = false;
            this.bar2.OptionsBar.UseWholeRow = true;
            this.bar2.StandaloneBarDockControl = this.sbdcToolTest;
            this.bar2.Text = "TestToolBar";
            // 
            // btnFormatIndent
            // 
            this.btnFormatIndent.Caption = "整理缩进";
            this.btnFormatIndent.Id = 8;
            this.btnFormatIndent.ImageOptions.Image = global::OfficeCopilot.Settings.WinForms.Properties.Resources.indentincrease_16x161;
            this.btnFormatIndent.ImageOptions.LargeImage = global::OfficeCopilot.Settings.WinForms.Properties.Resources.indentincrease_32x321;
            this.btnFormatIndent.Name = "btnFormatIndent";
            // 
            // sbdcToolTest
            // 
            this.sbdcToolTest.CausesValidation = false;
            this.sbdcToolTest.Dock = System.Windows.Forms.DockStyle.Top;
            this.sbdcToolTest.Location = new System.Drawing.Point(9, 9);
            this.sbdcToolTest.Manager = this.barMan;
            this.sbdcToolTest.Margin = new System.Windows.Forms.Padding(4, 4, 4, 4);
            this.sbdcToolTest.Name = "sbdcToolTest";
            this.sbdcToolTest.Size = new System.Drawing.Size(1012, 38);
            this.sbdcToolTest.Text = "standaloneBarDockControl1";
            // 
            // barDockControlTop
            // 
            this.barDockControlTop.CausesValidation = false;
            this.barDockControlTop.Dock = System.Windows.Forms.DockStyle.Top;
            this.barDockControlTop.Location = new System.Drawing.Point(0, 0);
            this.barDockControlTop.Manager = this.barMan;
            this.barDockControlTop.Margin = new System.Windows.Forms.Padding(4, 4, 4, 4);
            this.barDockControlTop.Size = new System.Drawing.Size(1067, 0);
            // 
            // barDockControlBottom
            // 
            this.barDockControlBottom.CausesValidation = false;
            this.barDockControlBottom.Dock = System.Windows.Forms.DockStyle.Bottom;
            this.barDockControlBottom.Location = new System.Drawing.Point(0, 526);
            this.barDockControlBottom.Manager = this.barMan;
            this.barDockControlBottom.Margin = new System.Windows.Forms.Padding(4, 4, 4, 4);
            this.barDockControlBottom.Size = new System.Drawing.Size(1067, 36);
            // 
            // barDockControlLeft
            // 
            this.barDockControlLeft.CausesValidation = false;
            this.barDockControlLeft.Dock = System.Windows.Forms.DockStyle.Left;
            this.barDockControlLeft.Location = new System.Drawing.Point(0, 0);
            this.barDockControlLeft.Manager = this.barMan;
            this.barDockControlLeft.Margin = new System.Windows.Forms.Padding(4, 4, 4, 4);
            this.barDockControlLeft.Size = new System.Drawing.Size(0, 526);
            // 
            // barDockControlRight
            // 
            this.barDockControlRight.CausesValidation = false;
            this.barDockControlRight.Dock = System.Windows.Forms.DockStyle.Right;
            this.barDockControlRight.Location = new System.Drawing.Point(1067, 0);
            this.barDockControlRight.Manager = this.barMan;
            this.barDockControlRight.Margin = new System.Windows.Forms.Padding(4, 4, 4, 4);
            this.barDockControlRight.Size = new System.Drawing.Size(0, 526);
            // 
            // lineHolder2
            // 
            this.lineHolder2.Dock = System.Windows.Forms.DockStyle.Top;
            this.lineHolder2.Location = new System.Drawing.Point(9, 66);
            this.lineHolder2.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
            this.lineHolder2.Name = "lineHolder2";
            this.lineHolder2.Size = new System.Drawing.Size(249, 9);
            this.lineHolder2.TabIndex = 4;
            this.lineHolder2.Text = " ";
            // 
            // txtDisplayName
            // 
            this.txtDisplayName.Dock = System.Windows.Forms.DockStyle.Top;
            this.txtDisplayName.Location = new System.Drawing.Point(9, 42);
            this.txtDisplayName.Margin = new System.Windows.Forms.Padding(4, 4, 4, 4);
            this.txtDisplayName.MenuManager = this.barMan;
            this.txtDisplayName.Name = "txtDisplayName";
            this.txtDisplayName.Properties.NullValuePrompt = "工具简介";
            this.txtDisplayName.Size = new System.Drawing.Size(249, 24);
            this.txtDisplayName.TabIndex = 3;
            // 
            // lineHolder1
            // 
            this.lineHolder1.Dock = System.Windows.Forms.DockStyle.Top;
            this.lineHolder1.Location = new System.Drawing.Point(9, 33);
            this.lineHolder1.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
            this.lineHolder1.Name = "lineHolder1";
            this.lineHolder1.Size = new System.Drawing.Size(249, 9);
            this.lineHolder1.TabIndex = 2;
            this.lineHolder1.Text = " ";
            // 
            // txtName
            // 
            this.txtName.Dock = System.Windows.Forms.DockStyle.Top;
            this.txtName.Location = new System.Drawing.Point(9, 9);
            this.txtName.Margin = new System.Windows.Forms.Padding(4, 4, 4, 4);
            this.txtName.MenuManager = this.barMan;
            this.txtName.Name = "txtName";
            this.txtName.Properties.NullValuePrompt = "工具名称";
            this.txtName.Properties.ReadOnly = true;
            this.txtName.Size = new System.Drawing.Size(249, 24);
            this.txtName.TabIndex = 0;
            // 
            // scSchemaJson
            // 
            this.scSchemaJson.Dock = System.Windows.Forms.DockStyle.Fill;
            this.scSchemaJson.Location = new System.Drawing.Point(3, 47);
            this.scSchemaJson.Margin = new System.Windows.Forms.Padding(4, 4, 4, 4);
            this.scSchemaJson.Name = "scSchemaJson";
            // 
            // scSchemaJson.Panel1
            // 
            this.scSchemaJson.Panel1.Controls.Add(this.txtInputSchemaJson);
            // 
            // scSchemaJson.Panel2
            // 
            this.scSchemaJson.Panel2.Controls.Add(this.txtOutputSchemaJson);
            this.scSchemaJson.Size = new System.Drawing.Size(751, 464);
            this.scSchemaJson.SplitterDistance = 382;
            this.scSchemaJson.SplitterWidth = 5;
            this.scSchemaJson.TabIndex = 2;
            // 
            // txtInputSchemaJson
            // 
            this.txtInputSchemaJson.Dock = System.Windows.Forms.DockStyle.Fill;
            this.txtInputSchemaJson.Location = new System.Drawing.Point(0, 0);
            this.txtInputSchemaJson.Margin = new System.Windows.Forms.Padding(4, 4, 4, 4);
            this.txtInputSchemaJson.MenuManager = this.barMan;
            this.txtInputSchemaJson.Name = "txtInputSchemaJson";
            this.txtInputSchemaJson.Size = new System.Drawing.Size(382, 464);
            this.txtInputSchemaJson.TabIndex = 0;
            // 
            // txtOutputSchemaJson
            // 
            this.txtOutputSchemaJson.Dock = System.Windows.Forms.DockStyle.Fill;
            this.txtOutputSchemaJson.Location = new System.Drawing.Point(0, 0);
            this.txtOutputSchemaJson.Margin = new System.Windows.Forms.Padding(4, 4, 4, 4);
            this.txtOutputSchemaJson.MenuManager = this.barMan;
            this.txtOutputSchemaJson.Name = "txtOutputSchemaJson";
            this.txtOutputSchemaJson.Size = new System.Drawing.Size(364, 464);
            this.txtOutputSchemaJson.TabIndex = 1;
            // 
            // xtpToolTester
            // 
            this.xtpToolTester.Controls.Add(this.scTest);
            this.xtpToolTester.Controls.Add(this.sbdcToolTest);
            this.xtpToolTester.Margin = new System.Windows.Forms.Padding(4, 4, 4, 4);
            this.xtpToolTester.Name = "xtpToolTester";
            this.xtpToolTester.Padding = new System.Windows.Forms.Padding(9, 9, 9, 9);
            this.xtpToolTester.Size = new System.Drawing.Size(1030, 519);
            this.xtpToolTester.Text = "调用测试";
            // 
            // scTest
            // 
            this.scTest.Dock = System.Windows.Forms.DockStyle.Fill;
            this.scTest.Location = new System.Drawing.Point(9, 47);
            this.scTest.Margin = new System.Windows.Forms.Padding(4, 4, 4, 4);
            this.scTest.Name = "scTest";
            // 
            // scTest.Panel1
            // 
            this.scTest.Panel1.Controls.Add(this.txtArguments);
            // 
            // scTest.Panel2
            // 
            this.scTest.Panel2.Controls.Add(this.txtOutput);
            this.scTest.Size = new System.Drawing.Size(1012, 463);
            this.scTest.SplitterDistance = 516;
            this.scTest.SplitterWidth = 5;
            this.scTest.TabIndex = 1;
            // 
            // txtArguments
            // 
            this.txtArguments.Dock = System.Windows.Forms.DockStyle.Fill;
            this.txtArguments.Location = new System.Drawing.Point(0, 0);
            this.txtArguments.Margin = new System.Windows.Forms.Padding(4, 4, 4, 4);
            this.txtArguments.MenuManager = this.barMan;
            this.txtArguments.Name = "txtArguments";
            this.txtArguments.Size = new System.Drawing.Size(516, 463);
            this.txtArguments.TabIndex = 0;
            // 
            // txtOutput
            // 
            this.txtOutput.Dock = System.Windows.Forms.DockStyle.Fill;
            this.txtOutput.Location = new System.Drawing.Point(0, 0);
            this.txtOutput.Margin = new System.Windows.Forms.Padding(4, 4, 4, 4);
            this.txtOutput.MenuManager = this.barMan;
            this.txtOutput.Name = "txtOutput";
            this.txtOutput.Size = new System.Drawing.Size(491, 463);
            this.txtOutput.TabIndex = 1;
            // 
            // fmToolEditor
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(8F, 15F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(1067, 562);
            this.Controls.Add(this.xtcToolEditor);
            this.Controls.Add(this.barDockControlLeft);
            this.Controls.Add(this.barDockControlRight);
            this.Controls.Add(this.barDockControlBottom);
            this.Controls.Add(this.barDockControlTop);
            this.Margin = new System.Windows.Forms.Padding(4, 4, 4, 4);
            this.Name = "fmToolEditor";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;
            this.Text = "fmToolEditor";
            ((System.ComponentModel.ISupportInitialize)(this.xtcToolEditor)).EndInit();
            this.xtcToolEditor.ResumeLayout(false);
            this.xtpToolEditor.ResumeLayout(false);
            this.scMain.Panel1.ResumeLayout(false);
            this.scMain.Panel2.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)(this.scMain)).EndInit();
            this.scMain.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)(this.txtDescription.Properties)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.barMan)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.txtDisplayName.Properties)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.txtName.Properties)).EndInit();
            this.scSchemaJson.Panel1.ResumeLayout(false);
            this.scSchemaJson.Panel2.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)(this.scSchemaJson)).EndInit();
            this.scSchemaJson.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)(this.txtInputSchemaJson.Properties)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.txtOutputSchemaJson.Properties)).EndInit();
            this.xtpToolTester.ResumeLayout(false);
            this.scTest.Panel1.ResumeLayout(false);
            this.scTest.Panel2.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)(this.scTest)).EndInit();
            this.scTest.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)(this.txtArguments.Properties)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.txtOutput.Properties)).EndInit();
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private DevExpress.XtraTab.XtraTabControl xtcToolEditor;
        private DevExpress.XtraTab.XtraTabPage xtpToolEditor;
        private DevExpress.XtraTab.XtraTabPage xtpToolTester;
        private DevExpress.XtraBars.BarManager barMan;
        private DevExpress.XtraBars.Bar barModelEdit;
        private DevExpress.XtraBars.BarButtonItem btnSave;
        private DevExpress.XtraBars.BarButtonItem btnCancel;
        private DevExpress.XtraBars.BarButtonItem btnRunTest;
        private DevExpress.XtraBars.BarDockControl barDockControlTop;
        private DevExpress.XtraBars.BarDockControl barDockControlBottom;
        private DevExpress.XtraBars.BarDockControl barDockControlLeft;
        private DevExpress.XtraBars.BarDockControl barDockControlRight;
        private System.Windows.Forms.SplitContainer scMain;
        private DevExpress.XtraEditors.TextEdit txtName;
        private DevExpress.XtraEditors.MemoEdit txtDescription;
        private System.Windows.Forms.Label lineHolder2;
        private DevExpress.XtraEditors.TextEdit txtDisplayName;
        private System.Windows.Forms.Label lineHolder1;
        private DevExpress.XtraBars.Bar bar1;
        private DevExpress.XtraBars.StandaloneBarDockControl sbdcInputSchemaJson;
        private System.Windows.Forms.SplitContainer scSchemaJson;
        private DevExpress.XtraBars.BarButtonItem btnCheckSyntax;
        private DevExpress.XtraEditors.MemoEdit txtInputSchemaJson;
        private DevExpress.XtraEditors.MemoEdit txtOutputSchemaJson;
        private DevExpress.XtraBars.BarButtonItem btnIndentFormat;
        private DevExpress.XtraBars.BarStaticItem bsiInformation;
        private DevExpress.XtraBars.Bar bar2;
        private DevExpress.XtraBars.StandaloneBarDockControl sbdcToolTest;
        private System.Windows.Forms.SplitContainer scTest;
        private DevExpress.XtraEditors.MemoEdit txtArguments;
        private DevExpress.XtraEditors.MemoEdit txtOutput;
        private DevExpress.XtraBars.BarButtonItem btnFormatIndent;
    }
}