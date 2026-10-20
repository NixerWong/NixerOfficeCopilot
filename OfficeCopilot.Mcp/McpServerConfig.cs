using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace OfficeCopilot.Mcp
{
    /// <summary>
    /// 解析时被跳过的 server 及原因（配置无效，不含显式 IsEnabled=false 的 server）。
    /// </summary>
    public sealed class McpSkippedServer
    {
        public McpSkippedServer(string name, string reason)
        {
            Name = name;
            Reason = reason;
        }

        public string Name { get; }

        public string Reason { get; }
    }

    /// <summary>
    /// 从 settings.json 解析出的 MCP 配置（运行时内部模型）。
    /// </summary>
    public sealed class McpJsonConfig
    {
        public Dictionary<string, McpServerConfigEntry> McpServers { get; set; }
            = new Dictionary<string, McpServerConfigEntry>();

        /// <summary>解析时被跳过的无效 server，供状态面板展示原因。</summary>
        public List<McpSkippedServer> SkippedServers { get; } = new List<McpSkippedServer>();

        /// <summary>
        /// 从 settings.json 中解析 McpServers 并转换为 MCP 传输配置。
        /// </summary>
        public static McpJsonConfig FromSettingsJson(string settingsJsonPath)
        {
            var config = new McpJsonConfig();

            if (string.IsNullOrWhiteSpace(settingsJsonPath) || !File.Exists(settingsJsonPath))
                return config;

            // 读取或解析失败直接抛出，不静默返回空配置（否则会被误判为“没有配置任何 server”）。
            var json = File.ReadAllText(settingsJsonPath, System.Text.Encoding.UTF8);

            using (var doc = JsonDocument.Parse(json))
            {
                var root = doc.RootElement;
                if (!root.TryGetProperty("McpServers", out var serversEl) ||
                    serversEl.ValueKind != JsonValueKind.Array)
                    return config;

                // server 名大小写不敏感：同名（仅大小写不同）时保留第一个，后续项不覆盖也不并存。
                config.McpServers = new Dictionary<string, McpServerConfigEntry>(StringComparer.OrdinalIgnoreCase);

                foreach (var server in serversEl.EnumerateArray())
                {
                    var declaredName = server.TryGetProperty("Name", out var rawNameEl) && rawNameEl.ValueKind == JsonValueKind.String
                        ? rawNameEl.GetString()
                        : null;
                    var unnamed = string.IsNullOrWhiteSpace(declaredName) ? "(未命名)" : declaredName;

                    if (!server.TryGetProperty("IsEnabled", out var enabledEl) ||
                        (enabledEl.ValueKind != JsonValueKind.True && enabledEl.ValueKind != JsonValueKind.False))
                    {
                        config.SkippedServers.Add(new McpSkippedServer(unnamed, "IsEnabled 缺失或不是布尔值，已跳过。"));
                        continue;
                    }

                    if (!enabledEl.GetBoolean())
                        continue;

                    if (!server.TryGetProperty("Name", out var nameEl) || nameEl.ValueKind != JsonValueKind.String ||
                        string.IsNullOrWhiteSpace(nameEl.GetString()))
                    {
                        config.SkippedServers.Add(new McpSkippedServer(unnamed, "缺少有效的 Name，已跳过。"));
                        continue;
                    }

                    var name = nameEl.GetString();
                    if (config.McpServers.ContainsKey(name))
                    {
                        config.SkippedServers.Add(new McpSkippedServer(name, "与前面的 server 重名（不区分大小写），已跳过。"));
                        continue;
                    }

                    var entry = new McpServerConfigEntry();

                    if (server.TryGetProperty("Stdio", out var stdio) &&
                        stdio.ValueKind == JsonValueKind.Object &&
                        !IsNull(stdio))
                    {
                        entry.Transport = "stdio";

                        if (stdio.TryGetProperty("Command", out var cmd))
                            entry.Command = cmd.GetString() ?? string.Empty;

                        var workingDirectory = stdio.TryGetProperty("WorkingDirectory", out var wd)
                            ? wd.GetString()
                            : null;
                        if (!string.IsNullOrWhiteSpace(workingDirectory))
                            entry.WorkingDirectory = workingDirectory;

                        // 仅含路径分隔符的相对命令（如 bin\server.exe）才相对于工作目录解析；
                        // 裸命令（如 npx、node）保留原样，交由 PATH 查找。
                        if (!string.IsNullOrWhiteSpace(entry.WorkingDirectory) &&
                            !string.IsNullOrWhiteSpace(entry.Command) &&
                            !Path.IsPathRooted(entry.Command) &&
                            entry.Command.IndexOfAny(new[] { '/', '\\' }) >= 0)
                        {
                            entry.Command = Path.GetFullPath(Path.Combine(entry.WorkingDirectory, entry.Command));
                        }

                        if (stdio.TryGetProperty("Arguments", out var argsEl) &&
                            argsEl.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var a in argsEl.EnumerateArray())
                            {
                                var s = a.GetString();
                                if (!string.IsNullOrWhiteSpace(s))
                                    entry.Args.Add(s);
                            }
                        }

                        if (stdio.TryGetProperty("EnvironmentVariables", out var envVars) &&
                            envVars.ValueKind == JsonValueKind.Array)
                        {
                            entry.Env = new Dictionary<string, string>();
                            foreach (var ev in envVars.EnumerateArray())
                            {
                                if (ev.TryGetProperty("Name", out var en) &&
                                    ev.TryGetProperty("Value", out var evv))
                                {
                                    var key = en.GetString();
                                    if (!string.IsNullOrWhiteSpace(key))
                                        entry.Env[key] = evv.GetString() ?? string.Empty;
                                }
                            }
                        }
                    }
                    else if (server.TryGetProperty("Http", out var http) &&
                             http.ValueKind == JsonValueKind.Object &&
                             !IsNull(http))
                    {
                        entry.Transport = "http";

                        if (http.TryGetProperty("Endpoint", out var url))
                            entry.Url = url.GetString() ?? string.Empty;

                        if (http.TryGetProperty("TransportMode", out var modeEl))
                        {
                            var mode = ParseHttpTransportMode(modeEl);
                            if (mode == null)
                            {
                                config.SkippedServers.Add(new McpSkippedServer(
                                    name, "TransportMode 无效（应为 AutoDetect、StreamableHttp 或 Sse），已跳过。"));
                                continue;
                            }

                            entry.TransportMode = mode;
                        }

                        if (http.TryGetProperty("Headers", out var hdrs) &&
                            hdrs.ValueKind == JsonValueKind.Array)
                        {
                            entry.Headers = new Dictionary<string, string>();
                            foreach (var h in hdrs.EnumerateArray())
                            {
                                if (h.TryGetProperty("Name", out var hn) &&
                                    h.TryGetProperty("Value", out var hv))
                                {
                                    var key = hn.GetString();
                                    if (!string.IsNullOrWhiteSpace(key))
                                        entry.Headers[key] = hv.GetString() ?? string.Empty;
                                }
                            }
                        }
                    }
                    else
                    {
                        config.SkippedServers.Add(new McpSkippedServer(name, "未配置有效的 Stdio 或 Http，已跳过。"));
                        continue;
                    }

                    // 仅显式 IsEnabled=false 的工具被禁用；缺失或非布尔值视为启用（与 McpToolSettings 默认值一致）。
                    if (server.TryGetProperty("Tools", out var toolsEl) &&
                        toolsEl.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var tool in toolsEl.EnumerateArray())
                        {
                            if (tool.ValueKind != JsonValueKind.Object ||
                                !tool.TryGetProperty("Name", out var toolNameEl) ||
                                toolNameEl.ValueKind != JsonValueKind.String)
                                continue;

                            var toolName = toolNameEl.GetString();
                            if (string.IsNullOrWhiteSpace(toolName))
                                continue;

                            if (tool.TryGetProperty("IsEnabled", out var toolEnabledEl) &&
                                toolEnabledEl.ValueKind == JsonValueKind.False)
                                entry.DisabledTools.Add(toolName.Trim());
                        }
                    }

                    config.McpServers[name] = entry;
                }
            }

            return config;
        }

        /// <summary>
        /// 解析 HTTP TransportMode。settings 由 StringEnumConverter 写入枚举名，数值 0/1/2 也接受。
        /// 缺失或 null 视为 AutoDetect；无法识别时返回 null。
        /// </summary>
        private static string ParseHttpTransportMode(JsonElement el)
        {
            switch (el.ValueKind)
            {
                case JsonValueKind.Null:
                case JsonValueKind.Undefined:
                    return "AutoDetect";

                case JsonValueKind.String:
                    var text = el.GetString();
                    foreach (var known in new[] { "AutoDetect", "StreamableHttp", "Sse" })
                    {
                        if (string.Equals(text, known, StringComparison.OrdinalIgnoreCase))
                            return known;
                    }
                    return null;

                case JsonValueKind.Number:
                    if (!el.TryGetInt32(out var number))
                        return null;
                    switch (number)
                    {
                        case 0: return "AutoDetect";
                        case 1: return "Sse";
                        case 2: return "StreamableHttp";
                        default: return null;
                    }

                default:
                    return null;
            }
        }

        private static bool IsNull(JsonElement el)
        {
            return el.ValueKind == JsonValueKind.Null;
        }
    }

    /// <summary>
    /// 单条 MCP server 配置。
    /// 支持 stdio 和 http 两种 transport。
    /// </summary>
    public sealed class McpServerConfigEntry
    {
        /// <summary>传输类型："stdio" 或 "http"。</summary>
        public string Transport { get; set; }

        // ---- stdio 专用字段 ----

        /// <summary>命令行（stdio transport）。</summary>
        public string Command { get; set; }

        /// <summary>命令行参数（stdio transport）。</summary>
        public List<string> Args { get; set; } = new List<string>();

        /// <summary>环境变量（stdio transport）。</summary>
        public Dictionary<string, string> Env { get; set; }

        /// <summary>子进程工作目录（stdio transport，可选）。</summary>
        public string WorkingDirectory { get; set; }

        // ---- http/sse 专用字段 ----

        /// <summary>HTTP/SSE URL。</summary>
        public string Url { get; set; }

        /// <summary>HTTP 请求头。</summary>
        public Dictionary<string, string> Headers { get; set; }

        /// <summary>HTTP transport 模式："AutoDetect"、"StreamableHttp" 或 "Sse"（仅 http 有效）。</summary>
        public string TransportMode { get; set; } = "AutoDetect";

        // ---- 通用字段 ----

        /// <summary>在 settings 中被单独禁用的工具名（原始工具名，不含 server 前缀）。</summary>
        public List<string> DisabledTools { get; set; } = new List<string>();

        /// <summary>
        /// 判断是否为 stdio transport。
        /// </summary>
        public bool IsStdio =>
            string.Equals(Transport, "stdio", System.StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// 判断是否为 http/sse transport。
        /// </summary>
        public bool IsHttp =>
            string.Equals(Transport, "http", System.StringComparison.OrdinalIgnoreCase)
            || string.Equals(Transport, "sse", System.StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// MCP server 连接状态。
    /// </summary>
    public enum McpServerConnectionState
    {
        /// <summary>未连接。</summary>
        Disconnected = 0,

        /// <summary>正在连接。</summary>
        Connecting = 1,

        /// <summary>已连接并就绪。</summary>
        Connected = 2,

        /// <summary>连接失败。</summary>
        Failed = 3
    }

    /// <summary>
    /// MCP server 运行时状态。
    /// </summary>
    public sealed class McpServerState
    {
        public string ServerName { get; set; } = string.Empty;

        public McpServerConnectionState State { get; set; } = McpServerConnectionState.Disconnected;

        public string ErrorMessage { get; set; }

        public int ToolCount { get; set; }
    }
}
