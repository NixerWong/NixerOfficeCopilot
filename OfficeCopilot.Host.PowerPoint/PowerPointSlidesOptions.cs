namespace OfficeCopilot.Host.PowerPoint
{
    public sealed class PowerPointSlidesOptions
    {
        public string DefaultStyle { get; set; }

        public string DefaultDensity { get; set; }

        public string DefaultBuildMode { get; set; }

        public int RepairIterationCap { get; set; }

        public int WorkspaceRetentionDays { get; set; }

        public bool EmbedFontsByDefault { get; set; }
    }
}