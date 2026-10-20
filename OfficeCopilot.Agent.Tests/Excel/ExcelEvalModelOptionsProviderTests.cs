using Microsoft.VisualStudio.TestTools.UnitTesting;
using OfficeCopilot.Agent.Client;
using OfficeCopilot.Eval.Excel;
using OfficeCopilot.Settings.Abstractions;
using System;
using System.Collections.Generic;

namespace OfficeCopilot.Agent.Tests.Excel
{
    [TestClass]
    public sealed class ExcelEvalModelOptionsProviderTests
    {
        [TestMethod]
        public void CreateUsesDefaultEnabledProviderAndBuildsAskModelOptions()
        {
            var settings = new OfficeCopilotSettings
            {
                DefaultModelProviderId = 20
            };
            settings.ChatDefaults.RequestTimeoutSeconds = 75;

            var fallback = CreateModel(10, true, "https://fallback.example", "fallback-model");
            var selected = CreateModel(20, true, "https://selected.example", "selected-model");
            selected.ProviderKind = ModelProviderKind.Anthropic;
            selected.ApiKey = "test-key";
            selected.SystemPrompt = "Model instructions";
            selected.SupportsImageInput = true;
            selected.HttpHeaders["X-Test"] = "header";
            selected.AdditionalParameters["temperature"] = 0.2;
            settings.ModelProviders.Add(fallback);
            settings.ModelProviders.Add(selected);

            var configuration = ExcelEvalModelOptionsProvider.Create(settings, "Excel ask prompt");

            Assert.AreEqual(20, configuration.ModelProviderId);
            Assert.AreEqual("Anthropic", configuration.ModelOptions.ProviderKind);
            Assert.AreEqual("https://selected.example", configuration.ModelOptions.Endpoint);
            Assert.AreEqual("test-key", configuration.ModelOptions.ApiKey);
            Assert.AreEqual("selected-model", configuration.ModelOptions.ModelId);
            Assert.AreEqual(75, configuration.ModelOptions.AdditionalParameters[
                AgentModelAdditionalParameterNames.RequestTimeoutSeconds]);
            Assert.AreEqual(true, configuration.ModelOptions.AdditionalParameters[
                AgentModelAdditionalParameterNames.SupportsImageInput]);
            Assert.AreEqual(false, configuration.ModelOptions.AdditionalParameters[
                AgentModelAdditionalParameterNames.ModelTranscriptEnabled]);
            Assert.AreEqual("header", configuration.ModelOptions.HttpHeaders["X-Test"]);
            StringAssert.Contains(configuration.ModelOptions.SystemPrompt, "Excel ask prompt");
            StringAssert.Contains(configuration.ModelOptions.SystemPrompt, "Model instructions");
            Assert.IsFalse(selected.AdditionalParameters.ContainsKey(
                AgentModelAdditionalParameterNames.RequestTimeoutSeconds));
        }

        [TestMethod]
        public void CreateFallsBackToLowestEnabledProviderWhenDefaultIsMissingOrDisabled()
        {
            var settings = new OfficeCopilotSettings
            {
                DefaultModelProviderId = 99
            };
            settings.ModelProviders.Add(CreateModel(20, true, "https://second.example", "second-model"));
            settings.ModelProviders.Add(CreateModel(10, true, "https://first.example", "first-model"));
            settings.ModelProviders.Add(CreateModel(1, false, "https://disabled.example", "disabled-model"));

            var configuration = ExcelEvalModelOptionsProvider.Create(settings, "host prompt");

            Assert.AreEqual(10, configuration.ModelProviderId);
            Assert.AreEqual("first-model", configuration.ModelOptions.ModelId);
        }

        [TestMethod]
        public void CreateRejectsMissingEnabledProviderOrEndpoint()
        {
            var noModels = new OfficeCopilotSettings();
            var noModelError = Assert.ThrowsException<InvalidOperationException>(
                () => ExcelEvalModelOptionsProvider.Create(noModels, string.Empty));
            StringAssert.Contains(noModelError.Message, "No enabled model provider");

            var missingEndpoint = new OfficeCopilotSettings();
            missingEndpoint.ModelProviders.Add(CreateModel(1, true, string.Empty, "model"));
            var endpointError = Assert.ThrowsException<InvalidOperationException>(
                () => ExcelEvalModelOptionsProvider.Create(missingEndpoint, string.Empty));
            StringAssert.Contains(endpointError.Message, "no endpoint configured");
        }

        private static ModelProviderSettings CreateModel(
            int id,
            bool enabled,
            string endpoint,
            string modelId)
        {
            return new ModelProviderSettings
            {
                Id = id,
                IsEnabled = enabled,
                ProviderKind = ModelProviderKind.Custom,
                Endpoint = endpoint,
                ModelId = modelId,
                MaxContextTokens = 64000,
                ApiKey = string.Empty,
                AdditionalParameters = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase),
                HttpHeaders = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            };
        }
    }
}
