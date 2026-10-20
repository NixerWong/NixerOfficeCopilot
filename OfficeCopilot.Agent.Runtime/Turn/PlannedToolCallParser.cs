using OfficeCopilot.Agent.Client;
using System;
using System.Collections.Generic;

namespace OfficeCopilot.Agent.Runtime.Turn
{
    /// <summary>
    /// 解析面板发来的显式工具调用指令（metadata / /tools / /context / /call / /tool）。
    /// 从 <see cref="BrokerTurnExecutor"/> 迁出（阶段 7B 补刀），全部为纯静态方法。
    /// </summary>
    internal static class PlannedToolCallParser
    {
        private const string RuntimeListToolsName = "runtime.list_tools";
        private const string RuntimeGetHostContextName = "runtime.get_host_context";
        private const string ToolNameMetadataKey = "tool.name";
        private const string ToolArgumentsMetadataKey = "tool.arguments";
        private const string ToolArgumentsJsonMetadataKey = "tool.arguments_json";

        public static List<PlannedToolCall> BuildPlannedToolCalls(AgentBrokerTurnRequest request)
        {
            var p = new List<PlannedToolCall>();
            if (request == null) return p;

            if (TryGetMetadataValue(request, ToolNameMetadataKey, out var tn) && !string.IsNullOrWhiteSpace(tn))
            {
                if (!TryGetMetadataValue(request, ToolArgumentsJsonMetadataKey, out var aj)
                    && !TryGetMetadataValue(request, ToolArgumentsMetadataKey, out aj))
                    aj = "{}";
                p.Add(new PlannedToolCall(tn.Trim(), PlannedToolCall.NormalizeArgumentsJson(aj)));
                return p;
            }

            var prompt = request.UserInput ?? string.Empty;
            if (IsToolsPrompt(prompt)) { p.Add(new PlannedToolCall(RuntimeListToolsName, "{}")); return p; }
            if (IsHostContextPrompt(prompt)) { p.Add(new PlannedToolCall(RuntimeGetHostContextName, "{}")); return p; }

            var ec = TryParseExplicitToolCommand(prompt);
            if (ec != null) p.Add(ec);
            return p;
        }

        private static bool TryGetMetadataValue(AgentBrokerTurnRequest r, string k, out string v)
        {
            v = string.Empty;
            return r?.Metadata != null && !string.IsNullOrWhiteSpace(k) && r.Metadata.TryGetValue(k, out v);
        }

        private static bool IsToolsPrompt(string prompt)
        {
            if (string.IsNullOrWhiteSpace(prompt)) return false;
            var n = prompt.Trim();
            return string.Equals(n, "/tools", StringComparison.OrdinalIgnoreCase)
                || string.Equals(n, "tools", StringComparison.OrdinalIgnoreCase)
                || n.IndexOf("list tools", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool IsHostContextPrompt(string prompt)
        {
            if (string.IsNullOrWhiteSpace(prompt)) return false;
            var n = prompt.Trim();
            return string.Equals(n, "/context", StringComparison.OrdinalIgnoreCase)
                || n.IndexOf("host context", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static PlannedToolCall TryParseExplicitToolCommand(string prompt)
        {
            if (string.IsNullOrWhiteSpace(prompt)) return null;
            var n = prompt.Trim();

            string pf = n.StartsWith("/call ", StringComparison.OrdinalIgnoreCase) ? "/call "
                : n.StartsWith("/tool ", StringComparison.OrdinalIgnoreCase) ? "/tool "
                : null;
            if (pf == null) return null;

            var r = n.Substring(pf.Length).Trim();
            if (string.IsNullOrWhiteSpace(r)) return null;

            var si = r.IndexOf(' ');
            return si < 0
                ? new PlannedToolCall(r, "{}")
                : new PlannedToolCall(r.Substring(0, si).Trim(), PlannedToolCall.NormalizeArgumentsJson(r.Substring(si + 1).Trim()));
        }
    }
}
