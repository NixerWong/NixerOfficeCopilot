using System;
using System.Collections.Generic;
using System.Linq;
using OfficeCopilot.Agent.Abstractions;
using OfficeCopilot.OpenXml.Tools;

namespace OfficeCopilot.Host.PowerPoint.Tools
{
    /// <summary>
    /// PowerPoint 内置工具目录。结构与 Excel / Word 的目录保持一致：
    /// 按分组构建工具、盖上分组元数据、补齐安全字段、套用用户的定义覆盖。
    /// </summary>
    public static class PowerPointBuiltInToolCatalog
    {
        private const string HostKind = "powerpoint";
        public static IAgentTool[] CreateContextTools()
        {
            return CreateContextTools(null);
        }

        public static IAgentTool[] CreateContextTools(PowerPointToolCatalogOptions options)
        {
            return CreateGroupTools(
                PowerPointToolGroups.Context,
                options == null ? null : options.DefinitionOverrides,
                new PowerPointGetActivePresentationTool(),
                new PowerPointGetSelectionTool());
        }

        public static IAgentTool[] CreateReadTools()
        {
            return CreateReadTools(null);
        }

        public static IAgentTool[] CreateReadTools(PowerPointToolCatalogOptions options)
        {
            return CreateGroupTools(
                PowerPointToolGroups.Read,
                options == null ? null : options.DefinitionOverrides,
                new PowerPointListSlidesTool(),
                new PowerPointReadSlideTool(),
                new PowerPointExtractContentTool(),
                new PowerPointExportSlideImageTool(),
                new PowerPointCheckFontsTool());
        }

        public static IAgentTool[] CreateWriteTools(PowerPointToolCatalogOptions options)
        {
            var tools = new List<IAgentTool>(PowerPointEditTool.CreateAll());
            tools.Add(new PowerPointSaveWithFontsTool());
            tools.Add(new PowerPointSaveCopyTool());
            return CreateGroupTools(
                PowerPointToolGroups.Write,
                options == null ? null : options.DefinitionOverrides,
                tools.ToArray());
        }

        public static IAgentTool[] CreateVbaTools(PowerPointToolCatalogOptions options)
        {
            return CreateGroupTools(
                PowerPointToolGroups.Vba,
                options == null ? null : options.DefinitionOverrides,
                new PowerPointExecuteVbaTool());
        }

        public static IAgentTool[] CreateBackupTools()
        {
            return CreateBackupTools(null);
        }

        public static IAgentTool[] CreateBackupTools(PowerPointToolCatalogOptions options)
        {
            return CreateGroupTools(
                PowerPointToolGroups.Backup,
                options == null ? null : options.DefinitionOverrides,
                new PowerPointListBackupsTool(),
                new PowerPointRestoreBackupTool());
        }

        public static IAgentTool[] CreateStyleTools()
        {
            return CreateStyleTools(null);
        }

        public static IAgentTool[] CreateStyleTools(PowerPointToolCatalogOptions options)
        {
            return CreateGroupTools(
                PowerPointToolGroups.Style,
                options == null ? null : options.DefinitionOverrides,
                new PowerPointStyleSearchTool(),
                new PowerPointStyleGetTool(),
                new PowerPointStylePreviewTool());
        }

        public static IAgentTool[] CreateDeckTools()
        {
            return CreateDeckTools(null);
        }

        public static IAgentTool[] CreateDeckTools(PowerPointToolCatalogOptions options)
        {
            return CreateGroupTools(
                PowerPointToolGroups.Deck,
                options == null ? null : options.DefinitionOverrides,
                new PowerPointDeckCreateTool(),
                new PowerPointDeckPutSlidesTool(),
                new PowerPointDeckGetSlidesTool(),
                new PowerPointDeckRemoveSlidesTool(),
                new PowerPointDeckReorderTool(),
                new PowerPointDeckPreviewTool(),
                new PowerPointDeckValidateTool(),
                new PowerPointDeckBuildTool());
        }

