using System;
using System.Collections.Generic;

namespace OfficeCopilot.Agent.Abstractions
{
    /// <summary>
    /// 宿主工具目录的公共骨架。Excel / Word / PowerPoint 三份目录原先各自复制了一份
    /// 完全相同的 CreateGroupTools / ApplyMetadata / CreateOverrideLookup / DelegatingAgentTool，
    /// 这里统一收拢，宿主目录只需要声明"哪些工具属于哪个组"。
    /// </summary>
    public static class ToolCatalogBuilder
    {
        /// <summary>
        /// 为一组工具补齐安全字段、套用用户覆盖、盖上分组与宿主元数据。
        /// 返回的是包装后的新实例，不会修改传入工具的原始定义。
        /// </summary>
        public static IAgentTool[] CreateGroupTools(
            string hostKind,
            string groupName,
            IEnumerable<ToolDefinitionOverride> definitionOverrides,
            params IAgentTool[] tools)
        {
            if (tools == null)
            {
                return new IAgentTool[0];
            }

            var overrideLookup = CreateOverrideLookup(definitionOverrides);
            var configuredTools = new IAgentTool[tools.Length];

            for (var i = 0; i < tools.Length; i++)
            {
                configuredTools[i] = CreateConfiguredTool(tools[i], hostKind, groupName, overrideLookup);
            }

            return configuredTools;
        }

        /// <summary>
        /// 按用户的注册选择过滤候选工具。工具组与工具名必须同时命中。
        /// </summary>
        public static void AppendEnabledTools(
            IList<IAgentTool> target,
            IEnumerable<IAgentTool> candidates,
            ToolRegistrationOptions options)
        {
            if (target == null || candidates == null || options == null)
            {
                return;
            }

            foreach (var tool in candidates)
            {
                var definition = tool == null ? null : tool.Definition;
                if (definition == null)
                {
                    continue;
                }

                string groupName;
                if (!TryGetGroupName(definition, out groupName))
                {
                    continue;
                }

                if (!options.EnabledGroupNames.Contains(groupName)
                    || !options.EnabledToolNames.Contains(definition.Name))
                {
                    continue;
                }

                target.Add(tool);
            }
        }

        public static bool TryGetGroupName(ToolDefinition definition, out string groupName)
        {
            groupName = string.Empty;
            if (definition == null || definition.Metadata == null)
            {
                return false;
            }

            return definition.Metadata.TryGetValue(ToolMetadataKeys.GroupName, out groupName)
                && !string.IsNullOrWhiteSpace(groupName);
        }

        private static IAgentTool CreateConfiguredTool(
            IAgentTool tool,
            string hostKind,
            string groupName,
            IDictionary<string, ToolDefinitionOverride> overrideLookup)
        {
            if (tool == null || tool.Definition == null)
            {
                return tool;
            }

            var definition = tool.Definition.Clone();
            ToolSafetyPolicy.Normalize(definition);

            ToolDefinitionOverride definitionOverride;
            if (overrideLookup != null
                && overrideLookup.TryGetValue(definition.Name ?? string.Empty, out definitionOverride))
            {
                definition.ApplyOverride(definitionOverride);
            }

            ApplyMetadata(definition, hostKind, groupName);
            var preflight = tool as IToolExecutionPreflight;
            return preflight == null
                ? new DelegatingAgentTool(tool, definition)
                : new PreflightDelegatingAgentTool(tool, preflight, definition);
        }

        private static void ApplyMetadata(ToolDefinition definition, string hostKind, string groupName)
        {
            if (definition == null)
            {
                return;
            }

            if (definition.Metadata == null)
            {
                definition.Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            }

            if (definition.Tags == null)
            {
                definition.Tags = new List<string>();
            }

            var host = hostKind ?? string.Empty;

            definition.Metadata[ToolMetadataKeys.GroupName] = groupName ?? string.Empty;
            definition.Metadata[ToolMetadataKeys.HostKind] = host;
            definition.Metadata[ToolMetadataKeys.CapabilityScope] = "host-private";
            definition.Metadata[ToolMetadataKeys.SelfTestEnabled] = "true";
            definition.Metadata[ToolMetadataKeys.RiskLevel] = definition.RiskLevel ?? string.Empty;
            definition.Metadata[ToolMetadataKeys.RequiresBackup] = definition.RequiresBackup ? "true" : "false";
            definition.Metadata[ToolMetadataKeys.TimeoutSeconds] = definition.TimeoutSeconds.ToString();

            AddTag(definition.Tags, host);
            AddTag(definition.Tags, "host-private");
            AddTag(definition.Tags, groupName);
        }

        private static void AddTag(IList<string> tags, string value)
        {
            if (tags == null || string.IsNullOrWhiteSpace(value))
            {
                return;
            }

            for (var i = 0; i < tags.Count; i++)
            {
                if (string.Equals(tags[i], value, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }
            }

            tags.Add(value);
        }

        private static IDictionary<string, ToolDefinitionOverride> CreateOverrideLookup(
            IEnumerable<ToolDefinitionOverride> definitionOverrides)
        {
            var lookup = new Dictionary<string, ToolDefinitionOverride>(StringComparer.OrdinalIgnoreCase);
            if (definitionOverrides == null)
            {
                return lookup;
            }

            foreach (var definitionOverride in definitionOverrides)
            {
                if (definitionOverride == null || string.IsNullOrWhiteSpace(definitionOverride.ToolName))
                {
                    continue;
                }

                lookup[definitionOverride.ToolName.Trim()] = definitionOverride;
            }

            return lookup;
        }

        /// <summary>
        /// 保留原工具的执行逻辑，只替换对外暴露的 <see cref="ToolDefinition"/>。
        /// 原工具实现的可选能力接口（如 <see cref="IToolExecutionPreflight"/>）必须由派生包装继续暴露，
        /// 否则调用方的 <c>tool as IXxx</c> 探测会失效。
        /// </summary>
        private class DelegatingAgentTool : IAgentTool
        {
            private readonly IAgentTool _innerTool;
            private readonly ToolDefinition _definition;

            public DelegatingAgentTool(IAgentTool innerTool, ToolDefinition definition)
            {
                _innerTool = innerTool;
                _definition = definition;
            }

            public ToolDefinition Definition
            {
                get { return _definition; }
            }

            public System.Threading.Tasks.Task<ToolExecutionResult> ExecuteAsync(
                ToolExecutionContext context,
                ToolExecutionRequest request)
            {
                return _innerTool.ExecuteAsync(context, request);
            }
        }

        private sealed class PreflightDelegatingAgentTool : DelegatingAgentTool, IToolExecutionPreflight
        {
            private readonly IToolExecutionPreflight _innerPreflight;

            public PreflightDelegatingAgentTool(
                IAgentTool innerTool,
                IToolExecutionPreflight innerPreflight,
                ToolDefinition definition)
                : base(innerTool, definition)
            {
                _innerPreflight = innerPreflight;
            }

            public ToolExecutionPreflightResult Preflight(
                ToolExecutionContext context,
                ToolExecutionRequest request)
            {
                return _innerPreflight.Preflight(context, request);
            }
        }
    }
}
