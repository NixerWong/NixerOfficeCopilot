using Microsoft.Office.Core;
using OfficeCopilot.Agent.Abstractions;
using OfficeCopilot.Host.Abstractions;
using OfficeCopilot.Host.Excel;
using OfficeCopilot.Host.Excel.Com;
using OfficeCopilot.Host.Excel.Tools;
using OfficeCopilot.Panel.WebView.Composition;
using OfficeCopilot.Settings.Abstractions;
using System.Threading;
using System.Windows.Forms;
using Excel = Microsoft.Office.Interop.Excel;

namespace NixerExcelCopilot
{
    internal sealed class ExcelCopilotTaskPaneManager : CopilotTaskPaneManagerBase<Excel.Window>
    {
        private readonly ThisAddIn _addIn;

        public ExcelCopilotTaskPaneManager(ThisAddIn addIn)
        {
            _addIn = addIn;
        }

        protected override string LogCategory
        {
            get { return "NixerExcelCopilot.TaskPane"; }
        }

        protected override string NoActiveWindowMessage
        {
            get { return "No Excel window is currently available."; }
        }

        protected override bool TryGetActiveWindow(out Excel.Window activeWindow, out string windowKey)
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
                // 没有打开的工作簿时访问 ActiveWindow 可能抛 COM 异常，与 Word / PowerPoint 保持一致。
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
            return new ExcelHostAdapter(_addIn.Application);
        }

        protected override void RegisterHostServices(AgentServiceProvider services, ICopilotHostAdapter hostAdapter)
        {
            var excelHostAdapter = (ExcelHostAdapter)hostAdapter;
            services.AddService<IVbaExecutionHostAdapter>(excelHostAdapter);
            services.AddService<IExcelToolHost>(excelHostAdapter);
            if (SynchronizationContext.Current == null)
            {
                SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
            }

            services.AddService<IHostCallPolicy>(new ExcelComDispatcher());
        }

        protected override void RegisterTools(InMemoryToolRegistry registry, BuiltinToolSettings settings)
        {
            if (registry == null)
            {
                return;
            }

            var options = new ExcelToolCatalogOptions();
            options.Registration = ToolCatalogBuilder.CreateRegistrationOptions(
                ExcelBuiltInToolCatalog.CreateAllAvailableDefinitions(),
                settings.GetToolGroupRegistration,
                settings.GetToolRegistration);

            foreach (var definitionOverride in CreateToolDefinitionOverrides(settings))
            {
                options.DefinitionOverrides.Add(definitionOverride);
            }

            registry.RegisterExcelBuiltInTools(options);
        }

        protected override Microsoft.Office.Tools.CustomTaskPane CreateTaskPane(
            UserControl panelControl,
            Excel.Window activeWindow,
            int width)
        {
            var taskPane = _addIn.CustomTaskPanes.Add(panelControl, "Nixer Excel Copilot", activeWindow);
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
