using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using OfficeCopilot.Agent.Client;

namespace OfficeCopilot.Agent.Runtime.Turn
{
    /// <summary>
    /// 模型调用 facade，包装现有 <see cref="IModelClient"/>，把 schema 暴露面收窄为 turn 编排所需。
    /// 阶段 7A：新增 <see cref="StreamAsync"/> 返回 <see cref="IAsyncEnumerable{ModelStreamEvent}"/>，
    /// 替代旧的 <see cref="InvokeAsync"/> 一次性返回模式，使执行器能边收事件边派发工具 + 检查 token 预算。
    /// </summary>
    internal interface IModelInvoker
    {
        /// <summary>
        /// 流式调用模型，逐事件产出 <see cref="ModelStreamEvent"/>。
        /// 消费方用 <c>await foreach</c> 消费事件，在工具调用完成时立即派发，在 token 预算耗尽时提前中断。
        /// </summary>
        IAsyncEnumerable<ModelStreamEvent> StreamAsync(
            AgentModelOptions modelOptions,
            IReadOnlyList<ModelClientMessage> messages,
            IReadOnlyList<ModelClientTool> tools,
            CancellationToken cancellationToken);

        /// <summary>
        /// [已废弃] 一次性模型调用，阶段 7A 之后新代码应使用 <see cref="StreamAsync"/>。
        /// 保留以兼容旧路径。
        /// </summary>
        Task<ModelClientCompletion> InvokeAsync(
            AgentModelOptions modelOptions,
            IReadOnlyList<ModelClientMessage> messages,
            IReadOnlyList<ModelClientTool> tools,
            ModelClientStreamingCallbacks streamingCallbacks,
            CancellationToken cancellationToken);
    }
}
