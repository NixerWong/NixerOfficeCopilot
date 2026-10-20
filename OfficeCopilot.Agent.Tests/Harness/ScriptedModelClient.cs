using OfficeCopilot.Agent.Client;
using OfficeCopilot.Agent.Runtime;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace OfficeCopilot.Agent.Tests.Harness
{
    /// <summary>
    /// 按脚本作答的 <see cref="IModelClient"/>，让 <see cref="BrokerTurnExecutor"/> 的完整回合可确定性回放。
    /// 同时记录每一轮收到的消息与工具集合，供断言"下发了什么、模型看见了什么"。
    /// </summary>
    internal sealed class ScriptedModelClient : IModelClient
    {
        private readonly List<ScriptedModelResponse> _scripted = new List<ScriptedModelResponse>();
        private Func<int, ScriptedModelResponse> _fallback;
        private int _callCount;

        public List<ScriptedModelRequest> Requests { get; } = new List<ScriptedModelRequest>();

        public int CallCount
        {
            get { return _callCount; }
        }

        public ScriptedModelClient Then(ScriptedModelResponse response)
        {
            _scripted.Add(response ?? ScriptedModelResponse.Text(string.Empty));
            return this;
        }

        /// <summary>脚本耗尽后的兜底作答；参数是从 0 起算的调用序号。</summary>
        public ScriptedModelClient ThenAlways(Func<int, ScriptedModelResponse> fallback)
        {
            _fallback = fallback;
            return this;
        }

        public async Task<ModelClientCompletion> CompleteAsync(
            AgentModelOptions modelOptions,
            IReadOnlyList<ModelClientMessage> messages,
            IReadOnlyList<ModelClientTool> tools,
            ModelClientStreamingCallbacks streamingCallbacks,
            CancellationToken cancellationToken)
        {
            var index = _callCount++;
            Requests.Add(new ScriptedModelRequest(messages, tools));

            var response = index < _scripted.Count
                ? _scripted[index]
                : (_fallback != null ? _fallback(index) : ScriptedModelResponse.Text("done"));
            if (response == null) response = ScriptedModelResponse.Text("done");

            if (!string.IsNullOrEmpty(response.Content) && streamingCallbacks?.OnContentDeltaAsync != null)
            {
                await streamingCallbacks.OnContentDeltaAsync(response.Content, cancellationToken).ConfigureAwait(false);
            }

            var completion = new ModelClientCompletion
            {
                Content = response.Content ?? string.Empty,
                FinishReason = !string.IsNullOrWhiteSpace(response.FinishReason)
                    ? response.FinishReason
                    : (response.ToolCalls.Count > 0 ? "tool_calls" : "stop")
            };

            foreach (var call in response.ToolCalls)
            {
                completion.ToolCalls.Add(new ModelClientToolCall
                {
                    Id = string.IsNullOrWhiteSpace(call.Id) ? "call-" + index + "-" + completion.ToolCalls.Count : call.Id,
                    Name = call.Name ?? string.Empty,
                    ArgumentsJson = string.IsNullOrWhiteSpace(call.ArgumentsJson) ? "{}" : call.ArgumentsJson
                });
            }

            return completion;
        }
    }

    internal sealed class ScriptedModelResponse
    {
        private ScriptedModelResponse()
        {
            Content = string.Empty;
            ToolCalls = new List<ModelClientToolCall>();
        }

        public string Content { get; private set; }

        public List<ModelClientToolCall> ToolCalls { get; private set; }

        /// <summary>显式指定 finish reason（默认按是否有工具调用推导）。用于模拟 max_tokens 截断等场景。</summary>
        public string FinishReason { get; private set; }

        public static ScriptedModelResponse Text(string content)
        {
            return new ScriptedModelResponse { Content = content ?? string.Empty };
        }

        /// <summary>模拟模型输出被 max_tokens 截断、未产生任何文本。</summary>
        public static ScriptedModelResponse TruncatedEmpty()
        {
            return new ScriptedModelResponse { Content = string.Empty, FinishReason = "max_tokens" };
        }

        public static ScriptedModelResponse Call(string toolName, string argumentsJson)
        {
            var response = new ScriptedModelResponse();
            response.ToolCalls.Add(new ModelClientToolCall { Name = toolName, ArgumentsJson = argumentsJson ?? "{}" });
            return response;
        }

        public ScriptedModelResponse AndCall(string toolName, string argumentsJson)
        {
            ToolCalls.Add(new ModelClientToolCall { Name = toolName, ArgumentsJson = argumentsJson ?? "{}" });
            return this;
        }
    }

    /// <summary>某一轮真实发给模型的内容快照。</summary>
    internal sealed class ScriptedModelRequest
    {
        public ScriptedModelRequest(IReadOnlyList<ModelClientMessage> messages, IReadOnlyList<ModelClientTool> tools)
        {
            Messages = messages == null ? new List<ModelClientMessage>() : new List<ModelClientMessage>(messages);
            ToolNames = tools == null
                ? new List<string>()
                : tools.Select(t => t == null ? string.Empty : t.Name ?? string.Empty).ToList();
        }

        public List<ModelClientMessage> Messages { get; private set; }

        public List<string> ToolNames { get; private set; }

        public string SystemText
        {
            get
            {
                var builder = new StringBuilder();
                foreach (var message in Messages)
                {
                    if (message != null && string.Equals(message.Role, "system", StringComparison.OrdinalIgnoreCase))
                    {
                        builder.AppendLine(message.Content ?? string.Empty);
                    }
                }
                return builder.ToString();
            }
        }

        /// <summary>取某个 tool_call_id 对应的 tool 消息正文，即模型实际读到的工具结果。</summary>
        public string GetToolMessageContent(string toolName)
        {
            for (var i = Messages.Count - 1; i >= 0; i--)
            {
                var message = Messages[i];
                if (message != null
                    && string.Equals(message.Role, "tool", StringComparison.OrdinalIgnoreCase)
                    && string.Equals(message.Name, toolName, StringComparison.OrdinalIgnoreCase))
                {
                    return message.Content ?? string.Empty;
                }
            }
            return null;
        }
    }
}
