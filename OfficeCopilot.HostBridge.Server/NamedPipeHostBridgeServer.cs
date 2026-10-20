using OfficeCopilot.HostBridge.Contracts;
using Newtonsoft.Json;
using System;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace OfficeCopilot.HostBridge.Server
{
    public sealed class NamedPipeHostBridgeServer : IHostBridgeServer
    {
        private readonly IHostBridgeRequestHandler _requestHandler;
        private bool _started;

        public NamedPipeHostBridgeServer(
            HostBridgeServerOptions options,
            IHostBridgeRequestHandler requestHandler)
        {
            Options = options ?? new HostBridgeServerOptions();
            _requestHandler = requestHandler ?? throw new ArgumentNullException(nameof(requestHandler));

            HostBridgeRuntimeLogger.Initialize();
            HostBridgeRuntimeLogger.Info("NamedPipeHostBridgeServer",
                $"Server created. PipeName={ResolvePipeName()} Handler={_requestHandler.GetType().Name}");
        }

        public HostBridgeServerOptions Options { get; private set; }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _started = true;

            HostBridgeRuntimeLogger.Info("NamedPipeHostBridgeServer",
                $"StartAsync: entering listen loop. PipeName={ResolvePipeName()}");

            while (!cancellationToken.IsCancellationRequested && _started)
            {
                try
                {
                    using (var server = CreateServer())
                    {
                        HostBridgeRuntimeLogger.Info("NamedPipeHostBridgeServer",
                            $"Waiting for client connection. PipeName={ResolvePipeName()}");
                        await server.WaitForConnectionAsync(cancellationToken).ConfigureAwait(false);
                        HostBridgeRuntimeLogger.Info("NamedPipeHostBridgeServer",
                            "Client connected. Processing request...");
                        await ProcessConnectionAsync(server, cancellationToken).ConfigureAwait(false);
                        HostBridgeRuntimeLogger.Info("NamedPipeHostBridgeServer",
                            "Request processed. Waiting for next connection...");
                    }
                }
                catch (OperationCanceledException)
                {
                    HostBridgeRuntimeLogger.Info("NamedPipeHostBridgeServer",
                        "StartAsync: cancellation requested, exiting listen loop.");
                    break;
                }
                catch (Exception ex)
                {
                    HostBridgeRuntimeLogger.Error("NamedPipeHostBridgeServer",
                        "StartAsync: unexpected error in listen loop. Will retry.", ex);
                }
            }

            HostBridgeRuntimeLogger.Info("NamedPipeHostBridgeServer",
                "StartAsync: listen loop exited. Started=" + _started);
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            HostBridgeRuntimeLogger.Info("NamedPipeHostBridgeServer", "StopAsync called.");
            _started = false;
            return Task.CompletedTask;
        }

        public void Dispose()
        {
            HostBridgeRuntimeLogger.Info("NamedPipeHostBridgeServer", "Dispose called.");
            _started = false;
        }

        private NamedPipeServerStream CreateServer()
        {
            return new NamedPipeServerStream(
                ResolvePipeName(),
                PipeDirection.InOut,
                1,
                PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous);
        }

        private async Task ProcessConnectionAsync(NamedPipeServerStream server, CancellationToken cancellationToken)
        {
            HostBridgeResponse response;

            try
            {
                using (var reader = new StreamReader(server, Encoding.UTF8, false, 1024, true))
                using (var writer = new StreamWriter(server, new UTF8Encoding(false), 1024, true))
                {
                    writer.AutoFlush = true;

                    var line = await reader.ReadLineAsync().ConfigureAwait(false);

                    if (string.IsNullOrWhiteSpace(line))
                    {
                        HostBridgeRuntimeLogger.Warning("NamedPipeHostBridgeServer",
                            "ProcessConnectionAsync: received empty request.");
                        response = HostBridgeResponse.CreateFailure(
                            string.Empty,
                            HostBridgeErrorCodes.EmptyRequest,
                            "The client sent an empty host-bridge request.");
                    }
                    else
                    {
                        try
                        {
                            var request = JsonConvert.DeserializeObject<HostBridgeRequest>(line)
                                ?? new HostBridgeRequest();

                            HostBridgeRuntimeLogger.Info("NamedPipeHostBridgeServer",
                                $"ProcessConnectionAsync: handling request. Operation={request.Operation} RequestId={request.RequestId}");

                            response = await _requestHandler.HandleAsync(request, cancellationToken).ConfigureAwait(false)
                                ?? HostBridgeResponse.CreateFailure(
                                    request.RequestId,
                                    HostBridgeErrorCodes.NullResponse,
                                    "The host bridge request handler returned a null response.");

                            HostBridgeRuntimeLogger.Info("NamedPipeHostBridgeServer",
                                $"ProcessConnectionAsync: request handled. Success={response.Success} Operation={request.Operation}");
                        }
                        catch (JsonException ex)
                        {
                            HostBridgeRuntimeLogger.Error("NamedPipeHostBridgeServer",
                                "ProcessConnectionAsync: invalid JSON request.", ex);
                            response = HostBridgeResponse.CreateFailure(
                                string.Empty,
                                HostBridgeErrorCodes.InvalidRequest,
                                ex.Message);
                        }
                        catch (Exception ex)
                        {
                            HostBridgeRuntimeLogger.Error("NamedPipeHostBridgeServer",
                                "ProcessConnectionAsync: handler threw exception.", ex);
                            response = HostBridgeResponse.CreateFailure(
                                string.Empty,
                                HostBridgeErrorCodes.HandlerError,
                                ex.Message);
                        }
                    }

                    var payload = JsonConvert.SerializeObject(response ?? new HostBridgeResponse());
                    await writer.WriteLineAsync(payload).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                HostBridgeRuntimeLogger.Error("NamedPipeHostBridgeServer",
                    "ProcessConnectionAsync: pipe I/O error.", ex);
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
