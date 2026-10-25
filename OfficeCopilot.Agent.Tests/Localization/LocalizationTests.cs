using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Resources;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OfficeCopilot.Agent.Client;
using OfficeCopilot.Agent.Runtime.Turn;
using OfficeCopilot.HostBridge.Contracts;
using OfficeCopilot.Host.Excel.Tools;
using OfficeCopilot.Host.PowerPoint.Tools;
using OfficeCopilot.Host.Word.Tools;
using OfficeCopilot.Localization;
using OfficeCopilot.Settings;
using OfficeCopilot.Settings.Abstractions;

namespace OfficeCopilot.Agent.Tests.Localization
{
    [TestClass]
    public sealed class LocalizationTests
    {
        [TestMethod]
        public void ResolveCulture_UsesTheSelectedLanguageAndDefaultsToEnglish()
        {
            Assert.AreEqual("zh-Hans", SupportedLanguages.ResolveCulture("zh-CN").Name);
            Assert.AreEqual("zh-Hans", SupportedLanguages.ResolveCulture("zh-Hans").Name);
            Assert.AreEqual("ja", SupportedLanguages.ResolveCulture("ja").Name);
            Assert.AreEqual("ja", SupportedLanguages.ResolveCulture("ja-JP").Name);
            Assert.AreEqual("de", SupportedLanguages.ResolveCulture("de").Name);
            Assert.AreEqual("de", SupportedLanguages.ResolveCulture("de-DE").Name);
            Assert.AreEqual("es", SupportedLanguages.ResolveCulture("es").Name);
            Assert.AreEqual("es", SupportedLanguages.ResolveCulture("es-ES").Name);
            Assert.AreEqual("en", SupportedLanguages.ResolveCulture("en").Name);
            Assert.AreEqual("en", SupportedLanguages.ResolveCulture("auto").Name);
            Assert.AreEqual("en", SupportedLanguages.ResolveCulture("unsupported").Name);
        }

        [TestMethod]
        public void NormalizePreference_OnlyAllowsRegisteredLanguageCodes()
        {
            Assert.AreEqual("en", SupportedLanguages.NormalizePreference(null));
            Assert.AreEqual("zh-Hans", SupportedLanguages.NormalizePreference("zh-CN"));
            Assert.AreEqual("ja", SupportedLanguages.NormalizePreference("ja-JP"));
            Assert.AreEqual(2, SupportedLanguages.GetIndex("ja"));
            Assert.AreEqual("ja", SupportedLanguages.GetCodeAt(2));
            Assert.AreEqual(3, SupportedLanguages.GetIndex("de-DE"));
            Assert.AreEqual("de", SupportedLanguages.GetCodeAt(3));
            Assert.AreEqual(4, SupportedLanguages.GetIndex("es-ES"));
            Assert.AreEqual("es", SupportedLanguages.GetCodeAt(4));
            Assert.AreEqual("en", SupportedLanguages.NormalizePreference("EN"));
            Assert.AreEqual("en", SupportedLanguages.NormalizePreference("auto"));
            Assert.AreEqual("en", SupportedLanguages.NormalizePreference("fr"));
        }

