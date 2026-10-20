using System;
using System.Collections.Generic;
using System.Linq;
using OfficeCopilot.Agent.Abstractions;
using OfficeCopilot.OpenXml.Tools;

namespace OfficeCopilot.Host.Excel.Tools
{
    public static class ExcelBuiltInToolCatalog
    {
        private const string HostKind = "excel";

        public static IAgentTool[] CreateContextTools()
        {
            return CreateContextTools(null);
        }

        public static IAgentTool[] CreateContextTools(ExcelToolCatalogOptions options)
        {
            return CreateGroupTools(
                ExcelToolGroups.Context,
                options == null ? null : options.DefinitionOverrides,
                new ExcelGetActiveWorkbookTool(),
                new ExcelGetSelectionTool(),
                new ExcelListWorksheetsTool());
        }

        public static IAgentTool[] CreateDataTools()
        {
            return CreateDataTools(null);
        }

        public static IAgentTool[] CreateDataTools(ExcelToolCatalogOptions options)
        {
            return CreateGroupTools(
                ExcelToolGroups.Read,
                options == null ? null : options.DefinitionOverrides,
                new ExcelReadRangeTool(),
                new ExcelReadWorksheetDataTool(),
                new ExcelAnalyzeRangeTool(),
                new ExcelProfileRangeTool(),
                new ExcelAggregateTool(),
                new ExcelReconcileTool(),
                new ExcelCompareKeySetsTool(),
                new ExcelAssertTool(),
                new ExcelListBackupsTool(),
                new ExcelRestoreBackupTool(),
                new ExcelCleanupWorkspaceTool(),
                new ExcelListNamedRangesTool(),
                new ExcelGetNamedRangeValueTool(), 
                new ExcelReadRangeFormulasTool(),
                new ExcelGetRangeInfoTool(),
                new ExcelCaptureWorksheetScreenshotTool(),
                new ExcelGetRangeHyperlinksTool(),
                new ExcelGetRangeValidationTool(),
                new ExcelGetRangeNumberFormatsTool(),
                new ExcelGetRangeMergeInfoTool(),
                new ExcelGetRangeLockInfoTool(),
                new ExcelGetRangeStyleTool(),
                new ExcelFindInRangeTool(),
                new ExcelValidateRangeFormulasTool());
        }

        public static IAgentTool[] CreateWriteTools()
        {
            return CreateWriteTools(null);
        }

        public static IAgentTool[] CreateWriteTools(ExcelToolCatalogOptions options)
        {
            return CreateGroupTools(
                ExcelToolGroups.Write,
                options == null ? null : options.DefinitionOverrides,
                new ExcelWriteRangeTool(),
                new ExcelCreateWorksheetTool(),
                new ExcelSetRangeFormulasTool(),
                new ExcelSetRangeFormatTool(),
                new ExcelExecuteVbaTool(),
                new ExcelClearRangeTool());
        }

        public static IAgentTool[] CreateAdvancedTools()
        {
            return CreateAdvancedTools(null);
        }

        public static IAgentTool[] CreateAdvancedTools(ExcelToolCatalogOptions options)
        {
            return CreateGroupTools(
                ExcelToolGroups.Advanced,
                options == null ? null : options.DefinitionOverrides,
                new ExcelListTablesTool(),
                new ExcelViewTableTool(),
                new ExcelReadTableDataTool(),
                new ExcelListPowerQueriesTool(),
                new ExcelViewPowerQueryTool(),
                new ExcelRefreshPowerQueryTool(),
                new ExcelCheckPowerQueryErrorsTool(),
                new ExcelValidatePowerQueryTool(),
                new ExcelListVbaModulesTool(),
                new ExcelViewVbaModuleTool(),
                new ExcelListConnectionsTool(),
                new ExcelViewConnectionTool(),
                new ExcelListDataModelTablesTool(),
                new ExcelListDataModelMeasuresTool(),
                new ExcelViewDataModelMeasureTool(),
                new ExcelListDataModelRelationshipsTool(),
                new ExcelListDataModelCalculatedColumnsTool(),
                new ExcelViewDataModelCalculatedColumnTool());
        }

