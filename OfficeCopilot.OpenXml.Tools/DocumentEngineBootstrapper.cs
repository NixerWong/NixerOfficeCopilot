using OfficeCopilot.OpenXml.Excel;
using OfficeCopilot.OpenXml.PowerPoint;
using OfficeCopilot.OpenXml.Word;

namespace OfficeCopilot.OpenXml.Tools
{
    /// <summary>
    /// Registers the three format engines into <see cref="OfficeCopilot.OpenXml.Core.DocumentEngineRegistry"/>.
    /// Idempotent and thread-safe; call <see cref="EnsureRegistered"/> before any
    /// registry-based Open/Create/Merge (the file tools do this automatically).
    /// </summary>
    public static class DocumentEngineBootstrapper
    {
        private static readonly object Gate = new object();
        private static bool _registered;

        public static void EnsureRegistered()
        {
            if (_registered)
            {
                return;
            }

            lock (Gate)
            {
                if (_registered)
                {
                    return;
                }

                WordDocumentEngine.Register();
                ExcelDocumentEngine.Register();
                PowerPointDocumentEngine.Register();
                _registered = true;
            }
        }
    }
}
