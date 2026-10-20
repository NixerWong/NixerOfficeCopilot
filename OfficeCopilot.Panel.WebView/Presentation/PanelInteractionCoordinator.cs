using OfficeCopilot.Agent.Client;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace OfficeCopilot.Panel.WebView.Presentation
{
    /// <summary>
    /// F1：从 <c>CopilotPanelControl</c> 抽出的待决用户交互登记簿。
    /// 原实现把 requestId→TCS 与 requestId→toolName 两张表连同各自的 lock
    /// 散落在面板主体、ToolSafety 分部与嵌套的 BrokerEventSink 三处；
    /// 这里收敛为单锁单入口，注册 / 完成 / 注销的配对关系可一眼看全。
    /// </summary>
    internal sealed class PanelInteractionCoordinator
    {
        private readonly object _gate = new object();

        private readonly Dictionary<string, TaskCompletionSource<AgentBrokerToolUserInteractionResponse>> _pending =
            new Dictionary<string, TaskCompletionSource<AgentBrokerToolUserInteractionResponse>>(StringComparer.Ordinal);

        private readonly Dictionary<string, string> _toolNames =
            new Dictionary<string, string>(StringComparer.Ordinal);

        /// <summary>
        /// 登记一个等待中的交互。同一 requestId 上尚未完成的旧请求会被取消，避免调用方永久挂起。
        /// </summary>
        public TaskCompletionSource<AgentBrokerToolUserInteractionResponse> Register(string requestId, string toolName)
        {
            var tcs = new TaskCompletionSource<AgentBrokerToolUserInteractionResponse>(
                TaskCreationOptions.RunContinuationsAsynchronously);

            TaskCompletionSource<AgentBrokerToolUserInteractionResponse> stale = null;

            lock (_gate)
            {
                if (_pending.TryGetValue(requestId, out stale) && stale.Task.IsCompleted)
                {
                    stale = null;
                }

                _pending[requestId] = tcs;

                if (toolName != null)
                {
                    _toolNames[requestId] = toolName;
                }
            }

            if (stale != null)
            {
                stale.TrySetCanceled();
            }

            return tcs;
        }

        public void Unregister(string requestId, bool forgetToolName)
        {
            lock (_gate)
            {
                _pending.Remove(requestId);
                if (forgetToolName)
                {
                    _toolNames.Remove(requestId);
                }
            }
        }

        public bool TryGetToolName(string requestId, out string toolName)
        {
            lock (_gate)
            {
                return _toolNames.TryGetValue(requestId, out toolName);
            }
        }

        /// <summary>
        /// 兑现一个等待中的交互。TCS 本身由发起方在 finally 里注销，这里只清掉工具名映射。
        /// </summary>
        public bool TryComplete(string requestId, AgentBrokerToolUserInteractionResponse response)
        {
            TaskCompletionSource<AgentBrokerToolUserInteractionResponse> tcs;

            lock (_gate)
            {
                if (!_pending.TryGetValue(requestId, out tcs) || tcs.Task.IsCompleted)
                {
                    return false;
                }

                _toolNames.Remove(requestId);
            }

            return tcs.TrySetResult(response);
        }
    }
}
