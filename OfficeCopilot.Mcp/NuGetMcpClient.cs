using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using McpClient = ModelContextProtocol.Client.McpClient;
using McpClientOptions = ModelContextProtocol.Client.McpClientOptions;
using McpClientTool = ModelContextProtocol.Client.McpClientTool;

namespace OfficeCopilot.Mcp
{
    /// <summary>
    /// 基于 ModelContextProtocol NuGet (v1.4.1+) 的 MCP 客户端包装。
    /// 使用 <c>McpClient.CreateAsync</c> 工厂方法创建客户端，
    /// 封装 stdio / http 连接生命周期与工具调用。
    /// </summary>
    public sealed class NuGetMcpClient : IMcpClient
    {
        private const int DefaultTimeoutSeconds = 90;

        private readonly string _serverName;
        private readonly McpServerConfigEntry _config;
        private McpClient _innerClient;
        private IReadOnlyList<McpToolDescriptor> _cachedTools;

        public NuGetMcpClient(string serverName, McpServerConfigEntry config)
        {
            _serverName = serverName ?? throw new ArgumentNullException(nameof(serverName));
            _config = config ?? throw new ArgumentNullException(nameof(config));
        }

        public string ServerName => _serverName;
        public bool IsConnected => _innerClient != null && !_innerClient.Completion.IsCompleted;

        // 连接、tools/list、tools/call 统一使用固定超时；不提供用户可配置项。
        private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(DefaultTimeoutSeconds);

        private async Task<T> RunWithTimeoutAsync<T>(
            Func<CancellationToken, Task<T>> operation, CancellationToken ct, string operationName,
            TimeSpan? timeout = null)
        {
            var limit = timeout ?? Timeout;

            // SDK 的 CreateAsync 不一定及时响应取消令牌（子进程挂起时会一直等到进程退出），
            // 所以这里用 WhenAny 竞速，超时后主动取消内部操作并抛 TimeoutException。
            using (var operationCts = CancellationTokenSource.CreateLinkedTokenSource(ct))
            {
                var work = operation(operationCts.Token);
                var delay = Task.Delay(limit, ct);

                var finished = await Task.WhenAny(work, delay).ConfigureAwait(false);
                if (finished == work)
                    return await work.ConfigureAwait(false);

                ct.ThrowIfCancellationRequested();

                operationCts.Cancel();
                _ = work.ContinueWith(
                    t => { _ = t.Exception; },
                    TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously);

                throw new TimeoutException(
                    $"MCP server '{_serverName}' {operationName} timed out after {limit.TotalSeconds}s.");
            }
        }