        public static IAgentTool[] CreateFileTools()
        {
            return CreateFileTools(null);
        }

        public static IAgentTool[] CreateFileTools(PowerPointToolCatalogOptions options)
        {
            return CreateGroupTools(
                PowerPointToolGroups.File,
                options == null ? null : options.DefinitionOverrides,
                new OpenXmlFileValidateTool(),
                new OpenXmlFileCreateTool(),
                new OpenXmlFileMergeTool(),
                new PowerPointFileAddSlideTool(),
                new PowerPointFileReadTextTool(),
                new PowerPointFileAddTextBoxTool(),
                new PowerPointFileSetShapeTextTool(),
                new PowerPointFileAddTableTool(),
                new PowerPointFileAddPictureTool(),
                new PowerPointFileAddNotesTool(),
                new PowerPointFileSetBackgroundTool(),
                new PowerPointFileAddChartTool());
        }

        public static IAgentTool[] CreateAllTools()
        {
            return CreateAllTools(null);
        }

        public static IAgentTool[] CreateAllTools(PowerPointToolCatalogOptions options)
        {
            var tools = new List<IAgentTool>();
            tools.AddRange(CreateContextTools(options));
            tools.AddRange(CreateReadTools(options));
            tools.AddRange(CreateWriteTools(options));
            tools.AddRange(CreateBackupTools(options));
            tools.AddRange(CreateStyleTools(options));
            tools.AddRange(CreateDeckTools(options));
            tools.AddRange(CreateFileTools(options));
            tools.AddRange(CreateVbaTools(options));
            return tools.ToArray();
        }

        public static IAgentTool[] CreateAllAvailableTools()
        {
            return CreateAllAvailableTools(null);
        }

        public static IAgentTool[] CreateAllAvailableTools(PowerPointToolCatalogOptions options)
        {
            return CreateAllTools(options);
        }

        public static ToolDefinition[] CreateAllAvailableDefinitions()
        {
            return CreateAllAvailableDefinitions(null);
        }

        public static ToolDefinition[] CreateAllAvailableDefinitions(PowerPointToolCatalogOptions options)
        {
            return CreateAllAvailableTools(options)
                .Where(p => p != null && p.Definition != null)
                .Select(p => p.Definition.Clone())
                .ToArray();
        }

        public static IAgentTool[] CreateConfiguredTools(PowerPointToolRegistrationOptions options)
        {
            return CreateConfiguredTools(options == null
                ? null
                : new PowerPointToolCatalogOptions
                {
                    Registration = options
                });
        }

        public static IAgentTool[] CreateConfiguredTools(PowerPointToolCatalogOptions options)
        {
            var registration = options == null ? null : options.Registration;
            if (registration == null || !registration.UseExplicitSelection)
            {
                return CreateAllTools(options);
            }

            var tools = new List<IAgentTool>();
            AppendEnabledTools(tools, CreateContextTools(options), registration);
            AppendEnabledTools(tools, CreateReadTools(options), registration);
            AppendEnabledTools(tools, CreateWriteTools(options), registration);
            AppendEnabledTools(tools, CreateBackupTools(options), registration);
            AppendEnabledTools(tools, CreateStyleTools(options), registration);
            AppendEnabledTools(tools, CreateDeckTools(options), registration);
            AppendEnabledTools(tools, CreateFileTools(options), registration);
            AppendEnabledTools(tools, CreateVbaTools(options), registration);
            return tools.ToArray();
        }

        private static void AppendEnabledTools(
            List<IAgentTool> target,
            IEnumerable<IAgentTool> candidates,
            ToolRegistrationOptions options)
        {
            ToolCatalogBuilder.AppendEnabledTools(target, candidates, options);
        }

        private static IAgentTool[] CreateGroupTools(
            string groupName,
            IEnumerable<ToolDefinitionOverride> definitionOverrides,
            params IAgentTool[] tools)
        {
            return ToolCatalogBuilder.CreateGroupTools(HostKind, groupName, definitionOverrides, tools);
        }
    }
}
