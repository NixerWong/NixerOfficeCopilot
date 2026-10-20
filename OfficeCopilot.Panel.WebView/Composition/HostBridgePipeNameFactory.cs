using System.Diagnostics;
using System.Globalization;

namespace OfficeCopilot.Panel.WebView.Composition
{
    /// <summary>
    /// HostBridge 命名管道端点命名规则。按"宿主进程 + 窗口"两级隔离，保证多开窗口互不串线。
    /// </summary>
    public static class HostBridgePipeNameFactory
    {
        public const string Prefix = "officecopilot-host-bridge-";

        public static string Create(string windowKey)
        {
            return Prefix
                + Process.GetCurrentProcess().Id.ToString(CultureInfo.InvariantCulture)
                + "-"
                + (windowKey ?? string.Empty);
        }
    }
}