        [TestMethod]
        public void LocalizationService_UsesExplicitResourceCultureWithoutChangingFormatCulture()
        {
            var originalUiCulture = CultureInfo.CurrentUICulture;
            var originalCulture = CultureInfo.CurrentCulture;
            try
            {
                LocalizationService.SetLanguage("en");
                Assert.AreEqual("System Config", LocalizationService.Get("Settings.SysConfig.Text"));
                Assert.AreEqual("System Config", LocalizationService.Get("Shell.Ribbon.SystemSettings"));
                Assert.AreEqual("Parameter Settings", LocalizationService.Get("Settings.Config.Title"));
                Assert.AreEqual("Models", LocalizationService.Get("Settings.Config.Tab.Models"));
                Assert.AreEqual("Disabled", LocalizationService.Get("Settings.Config.General.TranscriptMode.Disabled"));
                Assert.AreEqual("Excel - Context", LocalizationService.Get("Settings.Config.Tools.Group.ExcelContext"));
                Assert.AreEqual("Delete model \"Model A\"?", LocalizationService.Format("Settings.Config.Models.ConfirmDelete", "Model A"));
                Assert.AreEqual("Add Model", LocalizationService.Get("Settings.Config.ModelEditor.Title.Add"));
                Assert.AreEqual("Connection failed: timeout", LocalizationService.Format("Settings.Config.ModelEditor.ConnectionTestFailed", "timeout"));
                Assert.AreEqual("MCP Config", LocalizationService.Get("Settings.Config.DialogTitle.Mcp"));
                Assert.AreEqual("Delete MCP server \"Server A\"?", LocalizationService.Format("Settings.Config.Mcp.ConfirmDelete", "Server A"));
                Assert.AreEqual("Language", LocalizationService.Get("Settings.SysConfig.grpLanguage.Text"));
                Assert.AreEqual("Model Config", LocalizationService.Get("Shell.Ribbon.ModelConfig"));
                Assert.AreEqual("MCP Config", LocalizationService.Get("Shell.Ribbon.McpConfig"));
                Assert.AreEqual("Skills Config", LocalizationService.Get("Shell.Ribbon.SkillsConfig"));
                Assert.AreEqual("Memory Config", LocalizationService.Get("Shell.Ribbon.MemoryConfig"));
                Assert.AreEqual("About Nixer Excel Copilot", LocalizationService.Format("Shell.About.Title", "Nixer Excel Copilot"));
                Assert.AreEqual("Operation failed:\r\n\r\nDetails", LocalizationService.Format("Shell.Error.OperationFailed", "Details"));
                Assert.AreEqual("en", CultureInfo.CurrentUICulture.Name);
                Assert.AreEqual(originalCulture, CultureInfo.CurrentCulture);

                LocalizationService.SetLanguage("zh-Hans");
                Assert.AreEqual("系统配置", LocalizationService.Get("Settings.SysConfig.Text"));
                Assert.AreEqual("系统配置", LocalizationService.Get("Shell.Ribbon.SystemSettings"));
                Assert.AreEqual("参数设置", LocalizationService.Get("Settings.Config.Title"));
                Assert.AreEqual("模型设置", LocalizationService.Get("Settings.Config.Tab.Models"));
                Assert.AreEqual("禁用", LocalizationService.Get("Settings.Config.General.TranscriptMode.Disabled"));
                Assert.AreEqual("Excel - 上下文", LocalizationService.Get("Settings.Config.Tools.Group.ExcelContext"));
                Assert.AreEqual("确定要删除模型“模型 A”吗？", LocalizationService.Format("Settings.Config.Models.ConfirmDelete", "模型 A"));
                Assert.AreEqual("新增模型", LocalizationService.Get("Settings.Config.ModelEditor.Title.Add"));
                Assert.AreEqual("连接失败：timeout", LocalizationService.Format("Settings.Config.ModelEditor.ConnectionTestFailed", "timeout"));
                Assert.AreEqual("MCP 配置", LocalizationService.Get("Settings.Config.DialogTitle.Mcp"));
                Assert.AreEqual("确定要删除 MCP 服务“Server A”吗？", LocalizationService.Format("Settings.Config.Mcp.ConfirmDelete", "Server A"));
                Assert.AreEqual("语言", LocalizationService.Get("Settings.SysConfig.grpLanguage.Text"));
                Assert.AreEqual("模型配置", LocalizationService.Get("Shell.Ribbon.ModelConfig"));
                Assert.AreEqual("MCP 配置", LocalizationService.Get("Shell.Ribbon.McpConfig"));
                Assert.AreEqual("Skills 配置", LocalizationService.Get("Shell.Ribbon.SkillsConfig"));
                Assert.AreEqual("记忆配置", LocalizationService.Get("Shell.Ribbon.MemoryConfig"));
                Assert.AreEqual("关于 Nixer Excel Copilot", LocalizationService.Format("Shell.About.Title", "Nixer Excel Copilot"));
                Assert.AreEqual("操作失败：\r\n\r\nDetails", LocalizationService.Format("Shell.Error.OperationFailed", "Details"));
                Assert.AreEqual("zh-Hans", CultureInfo.CurrentUICulture.Name);
                Assert.AreEqual(originalCulture, CultureInfo.CurrentCulture);

                LocalizationService.SetLanguage("de");
                Assert.AreEqual("Copilot", LocalizationService.Get("Shell.Ribbon.TogglePane"));
                Assert.AreEqual("Modelle", LocalizationService.Get("Shell.Ribbon.ModelConfig"));
                Assert.AreEqual("MCP", LocalizationService.Get("Shell.Ribbon.McpConfig"));
                Assert.AreEqual("Skills", LocalizationService.Get("Shell.Ribbon.SkillsConfig"));
                Assert.AreEqual("Speicher", LocalizationService.Get("Shell.Ribbon.MemoryConfig"));
                Assert.AreEqual("Tools", LocalizationService.Get("Shell.Ribbon.BuiltinToolsConfig"));
                Assert.AreEqual("Einstellungen", LocalizationService.Get("Shell.Ribbon.SystemSettings"));
                Assert.AreEqual("Info", LocalizationService.Get("Shell.Ribbon.About"));
                Assert.AreEqual("de", CultureInfo.CurrentUICulture.Name);
                Assert.AreEqual(originalCulture, CultureInfo.CurrentCulture);

                LocalizationService.SetLanguage("es");
                Assert.AreEqual("Panel", LocalizationService.Get("Shell.Ribbon.TogglePane"));
                Assert.AreEqual("Modelos", LocalizationService.Get("Shell.Ribbon.ModelConfig"));
                Assert.AreEqual("MCP", LocalizationService.Get("Shell.Ribbon.McpConfig"));
                Assert.AreEqual("Habilidades", LocalizationService.Get("Shell.Ribbon.SkillsConfig"));
                Assert.AreEqual("Memoria", LocalizationService.Get("Shell.Ribbon.MemoryConfig"));
                Assert.AreEqual("Herramientas", LocalizationService.Get("Shell.Ribbon.BuiltinToolsConfig"));
                Assert.AreEqual("Ajustes", LocalizationService.Get("Shell.Ribbon.SystemSettings"));
                Assert.AreEqual("Info", LocalizationService.Get("Shell.Ribbon.About"));
                Assert.AreEqual("es", CultureInfo.CurrentUICulture.Name);
                Assert.AreEqual(originalCulture, CultureInfo.CurrentCulture);

                LocalizationService.SetLanguage("en");
                Assert.AreEqual("en", LocalizationService.Culture.Name);
                Assert.AreEqual("Language", LocalizationService.Get("Settings.SysConfig.grpLanguage.Text"));
                Assert.AreEqual(originalCulture, CultureInfo.CurrentCulture);
            }
            finally
            {
                LocalizationService.SetLanguage("en");
                Thread.CurrentThread.CurrentUICulture = originalUiCulture;
            }
        }

