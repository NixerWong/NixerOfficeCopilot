using OfficeCopilot.Agent.Abstractions;
using System;
using System.Collections.Generic;
using System.Linq;

namespace OfficeCopilot.Host.PowerPoint.Tools
{
    public static class PowerPointBuiltInTools
    {
        public static IAgentTool[] CreateDefaultTools()
        {
            return PowerPointBuiltInToolCatalog.CreateAllTools();
        }

        public static IAgentTool[] CreateConfiguredTools(PowerPointToolRegistrationOptions options)
        {
            return PowerPointBuiltInToolCatalog.CreateConfiguredTools(options);
        }

        public static IAgentTool[] CreateConfiguredTools(PowerPointToolCatalogOptions options)
        {
            return PowerPointBuiltInToolCatalog.CreateConfiguredTools(options);
        }

        public static IAgentTool[] CreateAllAvailableTools()
        {
            return PowerPointBuiltInToolCatalog.CreateAllAvailableTools();
        }

        public static IAgentTool[] CreateAllAvailableTools(PowerPointToolCatalogOptions options)
        {
            return PowerPointBuiltInToolCatalog.CreateAllAvailableTools(options);
        }

        public static ToolDefinition[] CreateAllAvailableDefinitions()
        {
            return PowerPointBuiltInToolCatalog.CreateAllAvailableDefinitions();
        }

        public static ToolDefinition[] CreateAllAvailableDefinitions(PowerPointToolCatalogOptions options)
        {
            return PowerPointBuiltInToolCatalog.CreateAllAvailableDefinitions(options);
        }

        /// <summary>
        /// 由用户设置构造注册选择。与"白名单"不同，这里只排除用户**明确关闭**的分组 / 工具：
        /// 新增工具默认可用；旧名（<c>powerpoint_*</c> / <c>powerpoint.*</c>）上的开关迁移到新名。
        /// </summary>
        /// <param name="groupState">分组开关：true / false / null（未配置）。</param>
        /// <param name="toolState">工具开关：true / false / null（未配置）。</param>
        public static PowerPointToolRegistrationOptions CreateRegistrationOptions(
            Func<string, bool?> groupState,
            Func<string, bool?> toolState)
        {
            var registration = ToolCatalogBuilder.CreateRegistrationOptions(
                CreateAllAvailableDefinitions(),
                groupName => ResolveGroupState(groupName, groupState),
                toolName => ResolveToolState(toolName, toolState));
            return new PowerPointToolRegistrationOptions(
                registration.EnabledGroupNames,
                registration.EnabledToolNames);
        }

        private static bool? ResolveGroupState(string groupName, Func<string, bool?> groupState)
        {
            if (groupState == null)
            {
                return null;
            }

            var state = groupState(groupName);
            if (state.HasValue)
            {
                return state;
            }

            foreach (var legacy in new[] { PowerPointToolGroups.LegacyContext, PowerPointToolGroups.LegacyFile })
            {
                if (PowerPointToolGroups.MapLegacy(legacy).Contains(groupName, StringComparer.OrdinalIgnoreCase))
                {
                    state = groupState(legacy);
                    if (state.HasValue)
                    {
                        return state;
                    }
                }
            }

            return null;
        }

        private static bool? ResolveToolState(string toolName, Func<string, bool?> toolState)
        {
            if (toolState == null)
            {
                return null;
            }

            var state = toolState(toolName);
            if (state.HasValue)
            {
                return state;
            }

            foreach (var alias in PowerPointToolNames.LegacyAliases)
            {
                if (string.Equals(alias.Value, toolName, StringComparison.OrdinalIgnoreCase))
                {
                    state = toolState(alias.Key);
                    if (state.HasValue)
                    {
                        return state;
                    }
                }
            }

            return null;
        }
    }

    /// <summary>
    /// PowerPoint 的工具注册选择。语义完全继承自 <see cref="ToolRegistrationOptions"/>。
    /// </summary>
    public sealed class PowerPointToolRegistrationOptions : ToolRegistrationOptions
    {
        public PowerPointToolRegistrationOptions()
        {
        }

        public PowerPointToolRegistrationOptions(IEnumerable<string> enabledGroupNames, IEnumerable<string> enabledToolNames)
            : base(enabledGroupNames, enabledToolNames)
        {
        }
    }

    /// <summary>
    /// PowerPoint 的工具目录选项。语义完全继承自 <see cref="ToolCatalogOptions"/>。
    /// </summary>
    public sealed class PowerPointToolCatalogOptions : ToolCatalogOptions
    {
    }
}