        public async Task ConnectAsync(CancellationToken cancellationToken)
        {
            if (_innerClient != null)
                return;

            ModelContextProtocol.Client.IClientTransport transport;

            if (_config.IsStdio)
            {
                var topts = new ModelContextProtocol.Client.StdioClientTransportOptions
                {
                    Command = OfficeCopilot.Agent.Abstractions.McpCommandResolver.Resolve(
                        _config.Command ?? throw new InvalidOperationException(
                            $"MCP server '{_serverName}': 'command' required for stdio."),
                        _config.WorkingDirectory),
                    Name = _serverName
                };

                if (_config.Args != null && _config.Args.Count > 0)
                    topts.Arguments = _config.Args;

                if (!string.IsNullOrWhiteSpace(_config.WorkingDirectory))
                    topts.WorkingDirectory = _config.WorkingDirectory;

                // 构建环境变量：继承基础系统变量 + 拼入自定义变量
                // SDK 默认 InheritEnvironmentVariables=true，但 Windows 上 cmd.exe /c
                // 包裹可能导致自定义变量丢失。使用显式字典确保变量一定生效。
                var env = ModelContextProtocol.Client.StdioClientTransportOptions
                    .GetDefaultEnvironmentVariables();

                if (_config.Env != null)
                {
                    foreach (var kv in _config.Env)
                    {
                        if (!string.IsNullOrWhiteSpace(kv.Key))
                            env[kv.Key] = kv.Value;
                    }
                }

                topts.EnvironmentVariables = env;

                transport = new ModelContextProtocol.Client.StdioClientTransport(topts);
            }
            else if (_config.IsHttp)
            {
                // .NET Framework 进程的默认协议可能不含 TLS 1.2，握手会失败，这里补上。
                System.Net.ServicePointManager.SecurityProtocol |= System.Net.SecurityProtocolType.Tls12;

                var endpoint = new Uri(_config.Url ?? throw new InvalidOperationException(
                    $"MCP server '{_serverName}': 'url' required for http/sse."));

                var topts = new ModelContextProtocol.Client.HttpClientTransportOptions
                {
                    Endpoint = endpoint,
                    Name = _serverName,
                    TransportMode = ToSdkTransportMode(_config.TransportMode)
                };

                // 传递用户在 settings.json 中配置的 HTTP 请求头（如认证 Token）
                if (_config.Headers != null && _config.Headers.Count > 0)
                {
                    topts.AdditionalHeaders = new Dictionary<string, string>(_config.Headers);
                }

                transport = new ModelContextProtocol.Client.HttpClientTransport(topts);
            }
            else
            {
                throw new InvalidOperationException(
                    $"Unsupported transport: '{_config.Transport}'. Use stdio, http, or sse.");
            }

            var clientOptions = new McpClientOptions();

            try
            {
                _innerClient = await RunWithTimeoutAsync(
                    t => McpClient.CreateAsync(
                        transport,
                        clientOptions,
                        loggerFactory: null,
                        cancellationToken: t),
                    cancellationToken,
                    "connect").ConfigureAwait(false);
            }
            catch (Exception)
            {
                // 握手失败（超时、协议错误、子进程提前退出等）都要释放 transport，否则子进程会残留。
                await DisposeTransportAsync(transport).ConfigureAwait(false);
                throw;
            }

            _cachedTools = null;
        }

        private static async Task DisposeTransportAsync(object transport)
        {
            try
            {
                if (transport is IAsyncDisposable asyncDisposable)
                    await asyncDisposable.DisposeAsync().ConfigureAwait(false);
                else if (transport is IDisposable disposable)
                    disposable.Dispose();
            }
            catch
            {
                // best-effort: 超时后释放失败不应覆盖原始超时异常
            }
        }

        public async Task<IReadOnlyList<McpToolDescriptor>> ListToolsAsync(CancellationToken ct)
        {
            if (_innerClient == null)
                return Array.Empty<McpToolDescriptor>();

            if (_cachedTools == null)
            {
                var tools = await RunWithTimeoutAsync(
                    async t => await _innerClient.ListToolsAsync(options: null, cancellationToken: t).ConfigureAwait(false),
                    ct,
                    "tools/list").ConfigureAwait(false);

                _cachedTools = tools
                    .Select(t => new McpToolDescriptor
                    {
                        Name = t.Name ?? string.Empty,
                        Description = t.Description ?? string.Empty,
                        InputSchemaJson = GetSchemaJson(t),
                        IsReadOnly = t.ProtocolTool?.Annotations?.ReadOnlyHint == true
                    })
                    .ToList();
            }

            return _cachedTools;
        }

        public async Task<McpToolCallResult> CallToolAsync(
            string toolName, JsonElement arguments, CancellationToken ct)
        {
            if (_innerClient == null)
            {
                return new McpToolCallResult
                {
                    Success = false,
                    ErrorMessage = $"MCP client '{_serverName}' not connected."
                };
            }

            try
            {
                // Convert JsonElement to Dictionary for the NuGet API
                var argsDict = JsonSerializer.Deserialize<Dictionary<string, object>>(
                    arguments.GetRawText()) ?? new Dictionary<string, object>();

                var result = await RunWithTimeoutAsync(
                    async t => await _innerClient.CallToolAsync(
                        toolName,
                        argsDict,
                        progress: null,
                        options: null,
                        cancellationToken: t).ConfigureAwait(false),
                    ct,
                    $"call '{toolName}'").ConfigureAwait(false);

                var text = ExtractContentText(result?.Content);
                var isError = result?.IsError ?? false;

                return new McpToolCallResult
                {
                    Success = !isError,
                    Content = text,
                    ErrorMessage = isError ? DescribeToolError(toolName, text) : null
                };
            }
            catch (Exception ex)
            {
                return new McpToolCallResult
                {
                    Success = false,
                    ErrorMessage = $"MCP call '{toolName}' on '{_serverName}' failed: {ex.Message}"
                };
            }
        }

