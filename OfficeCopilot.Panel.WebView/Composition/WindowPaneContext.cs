using OfficeCopilot.HostBridge.Server;
using System;
using System.Threading;

namespace OfficeCopilot.Panel.WebView.Composition
{
    /// <summary>
    /// 单个宿主窗口对应的一整套面板运行时资源。三个 VSTO 加载项此前各自维护一份同名私有类。
    /// </summary>
    public sealed class WindowPaneContext
    {
        public Microsoft.Office.Tools.CustomTaskPane TaskPane { get; set; }

        public CopilotPanelControl PanelControl { get; set; }

        public Services.BrokerRuntimeHost BrokerRuntimeHost { get; set; }

        public IHostBridgeServer HostBridgeServer { get; set; }

        public CancellationTokenSource HostBridgeServerCts { get; set; }

        public IDisposable LoggerFactoryDisposable { get; set; }
    }
}
