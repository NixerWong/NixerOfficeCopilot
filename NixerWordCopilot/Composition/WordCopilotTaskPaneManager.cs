using Microsoft.Office.Core;
using OfficeCopilot.Agent.Abstractions;
using OfficeCopilot.Host.Abstractions;
using OfficeCopilot.Host.Word;
using OfficeCopilot.Host.Word.Tools;
using OfficeCopilot.Panel.WebView.Composition;
using OfficeCopilot.Settings.Abstractions;
using System.Windows.Forms;
using Word = Microsoft.Office.Interop.Word;

namespace NixerWordCopilot
{
    internal sealed class WordCopilotTaskPaneManager : CopilotTaskPaneManagerBase<Word.Window>
    {
        private readonly ThisAddIn _addIn;

        public WordCopilotTaskPaneManager(ThisAddIn addIn)
        {
            _addIn = addIn;
        }

        protected override string LogCategory
        {
            get { return "NixerWordCopilot.TaskPane"; }
        }

        protected override string NoActiveWindowMessage
        {
            get { return "No active Word window available."; }
        }

        protected override bool TryGetActiveWindow(out Word.Window activeWindow, out string windowKey)
        {
            activeWindow = null;
            windowKey = null;

            if (_addIn == null || _addIn.Application == null)
            {
                return false;
            }

            try
            {
                activeWindow = _addIn.Application.ActiveWindow;
            }
            catch (System.Runtime.InteropServices.COMException)
            {
                // 没有打开的文档时 Word 访问 ActiveWindow 会抛 0x800A1098。
                return false;
            }

            if (activeWindow == null)
            {
                return false;
            }

            windowKey = activeWindow.Hwnd.ToString();
            return !string.IsNullOrEmpty(windowKey);
        }

        protected override ICopilotHostAdapter CreateHostAdapter()
        {
            return new WordHostAdapter(_addIn.Application);
        }

        protected override void RegisterHostServices(AgentServiceProvider services, ICopilotHostAdapter hostAdapter)
        {
            services.AddService<IWordToolHost>((WordHostAdapter)hostAdapter);
        }

        protected override void RegisterTools(InMemoryToolRegistry registry, BuiltinToolSettings settings)
        {
            if (registry == null)
            {
                return;
            }

            var options = new WordToolCatalogOptions();
            options.Registration = ToolCatalogBuilder.CreateRegistrationOptions(
                WordBuiltInToolCatalog.CreateAllAvailableDefinitions(),
                settings.GetToolGroupRegistration,
                settings.GetToolRegistration);

            foreach (var definitionOverride in CreateToolDefinitionOverrides(settings))
            {
                options.DefinitionOverrides.Add(definitionOverride);
            }

            registry.RegisterWordBuiltInTools(options);
        }

        protected override Microsoft.Office.Tools.CustomTaskPane CreateTaskPane(
            UserControl panelControl,
            Word.Window activeWindow,
            int width)
        {
            var taskPane = _addIn.CustomTaskPanes.Add(panelControl, "Nixer Word Copilot", activeWindow);
            taskPane.DockPosition = MsoCTPDockPosition.msoCTPDockPositionRight;
            taskPane.Width = width;
            return taskPane;
        }

        protected override void RemoveTaskPane(Microsoft.Office.Tools.CustomTaskPane taskPane)
        {
            if (_addIn.CustomTaskPanes != null)
            {
                _addIn.CustomTaskPanes.Remove(taskPane);
            }
        }
    }
}
