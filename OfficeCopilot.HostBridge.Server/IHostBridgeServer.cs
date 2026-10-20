using System;
using System.Threading;
using System.Threading.Tasks;

namespace OfficeCopilot.HostBridge.Server
{
    public interface IHostBridgeServer : IDisposable
    {
        HostBridgeServerOptions Options { get; }

        Task StartAsync(CancellationToken cancellationToken);

        Task StopAsync(CancellationToken cancellationToken);
    }
}
