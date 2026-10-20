using OfficeCopilot.HostBridge.Contracts;
using Newtonsoft.Json;
using System;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace OfficeCopilot.HostBridge.Client
{
    public sealed class NamedPipeHostBridgeClient : IHostBridgeClient
    {
        public NamedPipeHostBridgeClient()
            : this(new HostBridgeClientOptions())
        {
        }

        public NamedPipeHostBridgeClient(HostBridgeClientOptions options)
        {
            Options = options ?? new HostBridgeClientOptions();
        }

        public HostBridgeClientOptions Options { get; private set; }

        public async Task<HostBridgeCapabilities> GetCapabilitiesAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var response = await SendAsync(
                new HostBridgeRequest
                {
                    Operation = HostBridgeOperationNames.GetCapabilities,
                    SessionId = Options.SessionId ?? string.Empty,
                    PayloadJson = "{}"
                },
                cancellationToken).ConfigureAwait(false);

            if (response == null || !response.Success)
            {
                return new HostBridgeCapabilities();
            }

            try
            {
                return JsonConvert.DeserializeObject<HostBridgeCapabilities>(response.PayloadJson ?? "{}")
                    ?? new HostBridgeCapabilities();
            }
            catch (JsonException)
            {
                return new HostBridgeCapabilities();
            }
        }

        public async Task<HostBridgeResponse> SendAsync(HostBridgeRequest request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            request = request ?? new HostBridgeRequest();

            using (var pipe = new NamedPipeClientStream(
                ".",
                ResolvePipeName(),
                PipeDirection.InOut,
                PipeOptions.Asynchronous))
            {
                await Task.Run(
                    () => pipe.Connect(Math.Max(1, Options.HandshakeTimeoutMilliseconds)),
                    cancellationToken).ConfigureAwait(false);

                using (var writer = new StreamWriter(pipe, new UTF8Encoding(false), 1024, true))
                using (var reader = new StreamReader(pipe, Encoding.UTF8, false, 1024, true))
                {
                    writer.AutoFlush = true;
                    pipe.ReadMode = PipeTransmissionMode.Byte;

                    var payload = JsonConvert.SerializeObject(request);
                    await writer.WriteLineAsync(payload).ConfigureAwait(false);

                    var responseLine = await reader.ReadLineAsync().ConfigureAwait(false);
                    if (string.IsNullOrWhiteSpace(responseLine))
                    {
                        return HostBridgeResponse.CreateFailure(
                            request.RequestId,
                            HostBridgeErrorCodes.EmptyResponse,
                            "The host bridge returned an empty response.");
                    }

                    try
                    {
                        return JsonConvert.DeserializeObject<HostBridgeResponse>(responseLine)
                            ?? HostBridgeResponse.CreateFailure(
                                request.RequestId,
                                HostBridgeErrorCodes.NullResponse,
                                "The host bridge returned a null response envelope.");
                    }
                    catch (JsonException ex)
                    {
                        return HostBridgeResponse.CreateFailure(
                            request.RequestId,
                            HostBridgeErrorCodes.InvalidResponse,
                            ex.Message);
                    }
                }
            }
        }

        private string ResolvePipeName()
        {
            return string.IsNullOrWhiteSpace(Options.Endpoint)
                ? "officecopilot-host-bridge"
                : Options.Endpoint.Trim();
        }
    }
}
