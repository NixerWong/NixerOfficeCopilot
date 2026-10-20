using OfficeCopilot.HostBridge.Contracts;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace OfficeCopilot.HostBridge.Client
{
    public sealed class NullHostBridgeClient : IHostBridgeClient
    {
        public NullHostBridgeClient()
            : this(new HostBridgeClientOptions())
        {
        }

        public NullHostBridgeClient(HostBridgeClientOptions options)
        {
            Options = options ?? new HostBridgeClientOptions();
        }

        public HostBridgeClientOptions Options { get; private set; }

        public Task<HostBridgeCapabilities> GetCapabilitiesAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            return Task.FromResult(new HostBridgeCapabilities
            {
                HostApplicationName = "Unknown",
                HostVersion = string.Empty,
                CanExecuteCode = false,
                CanInsertText = false,
                ToolNames = new List<string>()
            });
        }

        public Task<HostBridgeResponse> SendAsync(HostBridgeRequest request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            return Task.FromResult(
                HostBridgeResponse.CreateFailure(
                    request == null ? string.Empty : request.RequestId,
                    HostBridgeErrorCodes.NotConnected,
                    "No host bridge connection has been configured yet."));
        }
    }
}