        [TestMethod]
        public void LocalizationService_UsesJapaneseResources()
        {
            var originalUiCulture = Thread.CurrentThread.CurrentUICulture;
            var originalCulture = Thread.CurrentThread.CurrentCulture;
            try
            {
                LocalizationService.SetLanguage("ja");

                Assert.AreEqual("日本語", LocalizationService.Get("Language.Japanese"));
                Assert.AreEqual("システム設定", LocalizationService.Get("Settings.SysConfig.Text"));
                Assert.AreEqual("モデル", LocalizationService.Get("Settings.Config.Tab.Models"));
                Assert.AreEqual("エージェント", LocalizationService.Get("Web.panel.agent"));
                Assert.AreEqual("質問", LocalizationService.Get("Web.panel.ask"));
                Assert.AreEqual("ja", Thread.CurrentThread.CurrentUICulture.Name);
                Assert.AreEqual(originalCulture, Thread.CurrentThread.CurrentCulture);
            }
            finally
            {
                LocalizationService.SetLanguage("en");
                Thread.CurrentThread.CurrentUICulture = originalUiCulture;
            }
        }

        [TestMethod]
        public void LocalizationService_GetAllReturnsFullKeysAndOverlaysCurrentCulture()
        {
            var originalUiCulture = Thread.CurrentThread.CurrentUICulture;
            try
            {
                LocalizationService.SetLanguage("en");
                var english = LocalizationService.GetAll("Web.");
                Assert.IsTrue(english.Count > 0);
                Assert.AreEqual("Copy", english["Web.action.copy"]);
                Assert.IsTrue(english.ContainsKey("Web.action.retry"));

                LocalizationService.SetLanguage("zh-Hans");
                var chinese = LocalizationService.GetAll("Web.");
                Assert.AreEqual(english.Count, chinese.Count);
                Assert.AreEqual("复制", chinese["Web.action.copy"]);
                Assert.IsTrue(chinese.ContainsKey("Web.action.retry"));

                LocalizationService.SetLanguage("ja");
                var japanese = LocalizationService.GetAll("Web.");
                Assert.AreEqual(english.Count, japanese.Count);
                Assert.AreNotEqual(english["Web.action.copy"], japanese["Web.action.copy"]);
                Assert.AreNotEqual(english["Web.action.retry"], japanese["Web.action.retry"]);
            }
            finally
            {
                LocalizationService.SetLanguage("en");
                Thread.CurrentThread.CurrentUICulture = originalUiCulture;
            }
        }

