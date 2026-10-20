using OfficeCopilot.Agent.Runtime;
using OfficeCopilot.HostBridge.Contracts;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace OfficeCopilot.Agent.Tests.Harness
{
    /// <summary>
    /// 内存版宿主工具执行器。默认所有工具成功返回，可按工具名注入失败或自定义结果。
    /// </summary>
    internal sealed class FakeHostToolInvoker : IHostToolInvoker
    {
        private readonly Dictionary<string, Func<HostToolInvocationRequest, HostToolInvocationResult>> _handlers =
            new Dictionary<string, Func<HostToolInvocationRequest, HostToolInvocationResult>>(StringComparer.OrdinalIgnoreCase);

        public List<HostToolInvocationRequest> Invocations { get; } = new List<HostToolInvocationRequest>();

        public List<string> InvokedToolNames { get; } = new List<string>();

        public FakeHostToolInvoker On(string toolName, Func<HostToolInvocationRequest, HostToolInvocationResult> handler)
        {
            _handlers[toolName] = handler;
            return this;
        }

        public FakeHostToolInvoker Succeeds(string toolName, string content)
        {
            return On(toolName, request => new HostToolInvocationResult
            {
                ToolCallId = request.ToolCallId,
                ToolName = request.ToolName,
                Success = true,
                Content = content ?? string.Empty
            });
        }

        public FakeHostToolInvoker Fails(string toolName, string errorCode, string errorMessage, string content)
        {
            return Fails(toolName, errorCode, errorMessage, content, null);
        }

        public FakeHostToolInvoker Fails(string toolName, string errorCode, string errorMessage, string content, string structuredContentJson)
        {
            return On(toolName, request => new HostToolInvocationResult
            {
                ToolCallId = request.ToolCallId,
                ToolName = request.ToolName,
                Success = false,
                Content = content ?? string.Empty,
                StructuredContentJson = structuredContentJson ?? string.Empty,
                ErrorCode = errorCode ?? string.Empty,
                ErrorMessage = errorMessage ?? string.Empty
            });
        }

        public int CountOf(string toolName)
        {
            var count = 0;
            foreach (var name in InvokedToolNames)
            {
                if (string.Equals(name, toolName, StringComparison.OrdinalIgnoreCase)) count++;
            }
            return count;
        }

        public Task<HostToolInvocationResult> InvokeAsync(HostToolInvocationRequest request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Invocations.Add(request);
            InvokedToolNames.Add(request?.ToolName ?? string.Empty);

            Func<HostToolInvocationRequest, HostToolInvocationResult> handler;
            if (request != null && _handlers.TryGetValue(request.ToolName ?? string.Empty, out handler) && handler != null)
            {
                return Task.FromResult(handler(request));
            }

            return Task.FromResult(new HostToolInvocationResult
            {
                ToolCallId = request?.ToolCallId ?? string.Empty,
                ToolName = request?.ToolName ?? string.Empty,
                Success = true,
                Content = "ok"
            });
        }
    }
}
