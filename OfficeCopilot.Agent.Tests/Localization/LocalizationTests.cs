using System;
using System.Collections;
using System.Globalization;
using System.Resources;
using System.Threading;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OfficeCopilot.Agent.Client;
using OfficeCopilot.Agent.Runtime.Turn;
using OfficeCopilot.HostBridge.Contracts;
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
        public void JapaneseSatelliteContainsAllNeutralResourceKeys()
        {
            var resources = new ResourceManager(
                "OfficeCopilot.Localization.Strings",
                typeof(LocalizationService).Assembly);
            var english = resources.GetResourceSet(CultureInfo.InvariantCulture, true, true);
            var japanese = resources.GetResourceSet(SupportedLanguages.ResolveCulture("ja"), true, false);

            Assert.IsNotNull(english);
            Assert.IsNotNull(japanese);

            var englishKeys = GetResourceKeys(english);
            var japaneseKeys = GetResourceKeys(japanese);
            CollectionAssert.AreEquivalent(englishKeys, japaneseKeys);
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
                    character >= '\u4e00' && character <= '\u9fff',
                    $"{promptName} must keep internal instructions in English.");
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

            var invalidSettings = new OfficeCopilotSettings { UiLanguage = "unsupported" };
            Assert.AreEqual("en", OfficeCopilotSettingsFactory.Normalize(invalidSettings).UiLanguage);
        }
    }
}
