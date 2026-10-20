using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace OfficeCopilot.Mcp
{
    /// <summary>
    /// MCP 注册表默认实现：从 .mcp.json 加载配置、管理连接、路由工具调用。
    /// 所有 MCP server 异常均被捕获并归一——不中断主流程。
    /// </summary>
    public sealed class McpRegistry : IMcpRegistry, IAsyncDisposable
    {
        private readonly Dictionary<string, IMcpClient> _clients
            = new Dictionary<string, IMcpClient>(StringComparer.OrdinalIgnoreCase);

        // 完整工具名（mcp__server__tool，已归一化）→ 客户端与原始工具名。路由只查此表，
        // 避免用原始 server 名与归一化后的名字做字符串匹配。
        private readonly Dictionary<string, McpRoute> _routes
            = new Dictionary<string, McpRoute>(StringComparer.Ordinal);

        private readonly List<McpServerState> _serverStates = new List<McpServerState>();

        // 解析阶段因配置无效而被跳过的 server（Failed 状态，仅用于展示原因；不会连接或路由）。
        private List<McpServerState> _configSkippedStates = new List<McpServerState>();

        // server 名 → 最近一次尝试时的配置指纹（成功或失败都记录），用于刷新时判断是否需要重连。
        private readonly Dictionary<string, string> _fingerprints
            = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        private readonly SemaphoreSlim _refreshGate = new SemaphoreSlim(1, 1);

        private readonly IMcpClientFactory _clientFactory;

        public McpRegistry() : this(null)
        {
        }

        public McpRegistry(IMcpClientFactory clientFactory)
        {
            _clientFactory = clientFactory ?? new NuGetMcpClientFactory();
        }

        public IReadOnlyList<IMcpClient> ConnectedClients
        {
            get
            {
                lock (_clients)
                {
                    return _clients.Values
                        .Where(c => c.IsConnected)
                        .ToList();
                }
            }
        }

        public async Task LoadAsync(string mcpConfigPath, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(mcpConfigPath) || !File.Exists(mcpConfigPath))
            {
                return;
            }

            // 与运行时刷新使用同一解析器（settings.json 格式）；读取或解析失败直接抛出。
            var config = McpJsonConfig.FromSettingsJson(mcpConfigPath);
            await LoadAsync(config, cancellationToken).ConfigureAwait(false);
        }

        public async Task LoadAsync(McpJsonConfig config, CancellationToken cancellationToken)
        {
            SetConfigSkippedStates(config?.SkippedServers);

            if (config?.McpServers == null || config.McpServers.Count == 0)
            {
                return;
            }

            var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var kvp in config.McpServers)
            {
                var serverName = kvp.Key;
                var serverConfig = kvp.Value;

                if (string.IsNullOrWhiteSpace(serverName) || serverConfig == null)
                {
                    continue;
                }

                // 名称按大小写不敏感去重：同名 server 只连接第一个，避免互相覆盖连接与路由。
                if (!seenNames.Add(serverName))
                {
                    continue;
                }

                _serverStates.Add(await LoadServerAsync(serverName, serverConfig, cancellationToken).ConfigureAwait(false));
            }
        }

        private async Task<McpServerState> LoadServerAsync(
            string serverName, McpServerConfigEntry serverConfig, CancellationToken cancellationToken)
        {
            var state = new McpServerState
            {
                ServerName = serverName,
                State = McpServerConnectionState.Connecting
            };

            var disabled = new HashSet<string>(serverConfig.DisabledTools ?? new List<string>(), StringComparer.OrdinalIgnoreCase);
            var conflicts = new List<string>();

            IMcpClient client = null;
            try
            {
                client = _clientFactory.CreateClient(serverName, serverConfig);
                await client.ConnectAsync(cancellationToken).ConfigureAwait(false);

                var tools = await client.ListToolsAsync(cancellationToken).ConfigureAwait(false);

                lock (_clients)
                {
                    if (_clients.TryGetValue(serverName, out var previous))
                    {
                        RemoveRoutesFor(previous);
                    }

                    _clients[serverName] = client;

                    foreach (var tool in tools ?? Array.Empty<McpToolDescriptor>())
                    {
                        if (disabled.Contains(tool.Name))
                        {
                            continue;
                        }

                        var fullName = McpToolNaming.BuildFullToolName(serverName, tool.Name);
                        if (_routes.ContainsKey(fullName))
                        {
                            // 归一化后名称冲突（如 my.server 与 my_server）：保留先注册的路由，并在状态中报告。
                            conflicts.Add(tool.Name);
                            continue;
                        }

                        _routes[fullName] = new McpRoute(client, tool.Name);
                    }
                }

                state.State = McpServerConnectionState.Connected;
                state.ToolCount = tools?.Count ?? 0;
                if (conflicts.Count > 0)
                {
                    state.ErrorMessage = "以下工具的完整名称与其他 server 冲突，已忽略：" + string.Join(", ", conflicts);
                }
            }
            catch (OperationCanceledException)
            {
                await DisposeClientQuietlyAsync(client).ConfigureAwait(false);
                throw;
            }
            catch (Exception ex)
            {
                state.State = McpServerConnectionState.Failed;
                state.ErrorMessage = ex.Message;
                await DisposeClientQuietlyAsync(client).ConfigureAwait(false);
            }

            // 只有完成一次尝试（成功或失败）才记录指纹；被取消时不记录，下次刷新会重试。
            _fingerprints[serverName] = Fingerprint(serverConfig);
            return state;
        }

        private static async Task DisposeClientQuietlyAsync(IMcpClient client)
        {
            if (client == null)
            {
                return;
            }

            try
            {
                await client.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception)
            {
                // best-effort: 清理失败不影响调用方
            }
        }

        /// <summary>
        /// 生成 server 配置的指纹，用于判断刷新时配置是否发生变化。
        /// </summary>
        private static string Fingerprint(McpServerConfigEntry c)
        {
            var sb = new StringBuilder();
            sb.Append(c.Transport).Append('\n');
            sb.Append(c.Command).Append('\n');
            sb.Append(c.WorkingDirectory).Append('\n');
            sb.Append(c.Url).Append('\n');
            sb.Append(c.TransportMode).Append('\n');

            foreach (var arg in c.Args ?? new List<string>())
            {
                sb.Append(arg).Append('\u0001');
            }
            sb.Append('\n');

            foreach (var tool in (c.DisabledTools ?? new List<string>()).OrderBy(t => t, StringComparer.Ordinal))
            {
                sb.Append(tool).Append('\u0001');
            }
            sb.Append('\n');

            foreach (var kv in (c.Env ?? new Dictionary<string, string>()).OrderBy(k => k.Key, StringComparer.Ordinal))
            {
                sb.Append(kv.Key).Append('=').Append(kv.Value).Append('\u0001');
            }
            sb.Append('\n');

            foreach (var kv in (c.Headers ?? new Dictionary<string, string>()).OrderBy(k => k.Key, StringComparer.Ordinal))
            {
                sb.Append(kv.Key).Append('=').Append(kv.Value).Append('\u0001');
            }

            return sb.ToString();
        }

        public IReadOnlyList<McpServerState> GetServerStates()
        {
            return _serverStates.Concat(_configSkippedStates)
                .Select(MarkExitedServer)
                .ToList()
                .AsReadOnly();
        }

        private McpServerState MarkExitedServer(McpServerState state)
        {
            if (state.State != McpServerConnectionState.Connected || !IsClientDead(state.ServerName))
            {
                return state;
            }

            return new McpServerState
            {
                ServerName = state.ServerName,
                State = McpServerConnectionState.Disconnected,
                ToolCount = state.ToolCount,
                ErrorMessage = "MCP server 进程已退出，将在下一轮刷新时重连。"
            };
        }

        private bool IsClientDead(string serverName)
        {
            lock (_clients)
            {
                return _clients.TryGetValue(serverName, out var client) && !client.IsConnected;
            }
        }

        private void SetConfigSkippedStates(IEnumerable<McpSkippedServer> skipped)
        {
            _configSkippedStates = (skipped ?? Enumerable.Empty<McpSkippedServer>())
                .Select(s => new McpServerState
                {
                    ServerName = s.Name,
                    State = McpServerConnectionState.Failed,
                    ErrorMessage = s.Reason
                })
                .ToList();
        }

        public bool TryRoute(string fullToolName, out IMcpClient client, out string realToolName)
        {
            client = null;
            realToolName = string.Empty;

            if (string.IsNullOrWhiteSpace(fullToolName))
            {
                return false;
            }

            lock (_clients)
            {
                if (!_routes.TryGetValue(fullToolName, out var route) || !route.Client.IsConnected)
                {
                    return false;
                }

                client = route.Client;
                realToolName = route.RealToolName;
                return true;
            }
        }

        public async Task<IReadOnlyList<McpToolDescriptor>> ListAllToolsAsync(CancellationToken cancellationToken)
        {
            var allTools = new List<McpToolDescriptor>();

            List<(IMcpClient Client, Dictionary<string, McpRoute> Owned)> clients;
            lock (_clients)
            {
                // 目录只列出路由表中确实指向该 client 的工具：禁用的、被同名冲突丢弃的都不会出现，
                // 保证目录中每一项都能被调用到正确的 server。
                clients = _clients.Values
                    .Where(c => c.IsConnected)
                    .Select(c => (c, _routes
                        .Where(kv => ReferenceEquals(kv.Value.Client, c))
                        .ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal)))
                    .ToList();
            }

            foreach (var (client, owned) in clients)
            {
                try
                {
                    var tools = await client.ListToolsAsync(cancellationToken).ConfigureAwait(false);
                    if (tools != null)
                    {
                        foreach (var tool in tools)
                        {
                            var fullName = McpToolNaming.BuildFullToolName(client.ServerName, tool.Name);
                            if (!owned.TryGetValue(fullName, out var route) || route.RealToolName != tool.Name)
                            {
                                continue;
                            }

                            // 重命名为 mcp__server__tool 格式
                            allTools.Add(new McpToolDescriptor
                            {
                                Name = fullName,
                                Description = $"[MCP:{client.ServerName}] {tool.Description ?? string.Empty}",
                                InputSchemaJson = tool.InputSchemaJson ?? "{\"type\":\"object\",\"properties\":{}}",
                                IsReadOnly = tool.IsReadOnly
                            });
                        }
                    }
                }
                catch (Exception)
                {
                }
            }

            return allTools;
        }

        private sealed class McpRoute
        {
            public McpRoute(IMcpClient client, string realToolName)
            {
                Client = client;
                RealToolName = realToolName;
            }

            public IMcpClient Client { get; }

            public string RealToolName { get; }
        }

        private void RemoveRoutesFor(IMcpClient client)
        {
            var stale = _routes.Where(kv => ReferenceEquals(kv.Value.Client, client)).Select(kv => kv.Key).ToList();
            foreach (var key in stale)
            {
                _routes.Remove(key);
            }
        }

        public async ValueTask DisposeAsync()
        {
            IMcpClient[] clients;
            lock (_clients)
            {
                clients = _clients.Values.ToArray();
                _clients.Clear();
                _routes.Clear();
                _fingerprints.Clear();
            }

            foreach (var client in clients)
            {
                try
                {
                    await client.DisposeAsync().ConfigureAwait(false);
                }
                catch
                {
                    // Best-effort cleanup
                }
            }
        }

        /// <summary>
        /// 获取 MCP 配置的默认路径（settings.json）。
        /// </summary>
        public static string GetDefaultConfigPath()
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            return Path.Combine(appData, "NixerOfficeCopilot", "settings.json");
        }

        /// <summary>
        /// 从 settings.json 增量刷新 MCP 连接：配置解析失败时保持现有连接不变；
        /// 只断开已删除或配置已变化的 server，并只连接新增或变化的 server。
        /// 配置未变化的 server（包括连接失败的）不会在每轮刷新时重连。
        /// </summary>
        public async Task RefreshFromSettingsAsync(string settingsJsonPath, CancellationToken cancellationToken)
        {
            // 文件不存在时保持现有连接不变（与启动时 LoadAsync 的行为一致）。
            if (!File.Exists(settingsJsonPath))
            {
                return;
            }

            // 先读取并解析：读失败或解析失败都直接抛出，此时尚未触碰任何现有连接。
            var config = McpJsonConfig.FromSettingsJson(settingsJsonPath);
            var desired = config.McpServers ?? new Dictionary<string, McpServerConfigEntry>();

            await _refreshGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                SetConfigSkippedStates(config.SkippedServers);

                var existingNames = _fingerprints.Keys.ToList();
                foreach (var name in existingNames)
                {
                    // 已连接但会话已结束（如子进程退出）的 server 也需要重连。
                    if (desired.TryGetValue(name, out var entry) && entry != null
                        && string.Equals(_fingerprints[name], Fingerprint(entry), StringComparison.Ordinal)
                        && !IsClientDead(name))
                    {
                        continue;
                    }

                    IMcpClient old = null;
                    lock (_clients)
                    {
                        if (_clients.TryGetValue(name, out old))
                        {
                            _clients.Remove(name);
                            RemoveRoutesFor(old);
                        }
                    }

                    _serverStates.RemoveAll(s => string.Equals(s.ServerName, name, StringComparison.OrdinalIgnoreCase));
                    _fingerprints.Remove(name);
                    await DisposeClientQuietlyAsync(old).ConfigureAwait(false);
                }

                foreach (var kvp in desired)
                {
                    var name = kvp.Key;
                    var entry = kvp.Value;

                    if (string.IsNullOrWhiteSpace(name) || entry == null || _fingerprints.ContainsKey(name))
                    {
                        continue;
                    }

                    _serverStates.Add(await LoadServerAsync(name, entry, cancellationToken).ConfigureAwait(false));
                }
            }
            finally
            {
                _refreshGate.Release();
            }
        }
    }
}