        [TestMethod]
        public void LocalizationService_UsesGermanAndSpanishResources()
        {
            var originalUiCulture = Thread.CurrentThread.CurrentUICulture;
            try
            {
                LocalizationService.SetLanguage("de");
                Assert.AreEqual("Deutsch", LocalizationService.Get("Language.German"));
                Assert.AreNotEqual("System Config", LocalizationService.Get("Settings.SysConfig.Text"));
                Assert.AreEqual("de", Thread.CurrentThread.CurrentUICulture.Name);

                LocalizationService.SetLanguage("es");
                Assert.AreEqual("Español", LocalizationService.Get("Language.Spanish"));
                Assert.AreNotEqual("System Config", LocalizationService.Get("Settings.SysConfig.Text"));
                Assert.AreEqual("es", Thread.CurrentThread.CurrentUICulture.Name);
            }
            finally
            {
                LocalizationService.SetLanguage("en");
                Thread.CurrentThread.CurrentUICulture = originalUiCulture;
            }
        }

        [TestMethod]
        public void SupportedLanguageSatellitesContainAllNeutralResourceKeys()
        {
            var resources = new ResourceManager(
                "OfficeCopilot.Localization.Strings",
                typeof(LocalizationService).Assembly);
            var english = resources.GetResourceSet(CultureInfo.InvariantCulture, true, true);
            Assert.IsNotNull(english);
            var englishKeys = GetResourceKeys(english);

            foreach (var language in new[] { "zh-Hans", "ja", "de", "es" })
            {
                var localized = resources.GetResourceSet(
                    SupportedLanguages.ResolveCulture(language),
                    true,
                    false);
                Assert.IsNotNull(localized, language + " satellite resources must be built.");
                CollectionAssert.AreEquivalent(
                    englishKeys,
                    GetResourceKeys(localized),
                    language + " must contain every neutral resource key.");
            }
        }

        [TestMethod]
        public void ToolDisplayNames_AreEnglishAcrossAllSupportedLanguages()
        {
            var resources = new ResourceManager(
                "OfficeCopilot.Localization.Strings",
                typeof(LocalizationService).Assembly);
            var english = resources.GetResourceSet(CultureInfo.InvariantCulture, true, true);
            Assert.IsNotNull(english);

            var toolDisplayNames = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (DictionaryEntry entry in english)
            {
                var key = (string)entry.Key;
                if (key.StartsWith("tool.", StringComparison.Ordinal)
                    && key.EndsWith(".display_name", StringComparison.Ordinal))
                {
                    toolDisplayNames.Add(key, (string)entry.Value);
                }
            }

            Assert.IsTrue(toolDisplayNames.Count > 0, "Neutral resources must define built-in tool display names.");
            foreach (var language in new[] { "zh-Hans", "ja", "de", "es" })
            {
                var localized = resources.GetResourceSet(
                    SupportedLanguages.ResolveCulture(language),
                    true,
                    false);
                Assert.IsNotNull(localized, language + " satellite resources must be built.");

                foreach (var displayName in toolDisplayNames)
                {
                    Assert.AreEqual(
                        displayName.Value,
                        localized.GetString(displayName.Key, false),
                        language + "/" + displayName.Key + " must remain English.");
                }
            }
        }

