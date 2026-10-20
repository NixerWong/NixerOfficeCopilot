namespace OfficeCopilot.Panel.WebView
{
    partial class CopilotPanelControl
    {
        /// <summary> 
        /// 必需的设计器变量。
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary> 
        /// 清理所有正在使用的资源。
        /// </summary>
        /// <param name="disposing">如果应释放托管资源，为 true；否则为 false。</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region 组件设计器生成的代码

        /// <summary> 
        /// 设计器支持所需的方法 - 不要修改
        /// 使用代码编辑器修改此方法的内容。
        /// </summary>
        private void InitializeComponent()
        {
            this.components = new System.ComponentModel.Container();
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(CopilotPanelControl));
            this.barManager = new DevExpress.XtraBars.BarManager(this.components);
            this.topBar = new DevExpress.XtraBars.Bar();
            this.listHistoryChat = new DevExpress.XtraBars.BarEditItem();
            this.riicbHistoryChat = new DevExpress.XtraEditors.Repository.RepositoryItemImageComboBox();
            this.icItems = new DevExpress.Utils.ImageCollection(this.components);
            this.btnNewChat = new DevExpress.XtraBars.BarButtonItem();
            this.btnDelChat = new DevExpress.XtraBars.BarButtonItem();
            this.panel2TopBar = new DevExpress.XtraBars.Bar();
            this.menuAddRef = new DevExpress.XtraBars.BarSubItem();
            this.btnSelectFiles = new DevExpress.XtraBars.BarButtonItem();
            this.menuMruRefs = new DevExpress.XtraBars.BarSubItem();
            this.sbdcAttachmentMenu = new DevExpress.XtraBars.StandaloneBarDockControl();
            this.panel2BottomBar = new DevExpress.XtraBars.Bar();
            this.menuModels = new DevExpress.XtraBars.BarSubItem();
            this.menuInteractiveMode = new DevExpress.XtraBars.BarSubItem();
            this.btnAgentType = new DevExpress.XtraBars.BarButtonItem();
            this.btnAsk = new DevExpress.XtraBars.BarButtonItem();
            this.btnSend = new DevExpress.XtraBars.BarButtonItem();
            this.panel2BottomDock = new DevExpress.XtraBars.StandaloneBarDockControl();
            this.barDockControlTop = new DevExpress.XtraBars.BarDockControl();
            this.barDockControlBottom = new DevExpress.XtraBars.BarDockControl();
            this.barDockControlLeft = new DevExpress.XtraBars.BarDockControl();
            this.barDockControlRight = new DevExpress.XtraBars.BarDockControl();
            this.menuRefLinks = new DevExpress.XtraBars.BarSubItem();
            this.beiInputWebUrl = new DevExpress.XtraBars.BarEditItem();
            this.ribeWebUrl = new DevExpress.XtraEditors.Repository.RepositoryItemButtonEdit();
            this.riteRefItems = new DevExpress.XtraEditors.Repository.RepositoryItemTokenEdit();
            this.spcMain = new System.Windows.Forms.SplitContainer();
            this.wvConversation = new Microsoft.Web.WebView2.WinForms.WebView2();
            this.panelChat = new DevExpress.XtraEditors.PanelControl();
            this.txtChat = new DevExpress.XtraEditors.MemoEdit();
            this.flpAttachments = new System.Windows.Forms.FlowLayoutPanel();
            ((System.ComponentModel.ISupportInitialize)(this.barManager)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.riicbHistoryChat)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.icItems)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.ribeWebUrl)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.riteRefItems)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.spcMain)).BeginInit();
            this.spcMain.Panel1.SuspendLayout();
            this.spcMain.Panel2.SuspendLayout();
            this.spcMain.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.wvConversation)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.panelChat)).BeginInit();
            this.panelChat.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.txtChat.Properties)).BeginInit();
            this.SuspendLayout();
            // 
            // barManager
            // 
            this.barManager.Bars.AddRange(new DevExpress.XtraBars.Bar[] {
            this.topBar,
            this.panel2TopBar,
            this.panel2BottomBar});
            this.barManager.DockControls.Add(this.barDockControlTop);
            this.barManager.DockControls.Add(this.barDockControlBottom);
            this.barManager.DockControls.Add(this.barDockControlLeft);
            this.barManager.DockControls.Add(this.barDockControlRight);
            this.barManager.DockControls.Add(this.panel2BottomDock);
            this.barManager.DockControls.Add(this.sbdcAttachmentMenu);
            this.barManager.DockWindowTabFont = new System.Drawing.Font("宋体", 7.2F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(134)));
            this.barManager.Form = this;
            this.barManager.Items.AddRange(new DevExpress.XtraBars.BarItem[] {
            this.listHistoryChat,
            this.btnNewChat,
            this.btnDelChat,
            this.menuAddRef,
            this.menuModels,
            this.menuInteractiveMode,
            this.btnSend,
            this.btnAgentType,
            this.btnAsk,
            this.menuRefLinks,
            this.btnSelectFiles,
            this.beiInputWebUrl,
            this.menuMruRefs});
            this.barManager.MaxItemId = 21;
            this.barManager.RepositoryItems.AddRange(new DevExpress.XtraEditors.Repository.RepositoryItem[] {
            this.riicbHistoryChat,
            this.riteRefItems,
            this.ribeWebUrl});
            // 
            // topBar
            // 
            this.topBar.BarName = "Tools";
            this.topBar.CanDockStyle = DevExpress.XtraBars.BarCanDockStyle.Top;
            this.topBar.DockCol = 0;
            this.topBar.DockRow = 0;
            this.topBar.DockStyle = DevExpress.XtraBars.BarDockStyle.Top;
            this.topBar.LinksPersistInfo.AddRange(new DevExpress.XtraBars.LinkPersistInfo[] {
            new DevExpress.XtraBars.LinkPersistInfo(this.listHistoryChat),
            new DevExpress.XtraBars.LinkPersistInfo(this.btnNewChat, true),
            new DevExpress.XtraBars.LinkPersistInfo(this.btnDelChat)});
            this.topBar.OptionsBar.AllowQuickCustomization = false;
            this.topBar.OptionsBar.DisableClose = true;
            this.topBar.OptionsBar.DisableCustomization = true;
            this.topBar.OptionsBar.DrawDragBorder = false;
            this.topBar.OptionsBar.MinHeight = 30;
            this.topBar.OptionsBar.RotateWhenVertical = false;
            this.topBar.OptionsBar.UseWholeRow = true;
            this.topBar.Text = "Tools";
            // 
            // listHistoryChat
            // 
            this.listHistoryChat.AutoFillWidth = true;
            this.listHistoryChat.Caption = "历史会话";
            this.listHistoryChat.Edit = this.riicbHistoryChat;
            this.listHistoryChat.Id = 1;
            this.listHistoryChat.Name = "listHistoryChat";
            // 
            // riicbHistoryChat
            // 
            this.riicbHistoryChat.AutoHeight = false;
            this.riicbHistoryChat.Buttons.AddRange(new DevExpress.XtraEditors.Controls.EditorButton[] {
            new DevExpress.XtraEditors.Controls.EditorButton(DevExpress.XtraEditors.Controls.ButtonPredefines.Combo)});
            this.riicbHistoryChat.Name = "riicbHistoryChat";
            this.riicbHistoryChat.Padding = new System.Windows.Forms.Padding(3);
            this.riicbHistoryChat.SmallImages = this.icItems;
            // 
            // icItems
            // 
            this.icItems.ImageStream = ((DevExpress.Utils.ImageCollectionStreamer)(resources.GetObject("icItems.ImageStream")));
            this.icItems.Images.SetKeyName(0, "message_16x16.png");
            // 
            // btnNewChat
            // 
            this.btnNewChat.Caption = "新建会话";
            this.btnNewChat.Id = 2;
            this.btnNewChat.ImageOptions.Image = ((System.Drawing.Image)(resources.GetObject("btnNewChat.ImageOptions.Image")));
            this.btnNewChat.ImageOptions.LargeImage = ((System.Drawing.Image)(resources.GetObject("btnNewChat.ImageOptions.LargeImage")));
            this.btnNewChat.Name = "btnNewChat";
            this.btnNewChat.Size = new System.Drawing.Size(30, 0);
            // 
            // btnDelChat
            // 
            this.btnDelChat.Caption = "删除会话";
            this.btnDelChat.Id = 3;
            this.btnDelChat.ImageOptions.Image = ((System.Drawing.Image)(resources.GetObject("btnDelChat.ImageOptions.Image")));
            this.btnDelChat.ImageOptions.LargeImage = ((System.Drawing.Image)(resources.GetObject("btnDelChat.ImageOptions.LargeImage")));
            this.btnDelChat.Name = "btnDelChat";
            this.btnDelChat.Size = new System.Drawing.Size(30, 0);
            // 
            // panel2TopBar
            // 
            this.panel2TopBar.BarName = "panel2TopBar";
            this.panel2TopBar.DockCol = 0;
            this.panel2TopBar.DockRow = 0;
            this.panel2TopBar.DockStyle = DevExpress.XtraBars.BarDockStyle.Standalone;
            this.panel2TopBar.LinksPersistInfo.AddRange(new DevExpress.XtraBars.LinkPersistInfo[] {
            new DevExpress.XtraBars.LinkPersistInfo(DevExpress.XtraBars.BarLinkUserDefines.PaintStyle, this.menuAddRef, "", true, true, true, 0, null, DevExpress.XtraBars.BarItemPaintStyle.CaptionGlyph)});
            this.panel2TopBar.OptionsBar.AllowQuickCustomization = false;
            this.panel2TopBar.OptionsBar.DisableClose = true;
            this.panel2TopBar.OptionsBar.DisableCustomization = true;
            this.panel2TopBar.OptionsBar.DrawDragBorder = false;
            this.panel2TopBar.OptionsBar.MinHeight = 44;
            this.panel2TopBar.OptionsBar.RotateWhenVertical = false;
            this.panel2TopBar.OptionsBar.UseWholeRow = true;
            this.panel2TopBar.StandaloneBarDockControl = this.sbdcAttachmentMenu;
            this.panel2TopBar.Text = "panel2TopBar";
            // 
            // menuAddRef
            // 
            this.menuAddRef.Id = 5;
            this.menuAddRef.ImageOptions.Image = ((System.Drawing.Image)(resources.GetObject("menuAddRef.ImageOptions.Image")));
            this.menuAddRef.ImageOptions.LargeImage = ((System.Drawing.Image)(resources.GetObject("menuAddRef.ImageOptions.LargeImage")));
            this.menuAddRef.LinksPersistInfo.AddRange(new DevExpress.XtraBars.LinkPersistInfo[] {
            new DevExpress.XtraBars.LinkPersistInfo(this.btnSelectFiles),
            new DevExpress.XtraBars.LinkPersistInfo(this.menuMruRefs, true)});
            this.menuAddRef.Name = "menuAddRef";
            // 
            // btnSelectFiles
            // 
            this.btnSelectFiles.Caption = "文件";
            this.btnSelectFiles.Id = 17;
            this.btnSelectFiles.ImageOptions.Image = global::OfficeCopilot.Panel.WebView.Properties.Resources.article_16x161;
            this.btnSelectFiles.ImageOptions.LargeImage = global::OfficeCopilot.Panel.WebView.Properties.Resources.article_32x321;
            this.btnSelectFiles.Name = "btnSelectFiles";
            // 
            // menuMruRefs
            // 
            this.menuMruRefs.Caption = "最近访问";
            this.menuMruRefs.Id = 19;
            this.menuMruRefs.Name = "menuMruRefs";
            // 
            // sbdcAttachmentMenu
            // 
            this.sbdcAttachmentMenu.CausesValidation = false;
            this.sbdcAttachmentMenu.Controls.Add(this.flpAttachments);
            this.sbdcAttachmentMenu.Dock = System.Windows.Forms.DockStyle.Top;
            this.sbdcAttachmentMenu.Location = new System.Drawing.Point(0, 0);
            this.sbdcAttachmentMenu.Manager = this.barManager;
            this.sbdcAttachmentMenu.Name = "sbdcAttachmentMenu";
            this.sbdcAttachmentMenu.Size = new System.Drawing.Size(632, 44);
            this.sbdcAttachmentMenu.Text = "standaloneBarDockControl1";
            // 
            // panel2BottomBar
            // 
            this.panel2BottomBar.BarName = "panel2BottomBar";
            this.panel2BottomBar.DockCol = 0;
            this.panel2BottomBar.DockRow = 0;
            this.panel2BottomBar.DockStyle = DevExpress.XtraBars.BarDockStyle.Standalone;
            this.panel2BottomBar.FloatLocation = new System.Drawing.Point(380, 782);
            this.panel2BottomBar.LinksPersistInfo.AddRange(new DevExpress.XtraBars.LinkPersistInfo[] {
            new DevExpress.XtraBars.LinkPersistInfo(DevExpress.XtraBars.BarLinkUserDefines.PaintStyle, this.menuModels, DevExpress.XtraBars.BarItemPaintStyle.CaptionGlyph),
            new DevExpress.XtraBars.LinkPersistInfo(DevExpress.XtraBars.BarLinkUserDefines.PaintStyle, this.menuInteractiveMode, DevExpress.XtraBars.BarItemPaintStyle.CaptionGlyph),
            new DevExpress.XtraBars.LinkPersistInfo(this.btnSend, true)});
            this.panel2BottomBar.OptionsBar.AllowQuickCustomization = false;
            this.panel2BottomBar.OptionsBar.DisableClose = true;
            this.panel2BottomBar.OptionsBar.DisableCustomization = true;
            this.panel2BottomBar.OptionsBar.DrawDragBorder = false;
            this.panel2BottomBar.OptionsBar.MinHeight = 30;
            this.panel2BottomBar.OptionsBar.UseWholeRow = true;
            this.panel2BottomBar.StandaloneBarDockControl = this.panel2BottomDock;
            this.panel2BottomBar.Text = "panel2BottomBar";
            // 
            // menuModels
            // 
            this.menuModels.Caption = "请先设置模型";
            this.menuModels.Id = 7;
            this.menuModels.ImageOptions.Image = ((System.Drawing.Image)(resources.GetObject("menuModels.ImageOptions.Image")));
            this.menuModels.ImageOptions.LargeImage = ((System.Drawing.Image)(resources.GetObject("menuModels.ImageOptions.LargeImage")));
            this.menuModels.Name = "menuModels";
            // 
            // menuInteractiveMode
            // 
            this.menuInteractiveMode.Caption = "Agent";
            this.menuInteractiveMode.Id = 8;
            this.menuInteractiveMode.ImageOptions.Image = ((System.Drawing.Image)(resources.GetObject("menuInteractiveMode.ImageOptions.Image")));
            this.menuInteractiveMode.ImageOptions.LargeImage = ((System.Drawing.Image)(resources.GetObject("menuInteractiveMode.ImageOptions.LargeImage")));
            this.menuInteractiveMode.ImageOptions.SvgImageSize = new System.Drawing.Size(16, 16);
            this.menuInteractiveMode.LinksPersistInfo.AddRange(new DevExpress.XtraBars.LinkPersistInfo[] {
            new DevExpress.XtraBars.LinkPersistInfo(this.btnAgentType),
            new DevExpress.XtraBars.LinkPersistInfo(this.btnAsk)});
            this.menuInteractiveMode.Name = "menuInteractiveMode";
            // 
            // btnAgentType
            // 
            this.btnAgentType.Caption = "Agent";
            this.btnAgentType.Id = 11;
            this.btnAgentType.ImageOptions.Image = ((System.Drawing.Image)(resources.GetObject("btnAgentType.ImageOptions.Image")));
            this.btnAgentType.ImageOptions.LargeImage = ((System.Drawing.Image)(resources.GetObject("btnAgentType.ImageOptions.LargeImage")));
            this.btnAgentType.Name = "btnAgentType";
            // 
            // btnAsk
            // 
            this.btnAsk.Caption = "Ask";
            this.btnAsk.Id = 12;
            this.btnAsk.ImageOptions.Image = ((System.Drawing.Image)(resources.GetObject("btnAsk.ImageOptions.Image")));
            this.btnAsk.ImageOptions.LargeImage = ((System.Drawing.Image)(resources.GetObject("btnAsk.ImageOptions.LargeImage")));
            this.btnAsk.Name = "btnAsk";
            // 
            // btnSend
            // 
            this.btnSend.Alignment = DevExpress.XtraBars.BarItemLinkAlignment.Right;
            this.btnSend.Caption = "发送";
            this.btnSend.Id = 10;
            this.btnSend.ImageOptions.SvgImage = ((DevExpress.Utils.Svg.SvgImage)(resources.GetObject("btnSend.ImageOptions.SvgImage")));
            this.btnSend.ImageOptions.SvgImageSize = new System.Drawing.Size(18, 16);
            this.btnSend.Name = "btnSend";
            this.btnSend.Size = new System.Drawing.Size(30, 0);
            // 
            // panel2BottomDock
            // 
            this.panel2BottomDock.CausesValidation = false;
            this.panel2BottomDock.Dock = System.Windows.Forms.DockStyle.Bottom;
            this.panel2BottomDock.Location = new System.Drawing.Point(0, 121);
            this.panel2BottomDock.Manager = this.barManager;
            this.panel2BottomDock.Margin = new System.Windows.Forms.Padding(4);
            this.panel2BottomDock.Name = "panel2BottomDock";
            this.panel2BottomDock.Size = new System.Drawing.Size(632, 38);
            this.panel2BottomDock.Text = "standaloneBarDockControl2";
            // 
            // barDockControlTop
            // 
            this.barDockControlTop.CausesValidation = false;
            this.barDockControlTop.Dock = System.Windows.Forms.DockStyle.Top;
            this.barDockControlTop.Location = new System.Drawing.Point(0, 0);
            this.barDockControlTop.Manager = this.barManager;
            this.barDockControlTop.Margin = new System.Windows.Forms.Padding(4);
            this.barDockControlTop.Size = new System.Drawing.Size(632, 47);
            // 
            // barDockControlBottom
            // 
            this.barDockControlBottom.CausesValidation = false;
            this.barDockControlBottom.Dock = System.Windows.Forms.DockStyle.Bottom;
            this.barDockControlBottom.Location = new System.Drawing.Point(0, 890);
            this.barDockControlBottom.Manager = this.barManager;
            this.barDockControlBottom.Margin = new System.Windows.Forms.Padding(4);
            this.barDockControlBottom.Size = new System.Drawing.Size(632, 0);
            // 
            // barDockControlLeft
            // 
            this.barDockControlLeft.CausesValidation = false;
            this.barDockControlLeft.Dock = System.Windows.Forms.DockStyle.Left;
            this.barDockControlLeft.Location = new System.Drawing.Point(0, 47);
            this.barDockControlLeft.Manager = this.barManager;
            this.barDockControlLeft.Margin = new System.Windows.Forms.Padding(4);
            this.barDockControlLeft.Size = new System.Drawing.Size(0, 843);
            // 
            // barDockControlRight
            // 
            this.barDockControlRight.CausesValidation = false;
            this.barDockControlRight.Dock = System.Windows.Forms.DockStyle.Right;
            this.barDockControlRight.Location = new System.Drawing.Point(632, 47);
            this.barDockControlRight.Manager = this.barManager;
            this.barDockControlRight.Margin = new System.Windows.Forms.Padding(4);
            this.barDockControlRight.Size = new System.Drawing.Size(0, 843);
            // 
            // menuRefLinks
            // 
            this.menuRefLinks.Caption = "链接";
            this.menuRefLinks.Id = 15;
            this.menuRefLinks.LinksPersistInfo.AddRange(new DevExpress.XtraBars.LinkPersistInfo[] {
            new DevExpress.XtraBars.LinkPersistInfo(DevExpress.XtraBars.BarLinkUserDefines.Width, this.beiInputWebUrl, "", false, true, true, 221)});
            this.menuRefLinks.Name = "menuRefLinks";
            // 
            // beiInputWebUrl
            // 
            this.beiInputWebUrl.Caption = "网址：";
            this.beiInputWebUrl.Edit = this.ribeWebUrl;
            this.beiInputWebUrl.Id = 18;
            this.beiInputWebUrl.ImageOptions.Image = global::OfficeCopilot.Panel.WebView.Properties.Resources.hyperlink_16x16;
            this.beiInputWebUrl.ImageOptions.LargeImage = global::OfficeCopilot.Panel.WebView.Properties.Resources.hyperlink_32x32;
            this.beiInputWebUrl.Name = "beiInputWebUrl";
            // 
            // ribeWebUrl
            // 
            this.ribeWebUrl.AutoHeight = false;
            this.ribeWebUrl.Buttons.AddRange(new DevExpress.XtraEditors.Controls.EditorButton[] {
            new DevExpress.XtraEditors.Controls.EditorButton(DevExpress.XtraEditors.Controls.ButtonPredefines.Plus)});
            this.ribeWebUrl.Name = "ribeWebUrl";
            // 
            // riteRefItems
            // 
            this.riteRefItems.Appearance.BackColor = System.Drawing.Color.Transparent;
            this.riteRefItems.Appearance.Options.UseBackColor = true;
            this.riteRefItems.BorderStyle = DevExpress.XtraEditors.Controls.BorderStyles.NoBorder;
            this.riteRefItems.Name = "riteRefItems";
            // 
            // spcMain
            // 
            this.spcMain.Dock = System.Windows.Forms.DockStyle.Fill;
            this.spcMain.FixedPanel = System.Windows.Forms.FixedPanel.Panel2;
            this.spcMain.Location = new System.Drawing.Point(0, 47);
            this.spcMain.Margin = new System.Windows.Forms.Padding(4);
            this.spcMain.Name = "spcMain";
            this.spcMain.Orientation = System.Windows.Forms.Orientation.Horizontal;
            // 
            // spcMain.Panel1
            // 
            this.spcMain.Panel1.Controls.Add(this.wvConversation);
            // 
            // spcMain.Panel2
            // 
            this.spcMain.Panel2.Controls.Add(this.panelChat);
            this.spcMain.Panel2.Controls.Add(this.sbdcAttachmentMenu);
            this.spcMain.Panel2.Controls.Add(this.panel2BottomDock);
            this.spcMain.Size = new System.Drawing.Size(632, 843);
            this.spcMain.SplitterDistance = 683;
            this.spcMain.SplitterWidth = 1;
            this.spcMain.TabIndex = 4;
            // 
            // wvConversation
            // 
            this.wvConversation.AllowExternalDrop = false;
            this.wvConversation.CreationProperties = null;
            this.wvConversation.DefaultBackgroundColor = System.Drawing.Color.White;
            this.wvConversation.Dock = System.Windows.Forms.DockStyle.Fill;
            this.wvConversation.Location = new System.Drawing.Point(0, 0);
            this.wvConversation.Margin = new System.Windows.Forms.Padding(4);
            this.wvConversation.Name = "wvConversation";
            this.wvConversation.Size = new System.Drawing.Size(632, 683);
            this.wvConversation.TabIndex = 0;
            this.wvConversation.ZoomFactor = 1D;
            // 
            // panelChat
            // 
            this.panelChat.Appearance.BackColor = System.Drawing.Color.White;
            this.panelChat.Appearance.Options.UseBackColor = true;
            this.panelChat.BorderStyle = DevExpress.XtraEditors.Controls.BorderStyles.NoBorder;
            this.panelChat.Controls.Add(this.txtChat);
            this.panelChat.Dock = System.Windows.Forms.DockStyle.Fill;
            this.panelChat.Location = new System.Drawing.Point(0, 44);
            this.panelChat.Margin = new System.Windows.Forms.Padding(4);
            this.panelChat.Name = "panelChat";
            this.panelChat.Padding = new System.Windows.Forms.Padding(4, 4, 0, 4);
            this.panelChat.Size = new System.Drawing.Size(632, 77);
            this.panelChat.TabIndex = 6;
            // 
            // txtChat
            // 
            this.txtChat.Dock = System.Windows.Forms.DockStyle.Fill;
            this.txtChat.Location = new System.Drawing.Point(4, 4);
            this.txtChat.Margin = new System.Windows.Forms.Padding(4);
            this.txtChat.MenuManager = this.barManager;
            this.txtChat.Name = "txtChat";
            this.txtChat.Properties.BorderStyle = DevExpress.XtraEditors.Controls.BorderStyles.NoBorder;
            this.txtChat.Size = new System.Drawing.Size(628, 69);
            this.txtChat.TabIndex = 4;
            // 
            // flpAttachments
            // 
            this.flpAttachments.Location = new System.Drawing.Point(170, 0);
            this.flpAttachments.Name = "flpAttachments";
            this.flpAttachments.Size = new System.Drawing.Size(462, 44);
            this.flpAttachments.TabIndex = 0;
            // 
            // CopilotPanelControl
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(8F, 15F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.Controls.Add(this.spcMain);
            this.Controls.Add(this.barDockControlLeft);
            this.Controls.Add(this.barDockControlRight);
            this.Controls.Add(this.barDockControlBottom);
            this.Controls.Add(this.barDockControlTop);
            this.Margin = new System.Windows.Forms.Padding(4);
            this.Name = "CopilotPanelControl";
            this.Size = new System.Drawing.Size(632, 890);
            ((System.ComponentModel.ISupportInitialize)(this.barManager)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.riicbHistoryChat)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.icItems)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.ribeWebUrl)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.riteRefItems)).EndInit();
            this.spcMain.Panel1.ResumeLayout(false);
            this.spcMain.Panel2.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)(this.spcMain)).EndInit();
            this.spcMain.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)(this.wvConversation)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.panelChat)).EndInit();
            this.panelChat.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)(this.txtChat.Properties)).EndInit();
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private DevExpress.XtraBars.BarManager barManager;
        private DevExpress.XtraBars.Bar topBar;
        private DevExpress.XtraBars.BarDockControl barDockControlTop;
        private DevExpress.XtraBars.BarDockControl barDockControlBottom;
        private DevExpress.XtraBars.BarDockControl barDockControlLeft;
        private DevExpress.XtraBars.BarDockControl barDockControlRight;
        private DevExpress.XtraBars.BarEditItem listHistoryChat;
        private DevExpress.XtraEditors.Repository.RepositoryItemImageComboBox riicbHistoryChat;
        private DevExpress.XtraBars.BarButtonItem btnNewChat;
        private System.Windows.Forms.SplitContainer spcMain;
        private Microsoft.Web.WebView2.WinForms.WebView2 wvConversation;
        private DevExpress.XtraBars.BarButtonItem btnDelChat;
        private DevExpress.XtraBars.StandaloneBarDockControl panel2BottomDock;
        private DevExpress.XtraBars.Bar panel2TopBar;
        private DevExpress.XtraBars.Bar panel2BottomBar;
        private DevExpress.XtraBars.BarSubItem menuAddRef;
        private DevExpress.XtraEditors.Repository.RepositoryItemTokenEdit riteRefItems;
        private DevExpress.XtraBars.BarSubItem menuModels;
        private DevExpress.XtraBars.BarSubItem menuInteractiveMode;
        private DevExpress.XtraBars.BarButtonItem btnSend;
        private DevExpress.XtraBars.BarButtonItem btnAgentType;
        private DevExpress.XtraBars.BarButtonItem btnAsk;
        private DevExpress.Utils.ImageCollection icItems;
        private DevExpress.XtraBars.BarSubItem menuRefLinks;
        private DevExpress.XtraBars.BarButtonItem btnSelectFiles;
        private DevExpress.XtraBars.BarEditItem beiInputWebUrl;
        private DevExpress.XtraEditors.Repository.RepositoryItemButtonEdit ribeWebUrl;
        private DevExpress.XtraBars.BarSubItem menuMruRefs;
        private DevExpress.XtraBars.StandaloneBarDockControl sbdcAttachmentMenu;
        private System.Windows.Forms.FlowLayoutPanel flpAttachments;
        private DevExpress.XtraEditors.PanelControl panelChat;
        private DevExpress.XtraEditors.MemoEdit txtChat;
    }
}