        public static IAgentTool[] CreateFileTools()
        {
            return CreateFileTools(null);
        }

        public static IAgentTool[] CreateFileTools(ExcelToolCatalogOptions options)
        {
            return CreateGroupTools(
                ExcelToolGroups.File,
                options == null ? null : options.DefinitionOverrides,
                new OpenXmlFileValidateTool(),
                new OpenXmlFileCreateTool(),
                new OpenXmlFileMergeTool(),
                new ExcelFileReadRangeTool(),
                new ExcelFileWriteRangeTool(),
                new ExcelFileSetFormulaTool(),
                new ExcelFileReadTableTool(),
                new ExcelFileWriteTableTool(),
                new ExcelFileAddTableTool(),
                new ExcelFileInsertRowsTool(),
                new ExcelFileAddNamedRangeTool(),
                new ExcelFileAddValidationTool(),
                new ExcelFileAddDataBarTool(),
                new ExcelFileAddChartTool(),
                new ExcelFileReadChartTool());
        }

        public static IAgentTool[] CreateAllTools()
        {
            return CreateAllTools(null);
        }

        public static IAgentTool[] CreateAllTools(ExcelToolCatalogOptions options)
        {
            var tools = new List<IAgentTool>();
            tools.AddRange(CreateContextTools(options));
            tools.AddRange(CreateDataTools(options));
            tools.AddRange(CreateWriteTools(options));
            tools.AddRange(CreateAdvancedTools(options));
            tools.AddRange(CreateFileTools(options));
            return tools.ToArray();
        }

        public static IAgentTool[] CreateAllAvailableTools()
        {
            return CreateAllAvailableTools(null);
        }

        public static IAgentTool[] CreateAllAvailableTools(ExcelToolCatalogOptions options)
        {
            var tools = new List<IAgentTool>();
            tools.AddRange(CreateContextTools(options));
            tools.AddRange(CreateDataTools(options));
            tools.AddRange(CreateWriteTools(options));
            tools.AddRange(CreateAdvancedTools(options));
            tools.AddRange(CreateFileTools(options));
            return tools.ToArray();
        }

        public static ToolDefinition[] CreateAllAvailableDefinitions()
        {
            return CreateAllAvailableDefinitions(null);
        }

        public static ToolDefinition[] CreateAllAvailableDefinitions(ExcelToolCatalogOptions options)
        {
            return CreateAllAvailableTools(options)
                .Where(p => p != null && p.Definition != null)
                .Select(p => p.Definition.Clone())
                .ToArray();
        }

        public static IAgentTool[] CreateConfiguredTools(ExcelToolRegistrationOptions options)
        {
            return CreateConfiguredTools(options == null
                ? null
                : new ExcelToolCatalogOptions
                {
                    Registration = options
                });
        }

        public static IAgentTool[] CreateConfiguredTools(ExcelToolCatalogOptions options)
        {
            var registration = options == null ? null : options.Registration;
            if (registration == null || !registration.UseExplicitSelection)
            {
                return CreateAllTools(options);
            }

            var tools = new List<IAgentTool>();
            AppendEnabledTools(tools, CreateContextTools(options), registration);
            AppendEnabledTools(tools, CreateDataTools(options), registration);
            AppendEnabledTools(tools, CreateWriteTools(options), registration);
            AppendEnabledTools(tools, CreateAdvancedTools(options), registration);
            AppendEnabledTools(tools, CreateFileTools(options), registration);
            return tools.ToArray();
        }

        private static void AppendEnabledTools(List<IAgentTool> target, IEnumerable<IAgentTool> candidates, ToolRegistrationOptions options)
        {
            ToolCatalogBuilder.AppendEnabledTools(target, candidates, options);
        }

        private static IAgentTool[] CreateGroupTools(string groupName, IEnumerable<ToolDefinitionOverride> definitionOverrides, params IAgentTool[] tools)
        {
            return ToolCatalogBuilder.CreateGroupTools(HostKind, groupName, definitionOverrides, tools);
        }
    }
}