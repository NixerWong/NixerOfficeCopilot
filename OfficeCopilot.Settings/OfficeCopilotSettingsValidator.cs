using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;
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
                result.Errors.Add("设置对象不能为空。");
                return result;
            }

            if (settings.ChatDefaults == null)
            {
                result.Errors.Add("缺少聊天默认配置。");
            }

            if (settings.Logging == null)
            {
                result.Errors.Add("缺少日志配置。");
            }
            else
            {
                if (settings.Logging.Runtime == null)
                {
                    result.Errors.Add("缺少运行时日志配置。");
                }
                else
                {
                    if (settings.Logging.Runtime.EnableFile &&
                        string.IsNullOrWhiteSpace(settings.Logging.Runtime.ApplicationLogFileName))
                    {
                        result.Errors.Add("启用文件日志时，ApplicationLogFileName 不能为空。");
                    }

                    if (settings.Logging.Runtime.MaxFileSizeMb <= 0)
                    {
                        result.Errors.Add("运行时日志文件大小上限必须大于 0。");
                    }

                    if (settings.Logging.Runtime.RetainedFileCountLimit <= 0)
                    {
                        result.Errors.Add("运行时日志保留文件数必须大于 0。");
                    }
                }

                if (settings.Logging.ModelTranscripts == null)
                {
                    result.Errors.Add("缺少模型收发日志配置。");
                }
                else
                {
                    if (settings.Logging.ModelTranscripts.IsEnabled &&
                        string.IsNullOrWhiteSpace(settings.Logging.ModelTranscripts.FileName))
                    {
                        result.Errors.Add("启用模型收发日志时，FileName 不能为空。");
                    }

                    if (settings.Logging.ModelTranscripts.MaxContentLength < 0)
                    {
                        result.Errors.Add("模型收发日志 MaxContentLength 不能小于 0。");
                    }
                }
            }

            if (settings.ModelProviders == null)
            {
                result.Errors.Add("模型提供者列表不能为空。");
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
                    result.Errors.Add("存在重复的模型配置 Id：" + duplicatedModelId);
                }

                foreach (var provider in settings.ModelProviders)
                {
                    if (provider == null)
                    {
                        result.Errors.Add("存在空的模型配置项。");
                        continue;
                    }

                    if (provider.Id <= 0)
                    {
                        result.Errors.Add("模型配置的 Id 必须大于 0。");
                    }

                    if (string.IsNullOrWhiteSpace(provider.Name))
                    {
                        result.Errors.Add("模型配置名称不能为空。");
                    }

                    if (provider.IsEnabled && string.IsNullOrWhiteSpace(provider.ModelId))
                    {
                        result.Errors.Add("启用的模型必须填写 ModelId。名称：" + provider.Name);
                    }

                    if (provider.MaxContextTokens <= 0)
                    {
                        result.Errors.Add("模型配置最大上下文长度必须大于 0。名称：" + provider.Name);
                    }

                    var temperature = provider.GetTemperature();
                    if (temperature < 0d || temperature > 2d)
                    {
                        result.Errors.Add("Temperature 必须位于 0~2 之间。名称：" + provider.Name);
                    }

                    var topP = provider.GetTopP();
                    if (topP < 0d || topP > 1d)
                    {
                        result.Errors.Add("TopP 必须位于 0~1 之间。名称：" + provider.Name);
                    }
                }
            }

            if (settings.McpServers == null)
            {
                result.Errors.Add("MCP 服务器列表不能为空。");
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
                    result.Errors.Add("存在重复的 MCP 服务器 Id：" + duplicatedMcpId);
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
                    result.Errors.Add("存在重复的 MCP 服务器名称（不区分大小写）：" + duplicatedMcpName);
                }

                foreach (var server in settings.McpServers)
                {
                    if (server == null)
                    {
                        result.Errors.Add("存在空的 MCP 服务器项。");
                        continue;
                    }

                    if (string.IsNullOrWhiteSpace(server.Id))
                    {
                        result.Errors.Add("MCP 服务器 Id 不能为空。");
                    }

                    if (string.IsNullOrWhiteSpace(server.Name))
                    {
                        result.Errors.Add("MCP 服务器名称不能为空。");
                    }

                    if (server.Stdio != null && server.Http != null)
                    {
                        result.Errors.Add("MCP 配置不能同时指定 Stdio 和 Http。名称：" + server.Name);
                    }

                    if (server.Stdio == null && server.Http == null)
                    {
                        result.Errors.Add("MCP 配置必须选择一种传输方式。名称：" + server.Name);
                    }

                    if (server.Stdio != null && string.IsNullOrWhiteSpace(server.Stdio.Command))
                    {
                        result.Errors.Add("Stdio MCP 配置必须填写 Command。名称：" + server.Name);
                    }

                    if (server.Http != null && string.IsNullOrWhiteSpace(server.Http.Endpoint))
                    {
                        result.Errors.Add("Http MCP 配置必须填写 Endpoint。名称：" + server.Name);
                    }
                }
            }

            if (settings.DefaultModelProviderId.HasValue)
            {
                var exists = settings.ModelProviders != null &&
                             settings.ModelProviders.Any(m => m != null && m.Id == settings.DefaultModelProviderId.Value);

                if (!exists)
                {
                    result.Errors.Add("默认模型配置不存在：" + settings.DefaultModelProviderId.Value);
                }
            }

            return result;
        }
    }
}
