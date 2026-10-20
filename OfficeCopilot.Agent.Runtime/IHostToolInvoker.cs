using OfficeCopilot.HostBridge.Contracts;
using System.Threading;
using System.Threading.Tasks;

namespace OfficeCopilot.Agent.Runtime
{
    public interface IHostToolInvoker
    {
        Task<HostToolInvocationResult> InvokeAsync(
            HostToolInvocationRequest request,
            CancellationToken cancellationToken);
    }

    public interface IHostToolPreflightInvoker
    {
        Task<PreflightToolCallResult> PreflightToolCallAsync(
            PreflightToolCallRequest request,
            CancellationToken cancellationToken);
    }
}
