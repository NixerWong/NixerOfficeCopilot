using System;

namespace OfficeCopilot.OpenXml.PowerPoint
{
    /// <summary>
    /// Lightweight readback of one shape on a slide.
    /// </summary>
    public sealed class PowerPointShapeInfo
    {
        public uint Id { get; set; }

        public string Name { get; set; }

        /// <summary>One of "title", "textbox" or "shape".</summary>
        public string Type { get; set; }

        public string Text { get; set; }
    }
}
