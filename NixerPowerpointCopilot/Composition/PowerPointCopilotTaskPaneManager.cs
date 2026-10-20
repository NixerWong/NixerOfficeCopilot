using Microsoft.Office.Core;
using OfficeCopilot.Agent.Abstractions;
using OfficeCopilot.Host.Abstractions;
using OfficeCopilot.Host.PowerPoint;
using OfficeCopilot.Host.PowerPoint.Tools;
using OfficeCopilot.Panel.WebView.Composition;
using OfficeCopilot.Settings.Abstractions;
using System.Windows.Forms;
using PowerPoint = Microsoft.Office.Interop.PowerPoint;

namespace NixerPowerpointCopilot
{
    internal sealed class PowerPointCopilotTaskPaneManager : CopilotTaskPaneManagerBase<PowerPoint.DocumentWindow>
    {
        private readonly ThisAddIn _addIn;
        private PowerPointDeckRenderService _renderService;
        private PowerPointDeckBuildService _deckBuildService;

        public PowerPointCopilotTaskPaneManager(ThisAddIn addIn)
        {
            _addIn = addIn;
        }

        protected override string LogCategory
        {
            get { return "NixerPowerpointCopilot.TaskPane"; }
        }

        protected override string NoActiveWindowMessage
        {
            get { return "No active PowerPoint window available."; }
        }

        protected override bool TryGetActiveWindow(out PowerPoint.DocumentWindow activeWindow, out string windowKey)
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
                // 没有打开的文档时 PowerPoint 访问 ActiveWindow 会抛异常。
                return false;
            }

            if (activeWindow == null)
            {
                return false;
            }

            windowKey = activeWindow.HWND.ToString();
            return !string.IsNullOrEmpty(windowKey);
        }

        protected override ICopilotHostAdapter CreateHostAdapter()
        {
            return new PowerPointHostAdapter(_addIn.Application);
        }

        protected override void RegisterHostServices(AgentServiceProvider services, ICopilotHostAdapter hostAdapter)
        {
            var powerpointAdapter = (PowerPointHostAdapter)hostAdapter;
            var renderer = GetRenderService();
            services.AddService<IPowerPointToolHost>(powerpointAdapter);
            services.AddService<IPowerPointDeckBuildHost>(powerpointAdapter);
            services.AddService<IPowerPointEditHost>(powerpointAdapter);
            services.AddService<IVbaExecutionHostAdapter>(powerpointAdapter);
            services.AddService<IDeckRenderService>(renderer);
            _deckBuildService = new PowerPointDeckBuildService(
                powerpointAdapter,
                renderer,
                OfficeCopilot.Slides.Assets.SlideAssetPaths.CreateDefault(),
                OfficeCopilot.Slides.Deck.DeckWorkspaceStore.GetDefaultRoot());
            _deckBuildService.FontInstaller = OfficeCopilot.Host.PowerPoint.PowerPointFontInstaller.CreateDefault(
                OfficeCopilot.Slides.Assets.SlideAssetPaths.CreateDefault());
            services.AddService<IPowerPointDeckBuildService>(_deckBuildService);
        }

        protected override void RegisterSettingsServices(AgentServiceProvider services, OfficeCopilotSettings settings)
        {
            var slides = settings == null ? null : settings.PowerPointSlides;
            services.AddService(new PowerPointSlidesOptions
            {
                DefaultStyle = slides == null ? string.Empty : slides.DefaultStyle,
                DefaultDensity = slides == null ? "medium" : slides.DefaultDensity,
                DefaultBuildMode = slides == null ? "fidelity" : slides.DefaultBuildMode,
                RepairIterationCap = slides == null ? 3 : slides.RepairIterationCap,
                WorkspaceRetentionDays = slides == null ? 30 : slides.WorkspaceRetentionDays,
                EmbedFontsByDefault = slides == null || slides.EmbedFontsByDefault
            });
            if (_deckBuildService != null && slides != null)
            {
                _deckBuildService.RepairIterationCap = slides.RepairIterationCap;
            }
        }

        /// <summary>渲染服务全局共用一个隐藏 WebView2；在 UI 线程上首次创建。</summary>
        private PowerPointDeckRenderService GetRenderService()
        {
            if (_renderService == null)
            {
                var ui = System.Threading.SynchronizationContext.Current as WindowsFormsSynchronizationContext
                    ?? new WindowsFormsSynchronizationContext();
                _renderService = new PowerPointDeckRenderService(
                    ui,
                    OfficeCopilot.Slides.Assets.SlideAssetPaths.CreateDefault(),
                    OfficeCopilot.Slides.Deck.DeckWorkspaceStore.GetDefaultRoot());
            }

            return _renderService;
        }

        protected override void RegisterTools(InMemoryToolRegistry registry, OfficeCopilotSettings settings)
        {
            if (registry == null)
            {
                return;
            }

            if (!HasToolCatalogConfiguration(settings))
            {
                registry.RegisterPowerPointBuiltInTools();
                return;
            }

            var options = new PowerPointToolCatalogOptions();
            if (settings.HasToolRegistrationConfiguration())
            {
                // 只排除用户明确关闭的项；旧 powerpoint_* 设置迁移到 ppt_*，新工具默认可用。
                options.Registration = PowerPointBuiltInTools.CreateRegistrationOptions(
                    settings.GetToolGroupRegistration,
                    settings.GetToolRegistration);
            }

            foreach (var definitionOverride in CreateToolDefinitionOverrides(settings))
            {
                definitionOverride.ToolName = PowerPointToolNames.MapLegacy(definitionOverride.ToolName);
                options.DefinitionOverrides.Add(definitionOverride);
            }

            registry.RegisterPowerPointBuiltInTools(options);
        }

        protected override Microsoft.Office.Tools.CustomTaskPane CreateTaskPane(
            UserControl panelControl,
            PowerPoint.DocumentWindow activeWindow,
            int width)
        {
            var taskPane = _addIn.CustomTaskPanes.Add(panelControl, "Nixer PowerPoint Copilot", activeWindow);
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
