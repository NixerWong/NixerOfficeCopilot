using OfficeCopilot.Agent.Client;
using OfficeCopilot.Agent.Persistence;
using OfficeCopilot.Agent.Planning;
using OfficeCopilot.Agent.Runtime;
using OfficeCopilot.HostBridge.Client;
using OfficeCopilot.HostBridge.Contracts;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace OfficeCopilot.Agent.Tests.Harness
{
    /// <summary>
    /// 组装一个纯内存的 <see cref="BrokerTurnExecutor"/> 回合：脚本模型 + 假宿主工具 + 假能力集。
    /// 不触网、不触 COM、不落盘，秒级完成，用于回归 Agent 循环本身的行为。
    /// </summary>
    internal sealed class TurnHarness
    {
        private readonly List<HostBridgeToolDescriptor> _tools = new List<HostBridgeToolDescriptor>();
        private readonly List<SkillDefinition> _skills = new List<SkillDefinition>();

        public ScriptedModelClient Model { get; } = new ScriptedModelClient();

        public FakeHostToolInvoker Host { get; } = new FakeHostToolInvoker();

        public RecordingEventSink Events { get; } = new RecordingEventSink();

        public BrokerConversationState Conversation { get; } = new BrokerConversationState { ConversationId = "conv-test" };

        public AgentRuntimeOptions Options { get; } = new AgentRuntimeOptions
        {
            // 技能规划会引入 SKILL.md 的白名单与轮次上限，回放场景要的是可预测的裸循环。
            EnableSkillPlanning = false,
            DefaultSkillName = string.Empty,
            StorageMode = ConversationStorageMode.InMemory
        };

        public string InteractiveMode { get; set; } = "agent";

        public int MaxToolRoundCount { get; set; }

        /// <summary>置 true 时 GetCapabilities 抛异常，用于验证能力获取失败的处理。</summary>
        public bool CapabilitiesThrow { get; set; }

        /// <summary>注入测试专用技能，避免执行计划类用例被真实技能目录的措辞变动弄砍。</summary>
        public TurnHarness WithSkill(SkillDefinition skill)
        {
            if (skill == null) return this;
            _skills.Add(skill);
            SkillId = skill.Name;
            Options.EnableSkillPlanning = true;
            return this;
        }

        /// <summary>显式指定技能，让技能选择不受关键词打分影响。</summary>
        public string SkillId { get; set; }

        public TurnHarness WithTool(string name, bool isReadOnly)
        {
            _tools.Add(new HostBridgeToolDescriptor
            {
                Name = name,
                DisplayName = name,
                Description = name,
                InputSchemaJson = "{\"type\":\"object\",\"properties\":{}}",
                IsReadOnly = isReadOnly,
                RiskLevel = isReadOnly ? "low" : "medium"
            });
            return this;
        }

        public AgentBrokerTurnResult Run(string userInput)
        {
            return RunAsync(userInput).GetAwaiter().GetResult();
        }

        public async Task<AgentBrokerTurnResult> RunAsync(string userInput)
        {
            var request = new AgentBrokerTurnRequest
            {
                ConversationId = Conversation.ConversationId,
                MessageId = "msg-test",
                UserInput = userInput ?? string.Empty,
                InteractiveMode = InteractiveMode,
                SkillId = SkillId ?? string.Empty,
                ModelOptions = new AgentModelOptions
                {
                    // Endpoint 非空是 CanUseModel 的门槛；不会真的发起请求。
                    Endpoint = "http://localhost/fake",
                    ModelId = "fake-model"
                }
            };
            request.ModelOptions.AdditionalParameters[AgentModelAdditionalParameterNames.SupportsFunctionCalling] = true;
            if (MaxToolRoundCount > 0)
            {
                request.Metadata["runtime.maxToolRoundCount"] = MaxToolRoundCount.ToString();
            }

            var capabilities = new HostBridgeCapabilities { HostApplicationName = "Excel" };
            foreach (var tool in _tools)
            {
                capabilities.Tools.Add(tool);
                capabilities.ToolNames.Add(tool.Name);
            }

            var snapshot = new HostContextSnapshotDto
            {
                HostApplicationName = "Excel",
                DocumentName = "工作簿1.xlsx",
                WorksheetName = "Sheet1",
                Summary = "Excel / 工作簿1.xlsx / Sheet1"
            };

            var executor = new BrokerTurnExecutor(
                Options,
                new BrokerEventDispatcher(),
                new FakeHostContextReader(snapshot),
                new FakeHostBridgeClient(capabilities, CapabilitiesThrow),
                Host,
                Model,
                skillRegistry: _skills.Count == 0 ? null : new InMemorySkillRegistry(_skills));

            var context = new BrokerTurnContext(request, Conversation, Events, snapshot, CancellationToken.None);
            return await executor.ExecuteAsync(context).ConfigureAwait(false);
        }
    }

    internal sealed class FakeHostContextReader : IHostContextReader
    {
        private readonly HostContextSnapshotDto _snapshot;

        public FakeHostContextReader(HostContextSnapshotDto snapshot)
        {
            _snapshot = snapshot ?? new HostContextSnapshotDto();
        }

        public Task<HostContextSnapshotDto> GetSnapshotAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(_snapshot);
        }
    }

    internal sealed class FakeHostBridgeClient : IHostBridgeClient
    {
        private readonly HostBridgeCapabilities _capabilities;
        private readonly bool _throwOnCapabilities;

        public FakeHostBridgeClient(HostBridgeCapabilities capabilities, bool throwOnCapabilities)
        {
            _capabilities = capabilities ?? new HostBridgeCapabilities();
            _throwOnCapabilities = throwOnCapabilities;
            Options = new HostBridgeClientOptions();
        }

        public HostBridgeClientOptions Options { get; private set; }

        public Task<HostBridgeCapabilities> GetCapabilitiesAsync(CancellationToken cancellationToken)
        {
            if (_throwOnCapabilities) throw new InvalidOperationException("host bridge unavailable");
            return Task.FromResult(_capabilities);
        }

        public Task<HostBridgeResponse> SendAsync(HostBridgeRequest request, CancellationToken cancellationToken)
        {
            return Task.FromResult(new HostBridgeResponse());
        }
    }

    internal sealed class RecordingEventSink : IAgentBrokerEventSink
    {
        public List<AgentBrokerEvent> Events { get; } = new List<AgentBrokerEvent>();

        public Task OnEventAsync(AgentBrokerEvent brokerEvent, CancellationToken cancellationToken)
        {
            if (brokerEvent != null) Events.Add(brokerEvent);
            return Task.CompletedTask;
        }
    }
}
