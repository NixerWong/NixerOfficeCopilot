using System;
using System.Threading.Tasks;
using OfficeCopilot.Agent.Abstractions;
using OfficeCopilot.Host.Abstractions;

namespace OfficeCopilot.Host.Word.Tools
{
    public abstract class WordToolBase : IAgentTool
    {
        public abstract ToolDefinition Definition { get; }
        public abstract Task<ToolExecutionResult> ExecuteAsync(ToolExecutionContext context, ToolExecutionRequest request);

        protected static IWordToolHost GetHost(ToolExecutionContext context)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            return context.GetService<IWordToolHost>()
                ?? throw new InvalidOperationException("IWordToolHost not registered.");
        }

        protected ToolExecutionResult Success(ToolExecutionRequest req, string content, string json = null)
            => ToolExecutionResult.CreateSuccess(Definition.Name, req?.ToolCallId ?? "", content, json);

        protected ToolExecutionResult Fail(ToolExecutionRequest req, string msg, string code = null)
            => ToolExecutionResult.CreateFailure(Definition.Name, req?.ToolCallId ?? "", msg, code);
    }
}
