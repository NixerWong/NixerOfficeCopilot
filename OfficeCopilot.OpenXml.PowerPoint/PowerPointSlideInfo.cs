using System;

namespace OfficeCopilot.OpenXml.PowerPoint
{
    /// <summary>
    /// Lightweight readback of one slide: its 1-based position in the deck and
    /// the concatenated text of all its text shapes.
    /// </summary>
    public sealed class PowerPointSlideInfo
    {
        public int Index { get; set; }

        public string Text { get; set; }
    }
}
