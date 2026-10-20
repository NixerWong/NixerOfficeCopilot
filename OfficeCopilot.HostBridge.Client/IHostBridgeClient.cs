using OfficeCopilot.HostBridge.Contracts;
using System.Threading;
using System.Threading.Tasks;

namespace OfficeCopilot.HostBridge.Client
{
    public interface IHostBridgeClient
    {
        HostBridgeClientOptions Options { get; }

        Task<HostBridgeCapabilities> GetCapabilitiesAsync(CancellationToken cancellationToken);

        Task<HostBridgeResponse> SendAsync(HostBridgeRequest request, CancellationToken cancellationToken);
    }
}
