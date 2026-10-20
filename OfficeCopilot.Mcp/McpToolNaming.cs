using System;

namespace OfficeCopilot.Mcp
{
    /// <summary>
    /// MCP 工具命名工具类。与 cc-haha <c>mcpStringUtils.ts</c> 保持完全一致的命名约定。
    /// 格式：<c>mcp__&lt;serverName&gt;__&lt;toolName&gt;</c>（双下划线分隔）。
    /// </summary>
    public static class McpToolNaming
    {
        /// <summary>MCP 工具名前缀标记。</summary>
        public const string Prefix = "mcp";

        /// <summary>分隔符（双下划线，与 cc-haha 一致）。</summary>
        public const string Separator = "__";

        /// <summary>
        /// 从完整工具名解析 MCP server 名和工具名。
        /// 例如 <c>"mcp__filesystem__read_file"</c> → <c>(server: "filesystem", tool: "read_file")</c>。
        /// 如果不是 MCP 工具名则返回 <c>null</c>。
        /// </summary>
        public static McpToolInfo TryParse(string fullToolName)
        {
            if (string.IsNullOrWhiteSpace(fullToolName))
            {
                return null;
            }

            var parts = fullToolName.Split(new[] { Separator }, StringSplitOptions.None);
            if (parts.Length < 3 || !string.Equals(parts[0], Prefix, StringComparison.Ordinal))
            {
                return null;
            }

            var serverName = parts[1];
            // 工具名可能包含双下划线，需要重新拼接
            var toolName = parts.Length > 3
                ? string.Join(Separator, parts, 2, parts.Length - 2)
                : parts.Length == 3 ? parts[2] : string.Empty;

            return new McpToolInfo(serverName, toolName);
        }

        /// <summary>
        /// 判断工具名是否属于 MCP 工具。
        /// </summary>
        public static bool IsMcpTool(string fullToolName)
        {
            return TryParse(fullToolName) != null;
        }

        /// <summary>
        /// 构造完整的 MCP 工具名。
        /// </summary>
        public static string BuildFullToolName(string serverName, string toolName)
        {
            return Prefix + Separator
                + NormalizeName(serverName)
                + Separator
                + NormalizeName(toolName);
        }

        /// <summary>
        /// 获取 MCP 工具名前缀（用于过滤）。
        /// 例如 <c>"mcp__filesystem__"</c>。
        /// </summary>
        public static string GetServerPrefix(string serverName)
        {
            return Prefix + Separator + NormalizeName(serverName) + Separator;
        }

        /// <summary>
        /// 工具名归一化：去除非字母数字下划线连字符的字符。
        /// 与 cc-haha <c>normalizeNameForMCP</c> 行为一致。
        /// </summary>
        private static string NormalizeName(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return string.Empty;
            }

            var chars = name.ToCharArray();
            for (var i = 0; i < chars.Length; i++)
            {
                var c = chars[i];
                if (!char.IsLetterOrDigit(c) && c != '_' && c != '-')
                {
                    chars[i] = '_';
                }
            }

            return new string(chars);
        }
    }

    /// <summary>
    /// MCP 工具名解析结果。
    /// </summary>
    public sealed class McpToolInfo
    {
        public McpToolInfo(string serverName, string toolName)
        {
            ServerName = serverName ?? string.Empty;
            ToolName = toolName ?? string.Empty;
        }

        public string ServerName { get; }
        public string ToolName { get; }
    }
}
