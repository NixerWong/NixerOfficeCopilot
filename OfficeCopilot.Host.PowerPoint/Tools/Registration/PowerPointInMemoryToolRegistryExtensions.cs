using System;
using OfficeCopilot.Agent.Abstractions;

namespace OfficeCopilot.Host.PowerPoint.Tools
{
    public static class PowerPointInMemoryToolRegistryExtensions
    {
        public static void RegisterPowerPointBuiltInTools(this InMemoryToolRegistry registry)
        {
            if (registry == null) throw new ArgumentNullException(nameof(registry));
            registry.RegisterRange(PowerPointBuiltInToolCatalog.CreateAllTools());
            RegisterPowerPointLegacyAliases(registry);
        }

        public static void RegisterPowerPointBuiltInTools(this InMemoryToolRegistry registry, PowerPointToolRegistrationOptions options)
        {
            if (registry == null) throw new ArgumentNullException(nameof(registry));
            registry.RegisterRange(PowerPointBuiltInToolCatalog.CreateConfiguredTools(options));
            RegisterPowerPointLegacyAliases(registry);
        }

        public static void RegisterPowerPointBuiltInTools(this InMemoryToolRegistry registry, PowerPointToolCatalogOptions options)
        {
            if (registry == null) throw new ArgumentNullException(nameof(registry));
            registry.RegisterRange(PowerPointBuiltInToolCatalog.CreateConfiguredTools(options));
            RegisterPowerPointLegacyAliases(registry);
        }

        /// <summary><c>powerpoint_*</c> 旧名路由到 <c>ppt_*</c> 新工具（蓝图 Q6）。</summary>
        public static void RegisterPowerPointLegacyAliases(this InMemoryToolRegistry registry)
        {
            if (registry == null) throw new ArgumentNullException(nameof(registry));
            foreach (var alias in PowerPointToolNames.LegacyAliases)
            {
                registry.RegisterAlias(alias.Key, alias.Value);
            }
        }

        public static void RegisterPowerPointContextBuiltInTools(this InMemoryToolRegistry registry)
        {
            if (registry == null) throw new ArgumentNullException(nameof(registry));
            registry.RegisterRange(PowerPointBuiltInToolCatalog.CreateContextTools());
        }

        public static void RegisterPowerPointFileBuiltInTools(this InMemoryToolRegistry registry)
        {
            if (registry == null) throw new ArgumentNullException(nameof(registry));
            registry.RegisterRange(PowerPointBuiltInToolCatalog.CreateFileTools());
        }
    }
}
