using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using OfficeCopilot.Agent.Client;

namespace OfficeCopilot.Agent.Runtime.Turn
{
    /// <summary>
    /// 阶段 7A 流式实现：用 <see cref="Channel{T}"/> 把 <see cref="IModelClient.CompleteAsync"/>
    /// 内部的回调式 streaming 桥接为 <see cref="IAsyncEnumerable{ModelStreamEvent}"/>。
    /// HttpModelClient 内部已处理 SSE 流式工具调用增量解析，本层只管事件桥接。
    /// </summary>
    internal sealed class DefaultModelInvoker : IModelInvoker
    {
        private readonly IModelClient _modelClient;

        public DefaultModelInvoker(IModelClient modelClient)
        {
            _modelClient = modelClient ?? throw new ArgumentNullException(nameof(modelClient));
        }

        // ---- 阶段 7A：流式 IAsyncEnumerable ----

        public async IAsyncEnumerable<ModelStreamEvent> StreamAsync(
            AgentModelOptions modelOptions,
            IReadOnlyList<ModelClientMessage> messages,
            IReadOnlyList<ModelClientTool> tools,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            // 用 Channel 做 producer-consumer 桥接：HttpModelClient 回调写 channel
            var channel = Channel.CreateUnbounded<ModelStreamEvent>();

            var contentBuilder = new StringBuilder();
            string finishReason = null;

            // Producer：在后台跑 IModelClient.CompleteAsync，通过 callbacks 写 channel
            var producerTask = Task.Run(async () =>
            {
                try
                {
                    var callbacks = new ModelClientStreamingCallbacks
                    {
                        OnContentDeltaAsync = (delta, ct) =>
                        {
                            if (!string.IsNullOrEmpty(delta))
                            {
                                contentBuilder.Append(delta);
                                return channel.Writer.WriteAsync(ModelStreamEvent.TextDelta(delta), ct).AsTask();
                            }
                            return Task.CompletedTask;
                        },
                        OnReasoningDeltaAsync = (delta, ct) =>
                        {
                            if (!string.IsNullOrEmpty(delta))
                            {
                                return channel.Writer.WriteAsync(ModelStreamEvent.ReasoningDelta(delta), ct).AsTask();
                            }
                            return Task.CompletedTask;
                        }
                    };

                    var completion = await _modelClient.CompleteAsync(
                        modelOptions,
                        messages ?? Array.Empty<ModelClientMessage>(),
                        tools ?? Array.Empty<ModelClientTool>(),
                        callbacks,
                        cancellationToken).ConfigureAwait(false)
                        ?? new ModelClientCompletion();

                    finishReason = string.IsNullOrWhiteSpace(completion.FinishReason)
                        ? (completion.HasToolCalls ? "tool_calls" : "stop")
                        : completion.FinishReason;

                    if (completion.HasToolCalls)
                    {
                        await channel.Writer.WriteAsync(
                            ModelStreamEvent.ToolCalls(completion.ToolCalls.ToList()),
                            cancellationToken).ConfigureAwait(false);
                    }
                }
                catch (Exception ex)
                {
                    await channel.Writer.WriteAsync(
                        ModelStreamEvent.Error(ex.Message),
                        CancellationToken.None).ConfigureAwait(false);
                }
                finally
                {
                    channel.Writer.Complete();
                }
            }, cancellationToken);

            // 从 Channel 逐事件 yield，实现真正的流式输出
            await foreach (var evt in channel.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                yield return evt;
            }

            await producerTask.ConfigureAwait(false);

            yield return ModelStreamEvent.TurnCompleted(finishReason ?? "stop");
        }

        // ---- 旧路径（保留兼容） ----

        public Task<ModelClientCompletion> InvokeAsync(
            AgentModelOptions modelOptions,
            IReadOnlyList<ModelClientMessage> messages,
            IReadOnlyList<ModelClientTool> tools,
            ModelClientStreamingCallbacks streamingCallbacks,
            CancellationToken cancellationToken)
        {
            return _modelClient.CompleteAsync(
                modelOptions,
                messages ?? Array.Empty<ModelClientMessage>(),
                tools ?? Array.Empty<ModelClientTool>(),
                streamingCallbacks ?? new ModelClientStreamingCallbacks(),
                cancellationToken);
        }
    }
}