        public async ValueTask DisposeAsync()
        {
            if (_innerClient != null)
            {
                try { await _innerClient.DisposeAsync().ConfigureAwait(false); }
                catch { /* best-effort */ }
                _innerClient = null;
                _cachedTools = null;
            }
        }

        /// <summary>
        /// 为 IsError 的工具结果生成可读的错误信息：包含工具名、服务名，以及服务端文本的首行（截断）。
        /// 完整文本仍保留在 Content 中。
        /// </summary>
        private string DescribeToolError(string toolName, string text)
        {
            const int MaxSummaryLength = 300;

            var prefix = $"MCP tool '{toolName}' on '{_serverName}' returned an error";
            var firstLine = (text ?? string.Empty).Split('\n')[0].Trim();
            if (firstLine.Length == 0)
                return prefix + ".";

            if (firstLine.Length > MaxSummaryLength)
                firstLine = firstLine.Substring(0, MaxSummaryLength) + "...";

            return prefix + ": " + firstLine;
        }

        /// <summary>
        /// 把 MCP 工具结果的内容块转换为文本。非文本内容不能直接给模型，这里转为描述占位，避免被静默丢弃。
        /// </summary>
        private static string ExtractContentText(IEnumerable<ModelContextProtocol.Protocol.ContentBlock> blocks)
        {
            if (blocks == null)
                return string.Empty;

            var parts = new List<string>();
            foreach (var block in blocks)
            {
                switch (block)
                {
                    case ModelContextProtocol.Protocol.TextContentBlock text:
                        parts.Add(text.Text ?? string.Empty);
                        break;
                    case ModelContextProtocol.Protocol.ImageContentBlock image:
                        parts.Add($"[image content: {image.MimeType}, {image.DecodedData.Length} bytes]");
                        break;
                    case ModelContextProtocol.Protocol.AudioContentBlock audio:
                        parts.Add($"[audio content: {audio.MimeType}, {audio.DecodedData.Length} bytes]");
                        break;
                    case ModelContextProtocol.Protocol.EmbeddedResourceBlock embedded:
                        parts.Add(DescribeResource(embedded.Resource));
                        break;
                    case ModelContextProtocol.Protocol.ResourceLinkBlock link:
                        parts.Add($"[resource link: {link.Name} ({link.Uri})]");
                        break;
                    default:
                        parts.Add($"[unsupported content block: {block?.Type}]");
                        break;
                }
            }

            return string.Join("\n", parts);
        }

        private static string DescribeResource(ModelContextProtocol.Protocol.ResourceContents resource)
        {
            switch (resource)
            {
                case ModelContextProtocol.Protocol.TextResourceContents text:
                    return text.Text ?? string.Empty;
                case ModelContextProtocol.Protocol.BlobResourceContents blob:
                    return $"[binary resource: {blob.Uri}, {blob.MimeType}, {blob.DecodedData.Length} bytes]";
                default:
                    return $"[resource: {resource?.Uri}]";
            }
        }

        private static ModelContextProtocol.Client.HttpTransportMode ToSdkTransportMode(string mode)
        {
            switch (mode)
            {
                case "Sse":
                    return ModelContextProtocol.Client.HttpTransportMode.Sse;
                case "StreamableHttp":
                    return ModelContextProtocol.Client.HttpTransportMode.StreamableHttp;
                default:
                    return ModelContextProtocol.Client.HttpTransportMode.AutoDetect;
            }
        }

        private static string GetSchemaJson(McpClientTool tool)
        {
            try
            {
                // McpClientTool.ProtocolTool.InputSchema 是真实的工具参数 schema
                var schema = tool.ProtocolTool?.InputSchema;
                if (schema.Value.ValueKind != System.Text.Json.JsonValueKind.Undefined &&
                    schema.Value.ValueKind != System.Text.Json.JsonValueKind.Null)
                {
                    return schema.Value.GetRawText();
                }
            }
            catch
            {
                // fall through
            }

            return "{\"type\":\"object\",\"properties\":{}}";
        }
    }
}

