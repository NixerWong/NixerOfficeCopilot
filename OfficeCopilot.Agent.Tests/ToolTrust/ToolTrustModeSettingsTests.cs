using Microsoft.VisualStudio.TestTools.UnitTesting;
using OfficeCopilot.Settings;
using OfficeCopilot.Settings.Abstractions;

namespace OfficeCopilot.Agent.Tests.ToolTrust
{
    [TestClass]
    public sealed class ToolTrustModeSettingsTests
    {
        [TestMethod]
        public void DefaultsToNeedConfirmForEveryHost()
        {
            var settings = OfficeCopilotSettingsFactory.CreateDefault();

            Assert.AreEqual(ToolTrustModeValues.NeedConfirm, OfficeCopilotSettingsFactory.GetToolTrustMode(settings, HostApplicationNames.Excel));
            Assert.AreEqual(ToolTrustModeValues.NeedConfirm, OfficeCopilotSettingsFactory.GetToolTrustMode(settings, HostApplicationNames.Word));
            Assert.AreEqual(ToolTrustModeValues.NeedConfirm, OfficeCopilotSettingsFactory.GetToolTrustMode(settings, HostApplicationNames.PowerPoint));
        }

        [TestMethod]
        public void SetToolTrustMode_StoresValueForOneHostOnly()
        {
            var settings = OfficeCopilotSettingsFactory.CreateDefault();

            OfficeCopilotSettingsFactory.SetToolTrustMode(settings, HostApplicationNames.Excel, ToolTrustModeValues.AlwaysAllow);

            Assert.AreEqual(ToolTrustModeValues.AlwaysAllow, OfficeCopilotSettingsFactory.GetToolTrustMode(settings, HostApplicationNames.Excel));
            Assert.AreEqual(ToolTrustModeValues.NeedConfirm, OfficeCopilotSettingsFactory.GetToolTrustMode(settings, HostApplicationNames.Word));
            Assert.AreEqual(ToolTrustModeValues.NeedConfirm, OfficeCopilotSettingsFactory.GetToolTrustMode(settings, HostApplicationNames.PowerPoint));
        }

        [TestMethod]
        public void SetToolTrustMode_InvalidValueFallsBackToNeedConfirm()
        {
            var settings = OfficeCopilotSettingsFactory.CreateDefault();
            OfficeCopilotSettingsFactory.SetToolTrustMode(settings, HostApplicationNames.Word, ToolTrustModeValues.AlwaysAllow);

            OfficeCopilotSettingsFactory.SetToolTrustMode(settings, HostApplicationNames.Word, "bogus");

            Assert.AreEqual(ToolTrustModeValues.NeedConfirm, OfficeCopilotSettingsFactory.GetToolTrustMode(settings, HostApplicationNames.Word));
        }

        [TestMethod]
        public void Normalize_FillsMissingAndInvalidValuesWithNeedConfirm()
        {
            var settings = OfficeCopilotSettingsFactory.CreateDefault();
            settings.ExcelToolTrustMode = ToolTrustModeValues.AlwaysAllow;
            settings.WordToolTrustMode = "bogus";
            settings.PowerPointToolTrustMode = null;

            OfficeCopilotSettingsFactory.Normalize(settings);

            Assert.AreEqual(ToolTrustModeValues.AlwaysAllow, settings.ExcelToolTrustMode);
            Assert.AreEqual(ToolTrustModeValues.NeedConfirm, settings.WordToolTrustMode);
            Assert.AreEqual(ToolTrustModeValues.NeedConfirm, settings.PowerPointToolTrustMode);
        }

        [TestMethod]
        public void SetToolTrustMode_WritesToIndependentParameterPerHost()
        {
            var settings = OfficeCopilotSettingsFactory.CreateDefault();

            OfficeCopilotSettingsFactory.SetToolTrustMode(settings, HostApplicationNames.Excel, ToolTrustModeValues.AlwaysAllow);
            OfficeCopilotSettingsFactory.SetToolTrustMode(settings, HostApplicationNames.PowerPoint, ToolTrustModeValues.AlwaysAllow);

            Assert.AreEqual(ToolTrustModeValues.AlwaysAllow, settings.ExcelToolTrustMode);
            Assert.AreEqual(ToolTrustModeValues.NeedConfirm, settings.WordToolTrustMode);
            Assert.AreEqual(ToolTrustModeValues.AlwaysAllow, settings.PowerPointToolTrustMode);
        }

        [TestMethod]
        public void GetToolTrustMode_ResolvesHostNameCaseInsensitively()
        {
            var settings = OfficeCopilotSettingsFactory.CreateDefault();
            OfficeCopilotSettingsFactory.SetToolTrustMode(settings, "powerpoint", ToolTrustModeValues.AlwaysAllow);

            Assert.AreEqual(ToolTrustModeValues.AlwaysAllow, OfficeCopilotSettingsFactory.GetToolTrustMode(settings, "PowerPoint"));
        }

        [TestMethod]
        public void RefreshToolTrustModes_KeepsTargetHostAndTakesOthersFromLatest()
        {
            var target = OfficeCopilotSettingsFactory.CreateDefault();
            OfficeCopilotSettingsFactory.SetToolTrustMode(target, HostApplicationNames.Excel, ToolTrustModeValues.AlwaysAllow);
            OfficeCopilotSettingsFactory.SetToolTrustMode(target, HostApplicationNames.Word, ToolTrustModeValues.NeedConfirm);

            var latest = OfficeCopilotSettingsFactory.CreateDefault();
            OfficeCopilotSettingsFactory.SetToolTrustMode(latest, HostApplicationNames.Excel, ToolTrustModeValues.NeedConfirm);
            OfficeCopilotSettingsFactory.SetToolTrustMode(latest, HostApplicationNames.Word, ToolTrustModeValues.AlwaysAllow);
            OfficeCopilotSettingsFactory.SetToolTrustMode(latest, HostApplicationNames.PowerPoint, ToolTrustModeValues.AlwaysAllow);

            OfficeCopilotSettingsFactory.RefreshToolTrustModes(target, latest, HostApplicationNames.Excel);

            Assert.AreEqual(ToolTrustModeValues.AlwaysAllow, OfficeCopilotSettingsFactory.GetToolTrustMode(target, HostApplicationNames.Excel));
            Assert.AreEqual(ToolTrustModeValues.AlwaysAllow, OfficeCopilotSettingsFactory.GetToolTrustMode(target, HostApplicationNames.Word));
            Assert.AreEqual(ToolTrustModeValues.AlwaysAllow, OfficeCopilotSettingsFactory.GetToolTrustMode(target, HostApplicationNames.PowerPoint));
        }

        [TestMethod]
        public void RefreshToolTrustModes_WithoutKeepHostTakesAllFromLatest()
        {
            var target = OfficeCopilotSettingsFactory.CreateDefault();
            OfficeCopilotSettingsFactory.SetToolTrustMode(target, HostApplicationNames.Excel, ToolTrustModeValues.AlwaysAllow);

            var latest = OfficeCopilotSettingsFactory.CreateDefault();
            OfficeCopilotSettingsFactory.SetToolTrustMode(latest, HostApplicationNames.Excel, ToolTrustModeValues.NeedConfirm);

            OfficeCopilotSettingsFactory.RefreshToolTrustModes(target, latest);

            Assert.AreEqual(ToolTrustModeValues.NeedConfirm, OfficeCopilotSettingsFactory.GetToolTrustMode(target, HostApplicationNames.Excel));
        }
    }
}
