using OfficeCopilot.HostBridge.Contracts;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace OfficeCopilot.HostBridge.Server
{
    public sealed class NullHostBridgeServer : IHostBridgeServer
    {
        public NullHostBridgeServer()
            : this(new HostBridgeServerOptions())
        {
        }

        public NullHostBridgeServer(HostBridgeServerOptions options)
        {
            Options = options ?? new HostBridgeServerOptions();
        }

        public HostBridgeServerOptions Options { get; private set; }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }

        public void Dispose()
        {
        }
    }
}
