namespace OfficeCopilot.Settings.WinForms
{
    partial class fmBuiltinToolConfig
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
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(fmBuiltinToolConfig));
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
            ((System.ComponentModel.ISupportInitialize)(this.spcTools)).BeginInit();
            this.spcTools.Panel1.SuspendLayout();
            this.spcTools.Panel2.SuspendLayout();
            this.spcTools.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.gcToolsGroup)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.gvToolsGroup)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.gcTools)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.gvTools)).BeginInit();
            this.SuspendLayout();
            // 
            // spcTools
            // 
            this.spcTools.Dock = System.Windows.Forms.DockStyle.Fill;
            this.spcTools.Location = new System.Drawing.Point(0, 0);
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
            this.spcTools.Size = new System.Drawing.Size(1093, 571);
            this.spcTools.SplitterDistance = 284;
            this.spcTools.SplitterWidth = 5;
            this.spcTools.TabIndex = 1;
            // 
            // gcToolsGroup
            // 
            this.gcToolsGroup.Dock = System.Windows.Forms.DockStyle.Fill;
            this.gcToolsGroup.EmbeddedNavigator.Margin = new System.Windows.Forms.Padding(4);
            this.gcToolsGroup.Location = new System.Drawing.Point(0, 0);
            this.gcToolsGroup.MainView = this.gvToolsGroup;
            this.gcToolsGroup.Margin = new System.Windows.Forms.Padding(4);
            this.gcToolsGroup.Name = "gcToolsGroup";
            this.gcToolsGroup.Size = new System.Drawing.Size(284, 571);
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
            this.gcTools.Name = "gcTools";
            this.gcTools.Size = new System.Drawing.Size(804, 571);
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
            // fmBuiltinToolConfig
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(8F, 15F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(1093, 571);
            this.Controls.Add(this.spcTools);
            this.Icon = ((System.Drawing.Icon)(resources.GetObject("$this.Icon")));
            this.Name = "fmBuiltinToolConfig";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            this.Text = "内置工具管理";
            this.spcTools.Panel1.ResumeLayout(false);
            this.spcTools.Panel2.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)(this.spcTools)).EndInit();
            this.spcTools.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)(this.gcToolsGroup)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.gvToolsGroup)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.gcTools)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.gvTools)).EndInit();
            this.ResumeLayout(false);

        }

        #endregion

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
    }
}