using System;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OfficeCopilot.Agent.Abstractions;
using OfficeCopilot.Host.Word.Tools;
using OfficeCopilot.Settings;
using OfficeCopilot.Settings.Abstractions;

namespace OfficeCopilot.Agent.Tests.Registration
{
    [TestClass]
    public sealed class ToolCatalogRegistrationTests
    {
        private string _root;

        [TestInitialize]
        public void Setup()
        {
            _root = Path.Combine(Path.GetTempPath(), "nixer-tool-registration-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_root);
        }

        [TestCleanup]
        public void Cleanup()
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, true);
            }
        }

        [TestMethod]
        public void WordCatalog_IsNotFilteredByOtherHostsLegacySettings()
        {
            var legacySettings = new OfficeCopilotSettings();
            legacySettings.ToolGroupRegistrations.Add(new ToolGroupRegistrationSettings
            {
                GroupName = "excel.read",
                IsRegistered = true
            });
            legacySettings.ToolRegistrations.Add(new ToolRegistrationSettings
            {
                GroupName = "excel.read",
                ToolName = "excel_read_range",
                IsRegistered = true
            });

            var settings = new BuiltinToolSettingsStore(_root).LoadMerged("Word", legacySettings);
            var definitions = WordBuiltInToolCatalog.CreateAllAvailableDefinitions();
            var registration = ToolCatalogBuilder.CreateRegistrationOptions(
                definitions,
                settings.GetToolGroupRegistration,
                settings.GetToolRegistration);
            var registered = WordBuiltInToolCatalog.CreateConfiguredTools(
                new WordToolCatalogOptions { Registration = registration });

            Assert.AreEqual(definitions.Length, registered.Length);
        }

        [TestMethod]
        public void ExistingAppFile_MigratesMissingLegacyHostRegistrations()
        {
            var store = new BuiltinToolSettingsStore(_root);
            var appSettings = new BuiltinToolSettings();
            appSettings.ToolRegistrations.Add(new ToolRegistrationSettings
            {
                GroupName = "runtime.context",
                ToolName = "get_reference_content",
                IsRegistered = true
            });
            store.SaveApp("Word", appSettings);

            var legacySettings = new OfficeCopilotSettings();
            legacySettings.ToolGroupRegistrations.Add(new ToolGroupRegistrationSettings
            {
                GroupName = "word.read",
                IsRegistered = false
            });
            legacySettings.ToolRegistrations.Add(new ToolRegistrationSettings
            {
                GroupName = "word.read",
                ToolName = "word.read_document",
                IsRegistered = false
            });

            var settings = store.LoadMerged("Word", legacySettings);

            Assert.AreEqual(false, settings.GetToolGroupRegistration("word.read"));
            Assert.AreEqual(false, settings.GetToolRegistration("word.read_document"));
            Assert.AreEqual(true, settings.GetToolRegistration("get_reference_content"));
        }

        [TestMethod]
        public void RegistrationOptions_OnlyExcludeExplicitlyDisabledEntries()
        {
            var definitions = WordBuiltInToolCatalog.CreateAllAvailableDefinitions();
            var contextTool = definitions.First(d => GetGroupName(d) == WordToolGroups.Context);
            var registration = ToolCatalogBuilder.CreateRegistrationOptions(
                definitions,
                groupName => string.Equals(groupName, WordToolGroups.Read, StringComparison.OrdinalIgnoreCase)
                    ? (bool?)false
                    : null,
                toolName => string.Equals(toolName, contextTool.Name, StringComparison.OrdinalIgnoreCase)
                    ? (bool?)false
                    : null);

            var registered = WordBuiltInToolCatalog.CreateConfiguredTools(
                new WordToolCatalogOptions { Registration = registration });
            var registeredDefinitions = registered.Select(tool => tool.Definition).ToList();

            Assert.IsFalse(registeredDefinitions.Any(d => GetGroupName(d) == WordToolGroups.Read));
            Assert.IsFalse(registeredDefinitions.Any(d => d.Name == contextTool.Name));
            Assert.IsTrue(registeredDefinitions.Any(d => GetGroupName(d) == WordToolGroups.Context));
            Assert.IsTrue(registeredDefinitions.Any(d => GetGroupName(d) == WordToolGroups.Write));
        }

        [TestMethod]
        public void LegacyPptPrefixedSettings_AreMigratedToPowerPointAppSettings()
        {
            var legacySettings = new OfficeCopilotSettings();
            legacySettings.ToolGroupRegistrations.Add(new ToolGroupRegistrationSettings
            {
                GroupName = "ppt.read",
                IsRegistered = false
            });
            legacySettings.ToolRegistrations.Add(new ToolRegistrationSettings
            {
                GroupName = "ppt.read",
                ToolName = "ppt_list_slides",
                IsRegistered = false
            });

            var settings = new BuiltinToolSettingsStore(_root).LoadMerged("PowerPoint", legacySettings);

            Assert.AreEqual(false, settings.GetToolGroupRegistration("ppt.read"));
            Assert.AreEqual(false, settings.GetToolRegistration("ppt_list_slides"));
            Assert.IsTrue(File.Exists(Path.Combine(_root, "builtin_tools.powerpoint.json")));
        }

        private static string GetGroupName(ToolDefinition definition)
        {
            string groupName;
            return ToolCatalogBuilder.TryGetGroupName(definition, out groupName) ? groupName : string.Empty;
        }
    }
}
