using OfficeCopilot.HostBridge.Contracts;
using System.Threading;
using System.Threading.Tasks;

namespace OfficeCopilot.HostBridge.Server
{
    public interface IHostBridgeRequestHandler
    {
        Task<HostBridgeResponse> HandleAsync(HostBridgeRequest request, CancellationToken cancellationToken);
    }
}
