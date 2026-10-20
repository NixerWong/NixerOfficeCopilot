using System;
using OfficeCopilot.Agent.Abstractions;

namespace OfficeCopilot.Host.Excel.Tools
{
    public static class InMemoryToolRegistryExtensions
    {
        public static void RegisterExcelBuiltInTools(this InMemoryToolRegistry registry)
        {
            if (registry == null)
            {
                throw new ArgumentNullException(nameof(registry));
            }

            registry.RegisterRange(ExcelBuiltInToolCatalog.CreateAllTools());
        }

        public static void RegisterExcelBuiltInTools(this InMemoryToolRegistry registry, ExcelToolRegistrationOptions options)
        {
            if (registry == null)
            {
                throw new ArgumentNullException(nameof(registry));
            }

            registry.RegisterRange(ExcelBuiltInToolCatalog.CreateConfiguredTools(options));
        }

        public static void RegisterExcelBuiltInTools(this InMemoryToolRegistry registry, ExcelToolCatalogOptions options)
        {
            if (registry == null)
            {
                throw new ArgumentNullException(nameof(registry));
            }

            registry.RegisterRange(ExcelBuiltInToolCatalog.CreateConfiguredTools(options));
        }

        public static void RegisterExcelContextBuiltInTools(this InMemoryToolRegistry registry)
        {
            if (registry == null)
            {
                throw new ArgumentNullException(nameof(registry));
            }

            registry.RegisterRange(ExcelBuiltInToolCatalog.CreateContextTools());
        }

        public static void RegisterExcelDataBuiltInTools(this InMemoryToolRegistry registry)
        {
            if (registry == null)
            {
                throw new ArgumentNullException(nameof(registry));
            }

            registry.RegisterRange(ExcelBuiltInToolCatalog.CreateDataTools());
        }

        public static void RegisterExcelAdvancedBuiltInTools(this InMemoryToolRegistry registry)
        {
            if (registry == null)
            {
                throw new ArgumentNullException(nameof(registry));
            }

            registry.RegisterRange(ExcelBuiltInToolCatalog.CreateAdvancedTools());
        }
    }
}