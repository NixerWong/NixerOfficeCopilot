using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace OfficeCopilot.Mcp
{
    /// <summary>
    /// MCP 工具描述符（从 MCP server 发现）。与 NuGet 的 <c>McpClientTool</c> 解耦。
    /// </summary>
    public sealed class McpToolDescriptor
    {
        public string Name { get; set; } = string.Empty;

        public string Description { get; set; } = string.Empty;

        public string InputSchemaJson { get; set; } = "{}";

        /// <summary>服务器声明的 readOnlyHint。未声明或为 false 时按写入工具处理。</summary>
        public bool IsReadOnly { get; set; }
    }

    /// <summary>
    /// MCP 工具调用结果。
    /// </summary>
    public sealed class McpToolCallResult
    {
        public bool Success { get; set; }

        public string Content { get; set; } = string.Empty;

        public string ErrorMessage { get; set; }
    }

    /// <summary>
    /// 单个 MCP server 的客户端抽象。包装 NuGet <c>ModelContextProtocol.IMcpClient</c>。
    /// </summary>
    public interface IMcpClient : IAsyncDisposable
    {
        /// <summary>MCP server 名称。</summary>
        string ServerName { get; }

        /// <summary>是否已连接并就绪；底层会话已结束（如子进程退出）时为 false。</summary>
        bool IsConnected { get; }

        /// <summary>建立连接并完成初始化握手。</summary>
        Task ConnectAsync(CancellationToken cancellationToken);

        /// <summary>列出该 server 暴露的所有工具。</summary>
        Task<IReadOnlyList<McpToolDescriptor>> ListToolsAsync(CancellationToken cancellationToken);

        /// <summary>调用指定工具。</summary>
        Task<McpToolCallResult> CallToolAsync(
            string toolName,
            JsonElement arguments,
            CancellationToken cancellationToken);
    }

    /// <summary>
    /// MCP 客户端工厂：根据配置创建 <see cref="IMcpClient"/> 实例。
    /// </summary>
    public interface IMcpClientFactory
    {
        /// <summary>支持的 transport 类型。</summary>
        bool SupportsTransport(string transport);

        /// <summary>创建客户端（不连接）。</summary>
        IMcpClient CreateClient(string serverName, McpServerConfigEntry config);
    }

    /// <summary>
    /// MCP server 注册表：管理多个 MCP server 的连接、工具发现与路由。
    /// 对应 cc-haha <c>McpConnectionManager</c> + <c>client.ts</c> 的连接管理职责。
    /// </summary>
    public interface IMcpRegistry
    {
        /// <summary>
        /// 从 settings.json 加载 MCP server 配置并连接（文件不存在时不做任何事；解析失败抛出）。
        /// </summary>
        Task LoadAsync(string settingsJsonPath, CancellationToken cancellationToken);

        /// <summary>
        /// 使用给定配置直接加载（不走文件）。
        /// </summary>
        Task LoadAsync(McpJsonConfig config, CancellationToken cancellationToken);

        /// <summary>
        /// 获取所有已注册的 server 运行时状态。
        /// </summary>
        IReadOnlyList<McpServerState> GetServerStates();

        /// <summary>
        /// 获取所有已连接的 MCP 客户端。
        /// </summary>
        IReadOnlyList<IMcpClient> ConnectedClients { get; }

        /// <summary>
        /// 根据完整 MCP 工具名（<c>mcp__server__tool</c>）路由到对应客户端。
        /// </summary>
        bool TryRoute(string fullToolName, out IMcpClient client, out string realToolName);

        /// <summary>
        /// 获取所有 MCP 工具的列表（供合并到全局 ToolCatalog）。
        /// </summary>
        Task<IReadOnlyList<McpToolDescriptor>> ListAllToolsAsync(CancellationToken cancellationToken);

        /// <summary>
        /// 从 settings.json 重新加载 MCP 配置：断开旧连接，清除状态，连接新配置的 server。
        /// 用于用户在设置面板中增删 MCP server 后即时生效，无需重启 broker。
        /// </summary>
        Task RefreshFromSettingsAsync(string settingsJsonPath, CancellationToken cancellationToken);
    }
}
