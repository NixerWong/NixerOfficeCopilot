using System;
using System.Collections.Generic;
using System.Linq;
using OfficeCopilot.Agent.Abstractions;
using OfficeCopilot.OpenXml.Tools;

namespace OfficeCopilot.Host.Word.Tools
{
    public static class WordBuiltInToolCatalog
    {
        private const string HostKind = "word";

        public static IAgentTool[] CreateContextTools()
        {
            return CreateContextTools(null);
        }

        public static IAgentTool[] CreateContextTools(WordToolCatalogOptions options)
        {
            return CreateGroupTools(
                WordToolGroups.Context,
                options == null ? null : options.DefinitionOverrides,
                new WordGetActiveDocumentTool(),
                new WordGetSelectionTool());
        }

        public static IAgentTool[] CreateReadTools()
        {
            return CreateReadTools(null);
        }

        public static IAgentTool[] CreateReadTools(WordToolCatalogOptions options)
        {
            return CreateGroupTools(
                WordToolGroups.Read,
                options == null ? null : options.DefinitionOverrides,
                new WordReadDocumentTool(),
                new WordReadOutlineTool(),
                new WordReadTableTool(),
                new WordReadParagraphsTool());
        }

        public static IAgentTool[] CreateWriteTools()
        {
            return CreateWriteTools(null);
        }

        public static IAgentTool[] CreateWriteTools(WordToolCatalogOptions options)
        {
            return CreateGroupTools(
                WordToolGroups.Write,
                options == null ? null : options.DefinitionOverrides,
                new WordInsertTextTool(),
                new WordFindReplaceTool(),
                new WordFormatSelectionTool(),
                new WordSetParagraphFormatTool(),
                new WordFormatParagraphsTool(),
                new WordApplyStyleTool(),
                new WordSetPageSetupTool(),
                new WordInsertTableTool(),
                new WordAddBookmarkTool(),
                new WordAddHyperlinkTool(),
                new WordSetHeaderFooterTool(),
                new WordInsertFieldTool(),
                new WordAddContentControlTool());
        }

        public static IAgentTool[] CreateNavigationTools()
        {
            return CreateNavigationTools(null);
        }

        public static IAgentTool[] CreateNavigationTools(WordToolCatalogOptions options)
        {
            return CreateGroupTools(
                WordToolGroups.Navigation,
                options == null ? null : options.DefinitionOverrides,
                new WordMoveToEndTool(),
                new WordInsertParagraphTool(),
                new WordSelectDocumentTool(),
                new WordSelectParagraphTool());
        }

        public static IAgentTool[] CreateAdvancedTools()
        {
            return CreateAdvancedTools(null);
        }

        public static IAgentTool[] CreateAdvancedTools(WordToolCatalogOptions options)
        {
            return CreateGroupTools(
                WordToolGroups.Advanced,
                options == null ? null : options.DefinitionOverrides,
                new WordListStylesTool(),
                new WordAddCommentTool(),
                new WordToggleTrackChangesTool());
        }

        public static IAgentTool[] CreateFileTools()
        {
            return CreateFileTools(null);
        }

        public static IAgentTool[] CreateFileTools(WordToolCatalogOptions options)
        {
            return CreateGroupTools(
                WordToolGroups.File,
                options == null ? null : options.DefinitionOverrides,
                new OpenXmlFileValidateTool(),
                new OpenXmlFileCreateTool(),
                new OpenXmlFileMergeTool(),
                new WordFileTocTool(),
                new WordFileWatermarkTool(),
                new WordFileNumberingTool(),
                new WordFileFieldTool());
        }

        public static IAgentTool[] CreateAllTools()
        {
            return CreateAllTools(null);
        }

        public static IAgentTool[] CreateAllTools(WordToolCatalogOptions options)
        {
            var tools = new List<IAgentTool>();
            tools.AddRange(CreateContextTools(options));
            tools.AddRange(CreateReadTools(options));
            tools.AddRange(CreateWriteTools(options));
            tools.AddRange(CreateNavigationTools(options));
            tools.AddRange(CreateAdvancedTools(options));
            tools.AddRange(CreateFileTools(options));
            return tools.ToArray();
        }

        public static IAgentTool[] CreateAllAvailableTools()
        {
            return CreateAllAvailableTools(null);
        }

        public static IAgentTool[] CreateAllAvailableTools(WordToolCatalogOptions options)
        {
            var tools = new List<IAgentTool>();
            tools.AddRange(CreateContextTools(options));
            tools.AddRange(CreateReadTools(options));
            tools.AddRange(CreateWriteTools(options));
            tools.AddRange(CreateNavigationTools(options));
            tools.AddRange(CreateAdvancedTools(options));
            tools.AddRange(CreateFileTools(options));
            return tools.ToArray();
        }

        public static ToolDefinition[] CreateAllAvailableDefinitions()
        {
            return CreateAllAvailableDefinitions(null);
        }

        public static ToolDefinition[] CreateAllAvailableDefinitions(WordToolCatalogOptions options)
        {
            return CreateAllAvailableTools(options)
                .Where(p => p != null && p.Definition != null)
                .Select(p => p.Definition.Clone())
                .ToArray();
        }

        public static IAgentTool[] CreateConfiguredTools(WordToolRegistrationOptions options)
        {
            return CreateConfiguredTools(options == null
                ? null
                : new WordToolCatalogOptions
                {
                    Registration = options
                });
        }

        public static IAgentTool[] CreateConfiguredTools(WordToolCatalogOptions options)
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
            AppendEnabledTools(tools, CreateNavigationTools(options), registration);
            AppendEnabledTools(tools, CreateAdvancedTools(options), registration);
            AppendEnabledTools(tools, CreateFileTools(options), registration);
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
