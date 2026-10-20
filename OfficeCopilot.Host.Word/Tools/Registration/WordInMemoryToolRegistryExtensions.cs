using System;
using OfficeCopilot.Agent.Abstractions;

namespace OfficeCopilot.Host.Word.Tools
{
    public static class WordInMemoryToolRegistryExtensions
    {
        public static void RegisterWordBuiltInTools(this InMemoryToolRegistry registry)
        {
            if (registry == null) throw new ArgumentNullException(nameof(registry));
            registry.RegisterRange(WordBuiltInToolCatalog.CreateAllTools());
        }

        public static void RegisterWordBuiltInTools(this InMemoryToolRegistry registry, WordToolRegistrationOptions options)
        {
            if (registry == null) throw new ArgumentNullException(nameof(registry));
            registry.RegisterRange(WordBuiltInToolCatalog.CreateConfiguredTools(options));
        }

        public static void RegisterWordBuiltInTools(this InMemoryToolRegistry registry, WordToolCatalogOptions options)
        {
            if (registry == null) throw new ArgumentNullException(nameof(registry));
            registry.RegisterRange(WordBuiltInToolCatalog.CreateConfiguredTools(options));
        }

        public static void RegisterWordContextBuiltInTools(this InMemoryToolRegistry registry)
        {
            if (registry == null) throw new ArgumentNullException(nameof(registry));
            registry.RegisterRange(WordBuiltInToolCatalog.CreateContextTools());
        }

        public static void RegisterWordReadBuiltInTools(this InMemoryToolRegistry registry)
        {
            if (registry == null) throw new ArgumentNullException(nameof(registry));
            registry.RegisterRange(WordBuiltInToolCatalog.CreateReadTools());
        }

        public static void RegisterWordWriteBuiltInTools(this InMemoryToolRegistry registry)
        {
            if (registry == null) throw new ArgumentNullException(nameof(registry));
            registry.RegisterRange(WordBuiltInToolCatalog.CreateWriteTools());
        }

        public static void RegisterWordAdvancedBuiltInTools(this InMemoryToolRegistry registry)
        {
            if (registry == null) throw new ArgumentNullException(nameof(registry));
            registry.RegisterRange(WordBuiltInToolCatalog.CreateAdvancedTools());
        }

        public static void RegisterWordFileBuiltInTools(this InMemoryToolRegistry registry)
        {
            if (registry == null) throw new ArgumentNullException(nameof(registry));
            registry.RegisterRange(WordBuiltInToolCatalog.CreateFileTools());
        }
    }
}