        [TestMethod]
        public void BuiltInToolMetadata_UsesEnglishForExcelWordAndPowerPoint()
        {
            var hostDefinitions = new[]
            {
                new { Host = "Excel", Definitions = ExcelBuiltInToolCatalog.CreateAllAvailableDefinitions() },
                new { Host = "Word", Definitions = WordBuiltInToolCatalog.CreateAllAvailableDefinitions() },
                new { Host = "PowerPoint", Definitions = PowerPointBuiltInToolCatalog.CreateAllAvailableDefinitions() }
            };

            foreach (var host in hostDefinitions)
            {
                Assert.IsTrue(host.Definitions.Length > 0, host.Host + " must expose built-in tool definitions.");
                foreach (var definition in host.Definitions)
                {
                    Assert.IsFalse(string.IsNullOrWhiteSpace(definition.Name), host.Host + " tool names must not be empty.");
                    Assert.IsFalse(string.IsNullOrWhiteSpace(definition.DisplayName), host.Host + "/" + definition.Name + " display name must not be empty.");
                    AssertHasNoCjkCharacters(definition.Name, host.Host + "/" + definition.Name + " tool name");
                    AssertHasNoCjkCharacters(definition.DisplayName, host.Host + "/" + definition.Name + " display name");
                    AssertHasNoCjkCharacters(definition.Description, host.Host + "/" + definition.Name + " description");

                    Assert.IsFalse(
                        string.IsNullOrWhiteSpace(definition.InputSchemaJson),
                        host.Host + "/" + definition.Name + " input schema must not be empty.");
                    using (var schema = JsonDocument.Parse(definition.InputSchemaJson))
                    {
                        AssertSchemaDescriptionsAreEnglish(
                            schema.RootElement,
                            host.Host + "/" + definition.Name);
                    }
                }
            }
        }

        [TestMethod]
        public void SupportedLanguageResources_PreserveCompositeFormatPlaceholders()
        {
            var resources = new ResourceManager(
                "OfficeCopilot.Localization.Strings",
                typeof(LocalizationService).Assembly);

            foreach (var language in new[] { "zh-Hans", "ja", "de", "es" })
            {
                var culture = SupportedLanguages.ResolveCulture(language);
                var localized = resources.GetResourceSet(culture, true, false);
                Assert.IsNotNull(localized, language + " satellite resources must be built.");

                foreach (DictionaryEntry entry in localized)
                {
                    var key = (string)entry.Key;
                    var source = resources.GetString(key, CultureInfo.InvariantCulture) ?? string.Empty;
                    var translated = (string)entry.Value;
                    CollectionAssert.AreEquivalent(
                        GetCompositeFormatItems(source),
                        GetCompositeFormatItems(translated),
                        language + "/" + key + " must preserve format placeholders.");
                }
            }
        }

        private static ArrayList GetResourceKeys(ResourceSet resourceSet)
        {
            var keys = new ArrayList();
            foreach (DictionaryEntry entry in resourceSet)
            {
                keys.Add(entry.Key);
            }

            return keys;
        }

        private static ArrayList GetCompositeFormatItems(string value)
        {
            var placeholders = new ArrayList();
            foreach (Match match in Regex.Matches(
                value ?? string.Empty,
                @"(?<!\{)\{(\d+)(?:,[^{}]*)?(?::[^{}]*)?\}(?!\})"))
            {
                placeholders.Add(match.Value);
            }

            return placeholders;
        }

        [TestMethod]
        public void ResponseLanguagePolicy_UsesSelectedUiLanguageUnlessTheUserRequestsAnotherLanguage()
        {
            var englishInstruction = ResponseLanguagePolicy.BuildInstruction("en");
            StringAssert.Contains(englishInstruction, "By default, respond in English");
            StringAssert.Contains(englishInstruction, "Use another language only when the user explicitly requests it");

            var chineseInstruction = ResponseLanguagePolicy.BuildInstruction("zh-Hans");
            StringAssert.Contains(chineseInstruction, "默认使用简体中文回答");
            StringAssert.Contains(chineseInstruction, "只有用户明确要求其他回复语言时");

            var japaneseInstruction = ResponseLanguagePolicy.BuildInstruction("ja");
            StringAssert.Contains(japaneseInstruction, "デフォルトでは日本語で回答してください");
            StringAssert.Contains(japaneseInstruction, "別の回答言語を明示的に指定");

            var germanInstruction = ResponseLanguagePolicy.BuildInstruction("de");
            StringAssert.Contains(germanInstruction, "Antworten Sie standardmäßig auf Deutsch");
            StringAssert.Contains(germanInstruction, "wenn der Benutzer dies ausdrücklich verlangt");

            var spanishInstruction = ResponseLanguagePolicy.BuildInstruction("es");
            StringAssert.Contains(spanishInstruction, "Responde en español de forma predeterminada");
            StringAssert.Contains(spanishInstruction, "el usuario lo solicita explícitamente");

            Assert.AreEqual(englishInstruction, ResponseLanguagePolicy.BuildInstruction("unsupported"));
        }

