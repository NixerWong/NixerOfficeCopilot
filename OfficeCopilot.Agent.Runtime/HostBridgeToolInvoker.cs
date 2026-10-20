using OfficeCopilot.HostBridge.Client;
using OfficeCopilot.HostBridge.Contracts;
using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace OfficeCopilot.Agent.Runtime
{
    public sealed class HostBridgeToolInvoker : IHostToolInvoker, IHostToolPreflightInvoker
    {
        private readonly IHostBridgeClient _hostBridgeClient;

        public HostBridgeToolInvoker(IHostBridgeClient hostBridgeClient)
        {
            _hostBridgeClient = hostBridgeClient ?? throw new ArgumentNullException(nameof(hostBridgeClient));
        }

        public async Task<HostToolInvocationResult> InvokeAsync(
            HostToolInvocationRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var bridgeRequest = new HostBridgeRequest
            {
                Operation = HostBridgeOperationNames.InvokeTool,
                PayloadJson = JsonSerializer.Serialize(request ?? new HostToolInvocationRequest()),
                SessionId = _hostBridgeClient.Options == null ? string.Empty : _hostBridgeClient.Options.SessionId ?? string.Empty
            };

            var bridgeResponse = await _hostBridgeClient.SendAsync(bridgeRequest, cancellationToken).ConfigureAwait(false);
            if (bridgeResponse == null)
            {
                return new HostToolInvocationResult
                {
                    Success = false,
                    ErrorCode = HostBridgeErrorCodes.NoResponse,
                    ErrorMessage = "The host bridge did not return a response."
                };
            }

            if (!bridgeResponse.Success)
            {
                return new HostToolInvocationResult
                {
                    ToolCallId = request == null ? string.Empty : request.ToolCallId ?? string.Empty,
                    ToolName = request == null ? string.Empty : request.ToolName ?? string.Empty,
                    Success = false,
                    ErrorCode = bridgeResponse.ErrorCode ?? string.Empty,
                    ErrorMessage = bridgeResponse.ErrorMessage ?? string.Empty
                };
            }

            try
            {
                var result = JsonSerializer.Deserialize<HostToolInvocationResult>(bridgeResponse.PayloadJson ?? "{}")
                    ?? new HostToolInvocationResult();

                if (string.IsNullOrWhiteSpace(result.ToolCallId) && request != null)
                {
                    result.ToolCallId = request.ToolCallId ?? string.Empty;
                }

                if (string.IsNullOrWhiteSpace(result.ToolName) && request != null)
                {
                    result.ToolName = request.ToolName ?? string.Empty;
                }

                return result;
            }
            catch (JsonException ex)
            {
                return new HostToolInvocationResult
                {
                    ToolCallId = request == null ? string.Empty : request.ToolCallId ?? string.Empty,
                    ToolName = request == null ? string.Empty : request.ToolName ?? string.Empty,
                    Success = false,
                    ErrorCode = HostBridgeErrorCodes.InvalidPayload,
                    ErrorMessage = ex.Message
                };
            }
        }

        public async Task<PreflightToolCallResult> PreflightToolCallAsync(
            PreflightToolCallRequest request,
            CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var bridgeRequest = new HostBridgeRequest
                {
                    Operation = HostBridgeOperationNames.PreflightToolCall,
                    PayloadJson = JsonSerializer.Serialize(request ?? new PreflightToolCallRequest()),
                    SessionId = _hostBridgeClient.Options == null
                        ? string.Empty
                        : _hostBridgeClient.Options.SessionId ?? string.Empty
                };
                var bridgeResponse = await _hostBridgeClient.SendAsync(bridgeRequest, cancellationToken).ConfigureAwait(false);
                if (bridgeResponse == null)
                {
                    return new PreflightToolCallResult
                    {
                        ErrorCode = HostBridgeErrorCodes.NoResponse,
                        ErrorMessage = "The host bridge did not return a preflight response."
                    };
                }

                if (!bridgeResponse.Success)
                {
                    return new PreflightToolCallResult
                    {
                        ErrorCode = bridgeResponse.ErrorCode ?? string.Empty,
                        ErrorMessage = bridgeResponse.ErrorMessage ?? string.Empty
                    };
                }

                try
                {
                    return JsonSerializer.Deserialize<PreflightToolCallResult>(bridgeResponse.PayloadJson ?? "{}")
                        ?? new PreflightToolCallResult
                        {
                            ErrorCode = HostBridgeErrorCodes.InvalidPayload,
                            ErrorMessage = "The host returned an empty preflight response."
                        };
                }
                catch (JsonException ex)
                {
                    return new PreflightToolCallResult
                    {
                        ErrorCode = HostBridgeErrorCodes.InvalidPayload,
                        ErrorMessage = ex.Message
                    };
                }
        }
    }
}
