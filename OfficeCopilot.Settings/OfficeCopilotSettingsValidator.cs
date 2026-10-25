using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;
using OfficeCopilot.Localization;
using OfficeCopilot.Settings.Abstractions;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace OfficeCopilot.Settings
{
    public sealed class OfficeCopilotSettingsValidator
    {
        public SettingsValidationResult Validate(OfficeCopilotSettings settings)
        {
            var result = new SettingsValidationResult();

            if (settings == null)
            {
                result.Errors.Add(LocalizationService.Get("Settings.Validation.SettingsRequired"));
                return result;
            }

            if (settings.ChatDefaults == null)
            {
                result.Errors.Add(LocalizationService.Get("Settings.Validation.ChatDefaultsMissing"));
            }

            if (settings.Logging == null)
            {
                result.Errors.Add(LocalizationService.Get("Settings.Validation.LoggingMissing"));
            }
            else
            {
                if (settings.Logging.Runtime == null)
                {
                    result.Errors.Add(LocalizationService.Get("Settings.Validation.RuntimeLoggingMissing"));
                }
                else
                {
                    if (settings.Logging.Runtime.EnableFile &&
                        string.IsNullOrWhiteSpace(settings.Logging.Runtime.ApplicationLogFileName))
                    {
                        result.Errors.Add(LocalizationService.Get("Settings.Validation.ApplicationLogFileNameRequired"));
                    }

                    if (settings.Logging.Runtime.MaxFileSizeMb <= 0)
                    {
                        result.Errors.Add(LocalizationService.Get("Settings.Validation.RuntimeLogMaxSizePositive"));
                    }

                    if (settings.Logging.Runtime.RetainedFileCountLimit <= 0)
                    {
                        result.Errors.Add(LocalizationService.Get("Settings.Validation.RuntimeLogRetainedCountPositive"));
                    }
                }

                if (settings.Logging.ModelTranscripts == null)
                {
                    result.Errors.Add(LocalizationService.Get("Settings.Validation.ModelTranscriptLoggingMissing"));
                }
                else
                {
                    if (settings.Logging.ModelTranscripts.IsEnabled &&
                        string.IsNullOrWhiteSpace(settings.Logging.ModelTranscripts.FileName))
                    {
                        result.Errors.Add(LocalizationService.Get("Settings.Validation.ModelTranscriptFileNameRequired"));
                    }

                    if (settings.Logging.ModelTranscripts.MaxContentLength < 0)
                    {
                        result.Errors.Add(LocalizationService.Get("Settings.Validation.ModelTranscriptMaxContentLengthNonNegative"));
                    }
                }
            }

            if (settings.ModelProviders == null)
            {
                result.Errors.Add(LocalizationService.Get("Settings.Validation.ModelProvidersRequired"));
            }
            else
            {
                var duplicatedModelIds = settings.ModelProviders
                    .Where(m => m != null && m.Id > 0)
                    .GroupBy(m => m.Id)
                    .Where(g => g.Count() > 1)
                    .Select(g => g.Key)
                    .ToList();

                foreach (var duplicatedModelId in duplicatedModelIds)
                {
                    result.Errors.Add(LocalizationService.Format("Settings.Validation.DuplicateModelId", duplicatedModelId));
                }

                foreach (var provider in settings.ModelProviders)
                {
                    if (provider == null)
                    {
                        result.Errors.Add(LocalizationService.Get("Settings.Validation.NullModelProvider"));
                        continue;
                    }

                    if (provider.Id <= 0)
                    {
                        result.Errors.Add(LocalizationService.Get("Settings.Validation.ModelIdPositive"));
                    }

                    if (string.IsNullOrWhiteSpace(provider.Name))
                    {
                        result.Errors.Add(LocalizationService.Get("Settings.Validation.ModelNameRequired"));
                    }

                    if (provider.IsEnabled && string.IsNullOrWhiteSpace(provider.ModelId))
                    {
                        result.Errors.Add(LocalizationService.Format("Settings.Validation.EnabledModelIdRequired", provider.Name));
                    }

                    if (provider.MaxContextTokens <= 0)
                    {
                        result.Errors.Add(LocalizationService.Format("Settings.Validation.MaxContextTokensPositive", provider.Name));
                    }

                    var temperature = provider.GetTemperature();
                    if (temperature < 0d || temperature > 2d)
                    {
                        result.Errors.Add(LocalizationService.Format("Settings.Validation.TemperatureRange", provider.Name));
                    }

                    var topP = provider.GetTopP();
                    if (topP < 0d || topP > 1d)
                    {
                        result.Errors.Add(LocalizationService.Format("Settings.Validation.TopPRange", provider.Name));
                    }
                }
            }

            if (settings.McpServers == null)
            {
                result.Errors.Add(LocalizationService.Get("Settings.Validation.McpServersRequired"));
            }
            else
            {
                var duplicatedMcpIds = settings.McpServers
                    .Where(m => !string.IsNullOrWhiteSpace(m.Id))
                    .GroupBy(m => m.Id, StringComparer.OrdinalIgnoreCase)
                    .Where(g => g.Count() > 1)
                    .Select(g => g.Key)
                    .ToList();

                foreach (var duplicatedMcpId in duplicatedMcpIds)
                {
                    result.Errors.Add(LocalizationService.Format("Settings.Validation.DuplicateMcpId", duplicatedMcpId));
                }

                // 运行时以 server 名（大小写不敏感）作为连接与工具名前缀，重名会导致后者被忽略。
                var duplicatedMcpNames = settings.McpServers
                    .Where(m => m != null && !string.IsNullOrWhiteSpace(m.Name))
                    .GroupBy(m => m.Name, StringComparer.OrdinalIgnoreCase)
                    .Where(g => g.Count() > 1)
                    .Select(g => g.Key)
                    .ToList();

                foreach (var duplicatedMcpName in duplicatedMcpNames)
                {
                    result.Errors.Add(LocalizationService.Format("Settings.Validation.DuplicateMcpName", duplicatedMcpName));
                }

                foreach (var server in settings.McpServers)
                {
                    if (server == null)
                    {
                        result.Errors.Add(LocalizationService.Get("Settings.Validation.NullMcpServer"));
                        continue;
                    }

                    if (string.IsNullOrWhiteSpace(server.Id))
                    {
                        result.Errors.Add(LocalizationService.Get("Settings.Validation.McpIdRequired"));
                    }

                    if (string.IsNullOrWhiteSpace(server.Name))
                    {
                        result.Errors.Add(LocalizationService.Get("Settings.Validation.McpNameRequired"));
                    }

                    if (server.Stdio != null && server.Http != null)
                    {
                        result.Errors.Add(LocalizationService.Format("Settings.Validation.McpTransportExclusive", server.Name));
                    }

                    if (server.Stdio == null && server.Http == null)
                    {
                        result.Errors.Add(LocalizationService.Format("Settings.Validation.McpTransportRequired", server.Name));
                    }

                    if (server.Stdio != null && string.IsNullOrWhiteSpace(server.Stdio.Command))
                    {
                        result.Errors.Add(LocalizationService.Format("Settings.Validation.StdioCommandRequired", server.Name));
                    }

                    if (server.Http != null && string.IsNullOrWhiteSpace(server.Http.Endpoint))
                    {
                        result.Errors.Add(LocalizationService.Format("Settings.Validation.HttpEndpointRequired", server.Name));
                    }
                }
            }

            if (settings.DefaultModelProviderId.HasValue)
            {
                var exists = settings.ModelProviders != null &&
                             settings.ModelProviders.Any(m => m != null && m.Id == settings.DefaultModelProviderId.Value);

                if (!exists)
                {
                    result.Errors.Add(LocalizationService.Format("Settings.Validation.DefaultModelProviderMissing", settings.DefaultModelProviderId.Value));
                }
            }

            return result;
        }
    }
}
