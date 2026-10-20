using OfficeCopilot.Agent.Abstractions;
using System.Collections.Generic;
using System.Linq;

namespace OfficeCopilot.Host.Excel.Tools
{
    public static class ExcelBuiltInTools
    {
        public static IAgentTool[] CreateDefaultTools()
        {
            return ExcelBuiltInToolCatalog.CreateAllTools();
        }

        public static IAgentTool[] CreateConfiguredTools(ExcelToolRegistrationOptions options)
        {
            return ExcelBuiltInToolCatalog.CreateConfiguredTools(options);
        }

        public static IAgentTool[] CreateConfiguredTools(ExcelToolCatalogOptions options)
        {
            return ExcelBuiltInToolCatalog.CreateConfiguredTools(options);
        }

        public static IAgentTool[] CreateAllAvailableTools()
        {
            return ExcelBuiltInToolCatalog.CreateAllAvailableTools();
        }

        public static IAgentTool[] CreateAllAvailableTools(ExcelToolCatalogOptions options)
        {
            return ExcelBuiltInToolCatalog.CreateAllAvailableTools(options);
        }

        public static ToolDefinition[] CreateAllAvailableDefinitions()
        {
            return ExcelBuiltInToolCatalog.CreateAllAvailableDefinitions();
        }

        public static ToolDefinition[] CreateAllAvailableDefinitions(ExcelToolCatalogOptions options)
        {
            return ExcelBuiltInToolCatalog.CreateAllAvailableDefinitions(options);
        }
    }

    /// <summary>
    /// Excel 的工具注册选择。语义完全继承自 <see cref="ToolRegistrationOptions"/>，
    /// 保留独立类型只是为了让调用方的意图更清晰。
    /// </summary>
    public sealed class ExcelToolRegistrationOptions : ToolRegistrationOptions
    {
        public ExcelToolRegistrationOptions()
        {
        }

        public ExcelToolRegistrationOptions(IEnumerable<string> enabledGroupNames, IEnumerable<string> enabledToolNames)
            : base(enabledGroupNames, enabledToolNames)
        {
        }
    }

    /// <summary>
    /// Excel 的工具目录选项。语义完全继承自 <see cref="ToolCatalogOptions"/>。
    /// </summary>
    public sealed class ExcelToolCatalogOptions : ToolCatalogOptions
    {
    }
}