using OfficeCopilot.Agent.Client;
using OfficeCopilot.Settings;
using OfficeCopilot.Settings.Abstractions;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace OfficeCopilot.Eval.Excel
{
    internal sealed class ExcelEvalModelConfiguration
    {
        public ExcelEvalModelConfiguration(int modelProviderId, AgentModelOptions modelOptions)
        {
            ModelProviderId = modelProviderId;
            ModelOptions = modelOptions ?? throw new ArgumentNullException(nameof(modelOptions));
        }

        public int ModelProviderId { get; private set; }

        public AgentModelOptions ModelOptions { get; private set; }
    }

    internal static class ExcelEvalModelOptionsProvider
    {
        public static ExcelEvalModelConfiguration LoadDefault()
        {
            var settings = new JsonSettingsStore().Load();
            var hostPrompt = HostSystemPromptStore.GetEffectivePrompt(
                HostApplicationNames.Excel,
                HostPromptMode.Ask);
            return Create(settings, hostPrompt);
        }

        internal static ExcelEvalModelConfiguration Create(
            OfficeCopilotSettings settings,
            string hostPrompt)
        {
            if (settings == null)
            {
                throw new ArgumentNullException(nameof(settings));
            }

            var enabledModels = (settings.ModelProviders ?? new List<ModelProviderSettings>())
                .Where(model => model != null && model.IsEnabled)
                .OrderBy(model => model.Id)
                .ToList();
            if (enabledModels.Count == 0)
            {
                throw new InvalidOperationException(
                    "No enabled model provider is configured in "
                    + OfficeCopilotStoragePaths.GetSettingsFilePath()
                    + ". Enable a model provider before running Excel Eval.");
            }

            var model = settings.DefaultModelProviderId.HasValue
                ? enabledModels.FirstOrDefault(candidate => candidate.Id == settings.DefaultModelProviderId.Value)
                : null;
            if (model == null)
            {
                model = enabledModels[0];
            }

            if (string.IsNullOrWhiteSpace(model.Endpoint))
            {
                throw new InvalidOperationException(
                    "The selected model provider " + model.Id.ToString()
                    + " has no endpoint configured. Update the model in Copilot settings before running Excel Eval.");
            }

            if (string.IsNullOrWhiteSpace(model.ModelId))
            {
                throw new InvalidOperationException(
                    "The selected model provider " + model.Id.ToString()
                    + " has no model ID configured. Update the model in Copilot settings before running Excel Eval.");
            }

            var additionalParameters = model.AdditionalParameters == null
                ? new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, object>(model.AdditionalParameters, StringComparer.OrdinalIgnoreCase);

            var requestTimeoutSeconds = settings.ChatDefaults == null
                ? 60
                : Math.Max(1, settings.ChatDefaults.RequestTimeoutSeconds);
            if (!additionalParameters.ContainsKey(AgentModelAdditionalParameterNames.RequestTimeoutSeconds)
                && !additionalParameters.ContainsKey("request_timeout_seconds")
                && !additionalParameters.ContainsKey("requestTimeoutSeconds"))
            {
                additionalParameters[AgentModelAdditionalParameterNames.RequestTimeoutSeconds] =
                    requestTimeoutSeconds;
            }

            additionalParameters[AgentModelAdditionalParameterNames.SupportsImageInput] =
                model.SupportsImageInput;
            ApplyTranscriptOptions(settings, additionalParameters);

            var effectiveSystemPrompt = hostPrompt ?? string.Empty;
            var modelPrompt = model.SystemPrompt;
            if (!string.IsNullOrWhiteSpace(modelPrompt))
            {
                effectiveSystemPrompt = string.IsNullOrWhiteSpace(effectiveSystemPrompt)
                    ? modelPrompt.Trim()
                    : effectiveSystemPrompt + Environment.NewLine + Environment.NewLine + modelPrompt.Trim();
            }

            return new ExcelEvalModelConfiguration(
                model.Id,
                new AgentModelOptions
                {
                    ProviderKind = model.ProviderKind.ToString(),
                    Endpoint = model.Endpoint,
                    ApiKey = model.ApiKey ?? string.Empty,
                    ModelId = model.ModelId,
                    MaxContextTokens = model.MaxContextTokens,
                    HttpHeaders = model.HttpHeaders == null
                        ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                        : new Dictionary<string, string>(model.HttpHeaders, StringComparer.OrdinalIgnoreCase),
                    AdditionalParameters = additionalParameters,
                    SystemPrompt = effectiveSystemPrompt
                });
        }

        private static void ApplyTranscriptOptions(
            OfficeCopilotSettings settings,
            IDictionary<string, object> additionalParameters)
        {
            var transcriptSettings = settings.Logging == null
                ? null
                : settings.Logging.ModelTranscripts;
            if (transcriptSettings == null
                || !transcriptSettings.IsEnabled
                || transcriptSettings.Mode == ModelTranscriptLogMode.Disabled)
            {
                additionalParameters[AgentModelAdditionalParameterNames.ModelTranscriptEnabled] = false;
                return;
            }

            var rootFolder = string.IsNullOrWhiteSpace(transcriptSettings.RootFolder)
                ? Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "NixerOfficeCopilot",
                    "logs")
                : transcriptSettings.RootFolder.Trim();
            var fileName = string.IsNullOrWhiteSpace(transcriptSettings.FileName)
                ? "model-transcript.log"
                : Path.GetFileName(transcriptSettings.FileName.Trim());

            additionalParameters[AgentModelAdditionalParameterNames.ModelTranscriptEnabled] = true;
            additionalParameters[AgentModelAdditionalParameterNames.ModelTranscriptMode] =
                transcriptSettings.Mode.ToString();
            additionalParameters[AgentModelAdditionalParameterNames.ModelTranscriptFilePath] =
                Path.Combine(rootFolder, fileName);
        }
    }
}
