using System;
using System.Globalization;
using System.Threading;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OfficeCopilot.Agent.Runtime.Turn;
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
            Assert.AreEqual("en", SupportedLanguages.ResolveCulture("en").Name);
            Assert.AreEqual("en", SupportedLanguages.ResolveCulture("auto").Name);
            Assert.AreEqual("en", SupportedLanguages.ResolveCulture("unsupported").Name);
        }

        [TestMethod]
        public void NormalizePreference_OnlyAllowsRegisteredLanguageCodes()
        {
            Assert.AreEqual("en", SupportedLanguages.NormalizePreference(null));
            Assert.AreEqual("zh-Hans", SupportedLanguages.NormalizePreference("zh-CN"));
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
            }
            finally
            {
                LocalizationService.SetLanguage("en");
                Thread.CurrentThread.CurrentUICulture = originalUiCulture;
            }
        }

        [TestMethod]
        public void ResponseLanguagePolicy_UsesUiLanguageOnlyAsTheAmbiguousLanguageFallback()
        {
            var englishInstruction = ResponseLanguagePolicy.BuildInstruction("en");
            StringAssert.Contains(englishInstruction, "Follow any explicit response-language request");
            StringAssert.Contains(englishInstruction, "same language as the user's latest message");
            StringAssert.Contains(englishInstruction, "reply in English");

            var chineseInstruction = ResponseLanguagePolicy.BuildInstruction("zh-Hans");
            StringAssert.Contains(chineseInstruction, "same language as the user's latest message");
            StringAssert.Contains(chineseInstruction, "reply in Simplified Chinese");

            StringAssert.Contains(ResponseLanguagePolicy.BuildInstruction("unsupported"), "reply in English");
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

            var invalidSettings = new OfficeCopilotSettings { UiLanguage = "unsupported" };
            Assert.AreEqual("en", OfficeCopilotSettingsFactory.Normalize(invalidSettings).UiLanguage);
        }
    }
}
