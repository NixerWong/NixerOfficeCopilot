namespace NixerWordCopilot
{
    partial class WordCopilotRibbon : Microsoft.Office.Tools.Ribbon.RibbonBase
    {
        private System.ComponentModel.IContainer components = null;

        public WordCopilotRibbon()
            : base(Globals.Factory.GetRibbonFactory())
        {
            InitializeComponent();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && components != null)
            {
                components.Dispose();
            }

            base.Dispose(disposing);
        }

        private void InitializeComponent()
        {
            this.tabCopilot = this.Factory.CreateRibbonTab();
            this.groupCopilot = this.Factory.CreateRibbonGroup();
            this.btnTogglePane = this.Factory.CreateRibbonButton();
            this.spbar1 = this.Factory.CreateRibbonSeparator();
            this.btnModelConfig = this.Factory.CreateRibbonButton();
            this.btnMcpConfig = this.Factory.CreateRibbonButton();
            this.btnSkillsConfig = this.Factory.CreateRibbonButton();
            this.btnMemoryConfig = this.Factory.CreateRibbonButton();
            this.btnBuiltinToolsConfig = this.Factory.CreateRibbonButton();
            this.spbar2 = this.Factory.CreateRibbonSeparator();
            this.btnConfig = this.Factory.CreateRibbonButton();
            this.spbar3 = this.Factory.CreateRibbonSeparator();
            this.btnAbout = this.Factory.CreateRibbonButton();
            this.tabCopilot.SuspendLayout();
            this.groupCopilot.SuspendLayout();
            this.SuspendLayout();
            // 
            // tabCopilot
            // 
            this.tabCopilot.Groups.Add(this.groupCopilot);
            this.tabCopilot.Label = "Nixer Copilot";
            this.tabCopilot.Name = "tabCopilot";
            // 
            // groupCopilot
            // 
            this.groupCopilot.Items.Add(this.btnTogglePane);
            this.groupCopilot.Items.Add(this.spbar1);
            this.groupCopilot.Items.Add(this.btnModelConfig);
            this.groupCopilot.Items.Add(this.btnMcpConfig);
            this.groupCopilot.Items.Add(this.btnSkillsConfig);
            this.groupCopilot.Items.Add(this.btnMemoryConfig);
            this.groupCopilot.Items.Add(this.btnBuiltinToolsConfig);
            this.groupCopilot.Items.Add(this.spbar2);
            this.groupCopilot.Items.Add(this.btnConfig);
            this.groupCopilot.Items.Add(this.spbar3);
            this.groupCopilot.Items.Add(this.btnAbout);
            this.groupCopilot.Label = "Word 助手";
            this.groupCopilot.Name = "groupCopilot";
            // 
            // btnTogglePane
            // 
            this.btnTogglePane.ControlSize = Microsoft.Office.Core.RibbonControlSize.RibbonControlSizeLarge;
            this.btnTogglePane.Image = global::NixerWordCopilot.Properties.Resources.ShowPanel;
            this.btnTogglePane.Label = "显示面板";
            this.btnTogglePane.Name = "btnTogglePane";
            this.btnTogglePane.ShowImage = true;
            this.btnTogglePane.Click += new Microsoft.Office.Tools.Ribbon.RibbonControlEventHandler(this.btnTogglePane_Click);
            // 
            // spbar1
            // 
            this.spbar1.Name = "spbar1";
            // 
            // btnModelConfig
            // 
            this.btnModelConfig.ControlSize = Microsoft.Office.Core.RibbonControlSize.RibbonControlSizeLarge;
            this.btnModelConfig.Image = global::NixerWordCopilot.Properties.Resources.Suggestion_32x32;
            this.btnModelConfig.Label = "模型管理";
            this.btnModelConfig.Name = "btnModelConfig";
            this.btnModelConfig.ShowImage = true;
            this.btnModelConfig.Click += new Microsoft.Office.Tools.Ribbon.RibbonControlEventHandler(this.btnModelConfig_Click);
            // 
            // btnMcpConfig
            // 
            this.btnMcpConfig.ControlSize = Microsoft.Office.Core.RibbonControlSize.RibbonControlSizeLarge;
            this.btnMcpConfig.Image = global::NixerWordCopilot.Properties.Resources.ModelEditor_Action_Modules_32x32;
            this.btnMcpConfig.Label = "MCP管理";
            this.btnMcpConfig.Name = "btnMcpConfig";
            this.btnMcpConfig.ShowImage = true;
            this.btnMcpConfig.Click += new Microsoft.Office.Tools.Ribbon.RibbonControlEventHandler(this.btnMcpConfig_Click);
            // 
            // btnSkillsConfig
            // 
            this.btnSkillsConfig.ControlSize = Microsoft.Office.Core.RibbonControlSize.RibbonControlSizeLarge;
            this.btnSkillsConfig.Image = global::NixerWordCopilot.Properties.Resources.Wizard_32x32;
            this.btnSkillsConfig.Label = "Skills管理";
            this.btnSkillsConfig.Name = "btnSkillsConfig";
            this.btnSkillsConfig.ShowImage = true;
            this.btnSkillsConfig.Click += new Microsoft.Office.Tools.Ribbon.RibbonControlEventHandler(this.btnSkillsConfig_Click);
            // 
            // btnMemoryConfig
            // 
            this.btnMemoryConfig.ControlSize = Microsoft.Office.Core.RibbonControlSize.RibbonControlSizeLarge;
            this.btnMemoryConfig.Image = global::NixerWordCopilot.Properties.Resources.Bookmark_32x321;
            this.btnMemoryConfig.Label = "记忆管理";
            this.btnMemoryConfig.Name = "btnMemoryConfig";
            this.btnMemoryConfig.ShowImage = true;
            this.btnMemoryConfig.Click += new Microsoft.Office.Tools.Ribbon.RibbonControlEventHandler(this.btnMemoryConfig_Click);
            // 
            // btnBuiltinToolsConfig
            // 
            this.btnBuiltinToolsConfig.ControlSize = Microsoft.Office.Core.RibbonControlSize.RibbonControlSizeLarge;
            this.btnBuiltinToolsConfig.Image = global::NixerWordCopilot.Properties.Resources.IDE_32x32;
            this.btnBuiltinToolsConfig.Label = "内置工具";
            this.btnBuiltinToolsConfig.Name = "btnBuiltinToolsConfig";
            this.btnBuiltinToolsConfig.ShowImage = true;
            this.btnBuiltinToolsConfig.Click += new Microsoft.Office.Tools.Ribbon.RibbonControlEventHandler(this.btnBuiltinToolsConfig_Click);
            // 
            // spbar2
            // 
            this.spbar2.Name = "spbar2";
            // 
            // btnConfig
            // 
            this.btnConfig.ControlSize = Microsoft.Office.Core.RibbonControlSize.RibbonControlSizeLarge;
            this.btnConfig.Image = global::NixerWordCopilot.Properties.Resources.Condif;
            this.btnConfig.Label = "系统设置";
            this.btnConfig.Name = "btnConfig";
            this.btnConfig.ShowImage = true;
            this.btnConfig.Click += new Microsoft.Office.Tools.Ribbon.RibbonControlEventHandler(this.btnConfig_Click);
            // 
            // spbar3
            // 
            this.spbar3.Name = "spbar3";
            // 
            // btnAbout
            // 
            this.btnAbout.ControlSize = Microsoft.Office.Core.RibbonControlSize.RibbonControlSizeLarge;
            this.btnAbout.Image = global::NixerWordCopilot.Properties.Resources._32x32;
            this.btnAbout.Label = "关于程序";
            this.btnAbout.Name = "btnAbout";
            this.btnAbout.ShowImage = true;
            this.btnAbout.Click += new Microsoft.Office.Tools.Ribbon.RibbonControlEventHandler(this.btnAbout_Click);
            // 
            // WordCopilotRibbon
            // 
            this.Name = "WordCopilotRibbon";
            this.RibbonType = "Microsoft.Word.Document";
            this.Tabs.Add(this.tabCopilot);
            this.Load += new Microsoft.Office.Tools.Ribbon.RibbonUIEventHandler(this.WordCopilotRibbon_Load);
            this.tabCopilot.ResumeLayout(false);
            this.tabCopilot.PerformLayout();
            this.groupCopilot.ResumeLayout(false);
            this.groupCopilot.PerformLayout();
            this.ResumeLayout(false);

        }

        internal Microsoft.Office.Tools.Ribbon.RibbonTab tabCopilot;
        internal Microsoft.Office.Tools.Ribbon.RibbonGroup groupCopilot;
        internal Microsoft.Office.Tools.Ribbon.RibbonButton btnTogglePane;
        internal Microsoft.Office.Tools.Ribbon.RibbonButton btnAbout;
        internal Microsoft.Office.Tools.Ribbon.RibbonSeparator spbar2;
        internal Microsoft.Office.Tools.Ribbon.RibbonButton btnConfig;
        internal Microsoft.Office.Tools.Ribbon.RibbonButton btnModelConfig;
        internal Microsoft.Office.Tools.Ribbon.RibbonButton btnMcpConfig;
        internal Microsoft.Office.Tools.Ribbon.RibbonSeparator spbar1;
        internal Microsoft.Office.Tools.Ribbon.RibbonButton btnSkillsConfig;
        internal Microsoft.Office.Tools.Ribbon.RibbonButton btnMemoryConfig;
        internal Microsoft.Office.Tools.Ribbon.RibbonButton btnBuiltinToolsConfig;
        internal Microsoft.Office.Tools.Ribbon.RibbonSeparator spbar3;
    }

    partial class ThisRibbonCollection
    {
        internal WordCopilotRibbon WordCopilotRibbon
        {
            get { return this.GetRibbon<WordCopilotRibbon>(); }
        }
    }
}
