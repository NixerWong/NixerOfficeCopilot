using System;

namespace OfficeCopilot.Mcp
{
    /// <summary>
    /// 默认的 MCP 客户端工厂，创建基于 ModelContextProtocol NuGet 的 <see cref="NuGetMcpClient"/>。
    /// </summary>
    public sealed class NuGetMcpClientFactory : IMcpClientFactory
    {
        public bool SupportsTransport(string transport)
        {
            return string.Equals(transport, "stdio", StringComparison.OrdinalIgnoreCase)
                || string.Equals(transport, "http", StringComparison.OrdinalIgnoreCase)
                || string.Equals(transport, "sse", StringComparison.OrdinalIgnoreCase);
        }

        public IMcpClient CreateClient(string serverName, McpServerConfigEntry config)
        {
            return new NuGetMcpClient(serverName, config);
        }
    }
}