        [TestMethod]
        public void CombinedSystemPrompt_StartsWithTheSelectedLanguageInstruction()
        {
            var request = new AgentBrokerTurnRequest
            {
                UiCulture = "zh-Hans",
                ModelOptions = new AgentModelOptions
                {
                    SystemPrompt = "Custom host instructions."
                }
            };

            var prompt = DefaultContextBuilder.BuildCombinedModelSystemPrompt(request, null, null);

            StringAssert.StartsWith(
                prompt,
                ResponseLanguagePolicy.BuildInstruction("zh-Hans"));
            Assert.IsTrue(
                prompt.IndexOf("Custom host instructions.", StringComparison.Ordinal) > 0,
                "The selected-language instruction must precede the configured system prompt.");

            var englishPrompt = DefaultContextBuilder.BuildCombinedModelSystemPrompt(
                new AgentBrokerTurnRequest
                {
                    UiCulture = "en",
                    InteractiveMode = "agent",
                    ModelOptions = new AgentModelOptions { SystemPrompt = "Custom host instructions." }
                },
                new HostContextSnapshotDto
                {
                    HostApplicationName = "Excel",
                    DocumentName = "Report.xlsx",
                    Summary = "Workbook summary",
                    CurrentLocalTime = "2026-10-01 10:00 UTC"
                },
                null);
            AssertHasNoCjkCharacters(englishPrompt, "English combined system prompt");
        }

        [TestMethod]
        public void BuiltInHostPrompts_UseEnglishForInternalInstructions()
        {
            var hosts = new[] { "Excel", "Word", "PowerPoint" };
            var modes = new[] { HostPromptMode.Ask, HostPromptMode.Agent };

            foreach (var host in hosts)
            {
                foreach (var mode in modes)
                {
                    var prompt = BuiltInHostPrompts.Get(host, mode);
                    AssertHasNoFixedChineseResponseLanguage(prompt, $"{host}/{mode} built-in prompt");
                    AssertHasNoCjkCharacters(prompt, $"{host}/{mode} built-in prompt");
                }
            }

            var defaultRuntimePrompt = DefaultContextBuilder.BuildModelSystemPrompt(null);
            StringAssert.Contains(defaultRuntimePrompt, "You are an Office assistant");
            AssertHasNoFixedChineseResponseLanguage(defaultRuntimePrompt, "default runtime prompt");
            AssertHasNoCjkCharacters(defaultRuntimePrompt, "default runtime prompt");
            AssertHasNoFixedChineseResponseLanguage(
                LoopGuardPolicy.BuildSystemPrompt(null),
                "loop guard prompt");
            AssertHasNoCjkCharacters(LoopGuardPolicy.BuildSystemPrompt(null), "loop guard prompt");
            AssertHasNoFixedChineseResponseLanguage(
                SkillPlanSupport.BuildVerificationRetryLimitSystemPrompt(null, null, null),
                "verification retry prompt");
            AssertHasNoCjkCharacters(
                SkillPlanSupport.BuildVerificationRetryLimitSystemPrompt(null, null, null),
                "verification retry prompt");
        }

        private static void AssertHasNoCjkCharacters(string prompt, string promptName)
        {
            foreach (var character in prompt)
            {
                Assert.IsFalse(
                    (character >= '\u3400' && character <= '\u4dbf')
                        || (character >= '\u4e00' && character <= '\u9fff')
                        || (character >= '\u3000' && character <= '\u303f')
                        || (character >= '\u3040' && character <= '\u30ff')
                        || (character >= '\u31f0' && character <= '\u31ff')
                        || (character >= '\uac00' && character <= '\ud7af')
                        || (character >= '\uf900' && character <= '\ufaff'),
                    $"{promptName} must use English text.");
            }
        }

