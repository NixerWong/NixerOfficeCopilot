using OfficeCopilot.HostBridge.Client;
using OfficeCopilot.HostBridge.Contracts;
using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace OfficeCopilot.Agent.Runtime
{
    public sealed class HostBridgeContextReader : IHostContextReader
    {
        private readonly IHostBridgeClient _hostBridgeClient;

        public HostBridgeContextReader(IHostBridgeClient hostBridgeClient)
        {
            _hostBridgeClient = hostBridgeClient ?? throw new ArgumentNullException(nameof(hostBridgeClient));
        }

        public async Task<HostContextSnapshotDto> GetSnapshotAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var request = new HostBridgeRequest
            {
                Operation = HostBridgeOperationNames.GetContextSnapshot,
                PayloadJson = "{}",
                SessionId = _hostBridgeClient.Options == null ? string.Empty : _hostBridgeClient.Options.SessionId ?? string.Empty
            };

            var response = await _hostBridgeClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (response == null || !response.Success)
            {
                return new HostContextSnapshotDto();
            }

            try
            {
                return JsonSerializer.Deserialize<HostContextSnapshotDto>(response.PayloadJson ?? "{}")
                    ?? new HostContextSnapshotDto();
            }
            catch (JsonException)
            {
                return new HostContextSnapshotDto();
            }
        }
    }
}