        private static void AssertSchemaDescriptionsAreEnglish(JsonElement element, string toolName)
        {
            if (element.ValueKind == JsonValueKind.Object)
            {
                foreach (var property in element.EnumerateObject())
                {
                    if (string.Equals(property.Name, "description", StringComparison.OrdinalIgnoreCase)
                        && property.Value.ValueKind == JsonValueKind.String)
                    {
                        AssertHasNoCjkCharacters(
                            property.Value.GetString(),
                            toolName + " schema description");
                    }

                    AssertSchemaDescriptionsAreEnglish(property.Value, toolName);
                }
            }
            else if (element.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in element.EnumerateArray())
                {
                    AssertSchemaDescriptionsAreEnglish(item, toolName);
                }
            }
        }

        private static void AssertHasNoFixedChineseResponseLanguage(string prompt, string promptName)
        {
            Assert.IsFalse(
                prompt.IndexOf("Chinese", StringComparison.OrdinalIgnoreCase) >= 0
                    || prompt.Contains("中文"),
                $"{promptName} must not force Chinese responses.");
        }

        [TestMethod]
        public void ApplyUiCultureToCurrentThread_ChangesOnlyUiCulture()
        {
            var originalUiCulture = Thread.CurrentThread.CurrentUICulture;
            var originalCulture = Thread.CurrentThread.CurrentCulture;
            try
            {
                var appliedCulture = LocalizationService.ApplyUiCultureToCurrentThread("zh-Hans");

                Assert.AreEqual("zh-Hans", appliedCulture.Name);
                Assert.AreEqual("zh-Hans", Thread.CurrentThread.CurrentUICulture.Name);
                Assert.AreEqual(originalCulture, Thread.CurrentThread.CurrentCulture);
            }
            finally
            {
                Thread.CurrentThread.CurrentUICulture = originalUiCulture;
                LocalizationService.SetLanguage("en");
                Thread.CurrentThread.CurrentUICulture = originalUiCulture;
            }
        }

        [TestMethod]
        public void LanguageChanged_IsRaisedAfterApplyingCurrentUiCulture()
        {
            var originalUiCulture = Thread.CurrentThread.CurrentUICulture;
            string cultureAtNotification = null;
            EventHandler handler = (sender, args) => cultureAtNotification = Thread.CurrentThread.CurrentUICulture.Name;
            LocalizationService.LanguageChanged += handler;
            try
            {
                LocalizationService.SetLanguage("en");
                LocalizationService.SetLanguage("zh-Hans");

                Assert.AreEqual("zh-Hans", cultureAtNotification);
            }
            finally
            {
                LocalizationService.LanguageChanged -= handler;
                LocalizationService.SetLanguage("en");
                Thread.CurrentThread.CurrentUICulture = originalUiCulture;
            }
        }

        [TestMethod]
        public void NormalizeSettings_DefaultsLanguageForOldAndInvalidConfigurations()
        {
            var oldSettings = new OfficeCopilotSettings
            {
                ConfigurationVersion = 7,
                UiLanguage = null
            };
            Assert.AreEqual("en", OfficeCopilotSettingsFactory.Normalize(oldSettings).UiLanguage);
            Assert.AreEqual(8, oldSettings.ConfigurationVersion);

            var autoSettings = new OfficeCopilotSettings { UiLanguage = "auto" };
            Assert.AreEqual("en", OfficeCopilotSettingsFactory.Normalize(autoSettings).UiLanguage);

            var japaneseSettings = new OfficeCopilotSettings { UiLanguage = "ja-JP" };
            Assert.AreEqual("ja", OfficeCopilotSettingsFactory.Normalize(japaneseSettings).UiLanguage);

            var germanSettings = new OfficeCopilotSettings { UiLanguage = "de-DE" };
            Assert.AreEqual("de", OfficeCopilotSettingsFactory.Normalize(germanSettings).UiLanguage);

            var spanishSettings = new OfficeCopilotSettings { UiLanguage = "es-ES" };
            Assert.AreEqual("es", OfficeCopilotSettingsFactory.Normalize(spanishSettings).UiLanguage);

            var invalidSettings = new OfficeCopilotSettings { UiLanguage = "unsupported" };
            Assert.AreEqual("en", OfficeCopilotSettingsFactory.Normalize(invalidSettings).UiLanguage);
        }
    }
}
