using Newtonsoft.Json;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace OfficeCopilot.Agent.Client
{
    public sealed class TransportAgentBrokerClient : IAgentBrokerClient
    {
        private readonly IAgentBrokerTransport _transport;

        public TransportAgentBrokerClient(
            AgentBrokerConnectionOptions options,
            IAgentBrokerTransport transport)
        {
            Options = options ?? new AgentBrokerConnectionOptions();
            _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        }

        public AgentBrokerConnectionOptions Options { get; private set; }

        public async Task<AgentBrokerCompletionResult> CompleteAsync(
            AgentBrokerCompletionRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            request = request ?? new AgentBrokerCompletionRequest();

            var envelope = new AgentBrokerRequestEnvelope
            {
                MessageType = AgentBrokerMessageTypes.CompleteRequest,
                PayloadJson = JsonConvert.SerializeObject(request)
            };
            envelope.Metadata["clientId"] = Options.ClientId ?? string.Empty;
            var response = await _transport.SendAsync(envelope, null, cancellationToken).ConfigureAwait(false);
            if (response == null)
            {
                return new AgentBrokerCompletionResult { Success = false, ErrorCode = "broker.transport.no_response", ErrorMessage = "The broker transport did not return a response." };
            }

            if (!response.Success)
            {
                return new AgentBrokerCompletionResult { Success = false, ErrorCode = response.ErrorCode ?? string.Empty, ErrorMessage = response.ErrorMessage ?? string.Empty };
            }

            try
            {
                return JsonConvert.DeserializeObject<AgentBrokerCompletionResult>(response.PayloadJson ?? "{}")
                    ?? new AgentBrokerCompletionResult { Success = false, ErrorCode = "broker.transport.invalid_payload" };
            }
            catch (JsonException ex)
            {
                return new AgentBrokerCompletionResult { Success = false, ErrorCode = "broker.transport.invalid_payload", ErrorMessage = ex.Message };
            }
        }

        public async Task<AgentBrokerTurnResult> SendTurnAsync(
            AgentBrokerTurnRequest request,
            IAgentBrokerEventSink eventSink,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            request = request ?? new AgentBrokerTurnRequest();

            var envelope = new AgentBrokerRequestEnvelope
            {
                MessageType = AgentBrokerMessageTypes.ExecuteTurnRequest,
                PayloadJson = JsonConvert.SerializeObject(request)
            };

            string brokerRequestId;
            if (request.Metadata != null
                && request.Metadata.TryGetValue("brokerRequestId", out brokerRequestId)
                && !string.IsNullOrWhiteSpace(brokerRequestId))
            {
                envelope.RequestId = brokerRequestId;
            }

            envelope.Metadata["clientId"] = Options.ClientId ?? string.Empty;
            envelope.Metadata["sessionId"] = request.SessionId ?? Options.SessionId ?? string.Empty;

            var response = await _transport.SendAsync(envelope, eventSink, cancellationToken).ConfigureAwait(false);
            if (response == null)
            {
                return new AgentBrokerTurnResult
                {
                    ConversationId = request.ConversationId ?? string.Empty,
                    MessageId = request.MessageId ?? string.Empty,
                    Success = false,
                    ErrorCode = "broker.transport.no_response",
                    ErrorMessage = "The broker transport did not return a response."
                };
            }

            if (!response.Success)
            {
                return new AgentBrokerTurnResult
                {
                    ConversationId = request.ConversationId ?? string.Empty,
                    MessageId = request.MessageId ?? string.Empty,
                    Success = false,
                    ErrorCode = response.ErrorCode ?? string.Empty,
                    ErrorMessage = response.ErrorMessage ?? string.Empty
                };
            }

            try
            {
                var result = JsonConvert.DeserializeObject<AgentBrokerTurnResult>(response.PayloadJson ?? "{}")
                    ?? new AgentBrokerTurnResult();
                return result;
            }
            catch (JsonException ex)
            {
                return new AgentBrokerTurnResult
                {
                    ConversationId = request.ConversationId ?? string.Empty,
                    MessageId = request.MessageId ?? string.Empty,
                    Success = false,
                    ErrorCode = "broker.transport.invalid_payload",
                    ErrorMessage = ex.Message
                };
            }
        }

        public async Task<AgentBrokerCancelTurnResult> CancelTurnAsync(
            AgentBrokerCancelTurnRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            request = request ?? new AgentBrokerCancelTurnRequest();

            var envelope = new AgentBrokerRequestEnvelope
            {
                MessageType = AgentBrokerMessageTypes.CancelTurnRequest,
                PayloadJson = JsonConvert.SerializeObject(request)
            };

            envelope.Metadata["clientId"] = Options.ClientId ?? string.Empty;
            envelope.Metadata["sessionId"] = request.SessionId ?? Options.SessionId ?? string.Empty;

            var response = await _transport.SendAsync(envelope, null, cancellationToken).ConfigureAwait(false);
            if (response == null)
            {
                return new AgentBrokerCancelTurnResult
                {
                    ConversationId = request.ConversationId ?? string.Empty,
                    MessageId = request.MessageId ?? string.Empty,
                    Success = false,
                    WasCancelled = false,
                    ErrorCode = "broker.transport.no_response",
                    ErrorMessage = "The broker transport did not return a response."
                };
            }

            if (!response.Success)
            {
                return new AgentBrokerCancelTurnResult
                {
                    ConversationId = request.ConversationId ?? string.Empty,
                    MessageId = request.MessageId ?? string.Empty,
                    Success = false,
                    WasCancelled = false,
                    ErrorCode = response.ErrorCode ?? string.Empty,
                    ErrorMessage = response.ErrorMessage ?? string.Empty
                };
            }

            try
            {
                return JsonConvert.DeserializeObject<AgentBrokerCancelTurnResult>(response.PayloadJson ?? "{}")
                    ?? new AgentBrokerCancelTurnResult();
            }
            catch (JsonException ex)
            {
                return new AgentBrokerCancelTurnResult
                {
                    ConversationId = request.ConversationId ?? string.Empty,
                    MessageId = request.MessageId ?? string.Empty,
                    Success = false,
                    WasCancelled = false,
                    ErrorCode = "broker.transport.invalid_payload",
                    ErrorMessage = ex.Message
                };
            }
        }

        public async Task<AgentBrokerSeedConversationResult> SeedConversationAsync(
            AgentBrokerSeedConversationRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            request = request ?? new AgentBrokerSeedConversationRequest();

            var envelope = new AgentBrokerRequestEnvelope
            {
                MessageType = AgentBrokerMessageTypes.SeedConversationRequest,
                PayloadJson = JsonConvert.SerializeObject(request)
            };

            envelope.Metadata["clientId"] = Options.ClientId ?? string.Empty;
            envelope.Metadata["sessionId"] = Options.SessionId ?? string.Empty;

            var response = await _transport.SendAsync(envelope, null, cancellationToken).ConfigureAwait(false);
            if (response == null)
            {
                return new AgentBrokerSeedConversationResult
                {
                    ConversationId = request.ConversationId ?? string.Empty,
                    Success = false,
                    ErrorCode = "broker.transport.no_response",
                    ErrorMessage = "The broker transport did not return a response."
                };
            }

            if (!response.Success)
            {
                return new AgentBrokerSeedConversationResult
                {
                    ConversationId = request.ConversationId ?? string.Empty,
                    Success = false,
                    ErrorCode = response.ErrorCode ?? string.Empty,
                    ErrorMessage = response.ErrorMessage ?? string.Empty
                };
            }

            try
            {
                return JsonConvert.DeserializeObject<AgentBrokerSeedConversationResult>(response.PayloadJson ?? "{}")
                    ?? new AgentBrokerSeedConversationResult();
            }
            catch (JsonException ex)
            {
                return new AgentBrokerSeedConversationResult
                {
                    ConversationId = request.ConversationId ?? string.Empty,
                    Success = false,
                    ErrorCode = "broker.transport.invalid_payload",
                    ErrorMessage = ex.Message
                };
            }
        }

        public async Task<AgentBrokerClearExecutionModePreferenceResult> ClearExecutionModePreferenceAsync(
            AgentBrokerClearExecutionModePreferenceRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            request = request ?? new AgentBrokerClearExecutionModePreferenceRequest();

            var envelope = new AgentBrokerRequestEnvelope
            {
                MessageType = AgentBrokerMessageTypes.ClearExecutionModePreferenceRequest,
                PayloadJson = JsonConvert.SerializeObject(request)
            };
            envelope.Metadata["clientId"] = Options.ClientId ?? string.Empty;
            envelope.Metadata["sessionId"] = Options.SessionId ?? string.Empty;

            var response = await _transport.SendAsync(envelope, null, cancellationToken).ConfigureAwait(false);
            if (response == null)
            {
                return new AgentBrokerClearExecutionModePreferenceResult
                {
                    ConversationId = request.ConversationId ?? string.Empty,
                    Success = false,
                    ErrorCode = "broker.transport.no_response",
                    ErrorMessage = "The broker transport did not return a response."
                };
            }

            if (!response.Success)
            {
                return new AgentBrokerClearExecutionModePreferenceResult
                {
                    ConversationId = request.ConversationId ?? string.Empty,
                    Success = false,
                    ErrorCode = response.ErrorCode ?? string.Empty,
                    ErrorMessage = response.ErrorMessage ?? string.Empty
                };
            }

            try
            {
                return JsonConvert.DeserializeObject<AgentBrokerClearExecutionModePreferenceResult>(
                    response.PayloadJson ?? "{}")
                    ?? new AgentBrokerClearExecutionModePreferenceResult();
            }
            catch (JsonException ex)
            {
                return new AgentBrokerClearExecutionModePreferenceResult
                {
                    ConversationId = request.ConversationId ?? string.Empty,
                    Success = false,
                    ErrorCode = "broker.transport.invalid_payload",
                    ErrorMessage = ex.Message
                };
            }
        }

        public async Task<AgentBrokerListConversationsResult> ListConversationsAsync(
            AgentBrokerListConversationsRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            request = request ?? new AgentBrokerListConversationsRequest();

            var envelope = new AgentBrokerRequestEnvelope
            {
                MessageType = AgentBrokerMessageTypes.ListConversationsRequest,
                PayloadJson = JsonConvert.SerializeObject(request)
            };

            envelope.Metadata["clientId"] = Options.ClientId ?? string.Empty;
            envelope.Metadata["sessionId"] = Options.SessionId ?? string.Empty;

            var response = await _transport.SendAsync(envelope, null, cancellationToken).ConfigureAwait(false);
            if (response == null)
            {
                return new AgentBrokerListConversationsResult
                {
                    Success = false,
                    ErrorCode = "broker.transport.no_response",
                    ErrorMessage = "The broker transport did not return a response."
                };
            }

            if (!response.Success)
            {
                return new AgentBrokerListConversationsResult
                {
                    Success = false,
                    ErrorCode = response.ErrorCode ?? string.Empty,
                    ErrorMessage = response.ErrorMessage ?? string.Empty
                };
            }

            try
            {
                return JsonConvert.DeserializeObject<AgentBrokerListConversationsResult>(response.PayloadJson ?? "{}")
                    ?? new AgentBrokerListConversationsResult();
            }
            catch (JsonException ex)
            {
                return new AgentBrokerListConversationsResult
                {
                    Success = false,
                    ErrorCode = "broker.transport.invalid_payload",
                    ErrorMessage = ex.Message
                };
            }
        }

        public async Task<AgentBrokerLoadConversationResult> LoadConversationAsync(
            AgentBrokerLoadConversationRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            request = request ?? new AgentBrokerLoadConversationRequest();

            var envelope = new AgentBrokerRequestEnvelope
            {
                MessageType = AgentBrokerMessageTypes.LoadConversationRequest,
                PayloadJson = JsonConvert.SerializeObject(request)
            };

            envelope.Metadata["clientId"] = Options.ClientId ?? string.Empty;
            envelope.Metadata["sessionId"] = Options.SessionId ?? string.Empty;

            var response = await _transport.SendAsync(envelope, null, cancellationToken).ConfigureAwait(false);
            if (response == null)
            {
                return new AgentBrokerLoadConversationResult
                {
                    ConversationId = request.ConversationId ?? string.Empty,
                    Success = false,
                    ErrorCode = "broker.transport.no_response",
                    ErrorMessage = "The broker transport did not return a response."
                };
            }

            if (!response.Success)
            {
                return new AgentBrokerLoadConversationResult
                {
                    ConversationId = request.ConversationId ?? string.Empty,
                    Success = false,
                    ErrorCode = response.ErrorCode ?? string.Empty,
                    ErrorMessage = response.ErrorMessage ?? string.Empty
                };
            }

            try
            {
                return JsonConvert.DeserializeObject<AgentBrokerLoadConversationResult>(response.PayloadJson ?? "{}")
                    ?? new AgentBrokerLoadConversationResult();
            }
            catch (JsonException ex)
            {
                return new AgentBrokerLoadConversationResult
                {
                    ConversationId = request.ConversationId ?? string.Empty,
                    Success = false,
                    ErrorCode = "broker.transport.invalid_payload",
                    ErrorMessage = ex.Message
                };
            }
        }

        public async Task<AgentBrokerDeleteConversationResult> DeleteConversationAsync(
            AgentBrokerDeleteConversationRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            request = request ?? new AgentBrokerDeleteConversationRequest();

            var envelope = new AgentBrokerRequestEnvelope
            {
                MessageType = AgentBrokerMessageTypes.DeleteConversationRequest,
                PayloadJson = JsonConvert.SerializeObject(request)
            };

            envelope.Metadata["clientId"] = Options.ClientId ?? string.Empty;
            envelope.Metadata["sessionId"] = Options.SessionId ?? string.Empty;

            var response = await _transport.SendAsync(envelope, null, cancellationToken).ConfigureAwait(false);
            if (response == null)
            {
                return new AgentBrokerDeleteConversationResult
                {
                    ConversationId = request.ConversationId ?? string.Empty,
                    Success = false,
                    ErrorCode = "broker.transport.no_response",
                    ErrorMessage = "The broker transport did not return a response."
                };
            }

            if (!response.Success)
            {
                return new AgentBrokerDeleteConversationResult
                {
                    ConversationId = request.ConversationId ?? string.Empty,
                    Success = false,
                    ErrorCode = response.ErrorCode ?? string.Empty,
                    ErrorMessage = response.ErrorMessage ?? string.Empty
                };
            }

            try
            {
                return JsonConvert.DeserializeObject<AgentBrokerDeleteConversationResult>(response.PayloadJson ?? "{}")
                    ?? new AgentBrokerDeleteConversationResult();
            }
            catch (JsonException ex)
            {
                return new AgentBrokerDeleteConversationResult
                {
                    ConversationId = request.ConversationId ?? string.Empty,
                    Success = false,
                    ErrorCode = "broker.transport.invalid_payload",
                    ErrorMessage = ex.Message
                };
            }
        }

        /// <summary>
        /// Phase 1: Panel 向 Broker 发送用户交互响应。
        /// 发送 <see cref="AgentBrokerMessageTypes.ToolUserInteractionResponse"/> 消息，
        /// Broker 端 <see cref="BrokerEnvelopeDispatcher"/> 会路由到等待中的 TCS。
        /// </summary>
        public async Task<AgentBrokerToolUserInteractionResponse> SendUserInteractionResponseAsync(
            AgentBrokerToolUserInteractionResponse response,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            response = response ?? new AgentBrokerToolUserInteractionResponse();

            var envelope = new AgentBrokerRequestEnvelope
            {
                MessageType = AgentBrokerMessageTypes.ToolUserInteractionResponse,
                PayloadJson = JsonConvert.SerializeObject(response)
            };

            envelope.Metadata["clientId"] = Options.ClientId ?? string.Empty;
            envelope.Metadata["sessionId"] = Options.SessionId ?? string.Empty;

            var brokerResponse = await _transport.SendAsync(envelope, null, cancellationToken).ConfigureAwait(false);
            if (brokerResponse == null)
            {
                return new AgentBrokerToolUserInteractionResponse
                {
                    ToolCallId = response.ToolCallId ?? string.Empty,
                    Cancelled = true,
                    ErrorCode = "panel.transport.no_response",
                    ErrorMessage = "The broker transport did not return a response."
                };
            }

            if (!brokerResponse.Success)
            {
                return new AgentBrokerToolUserInteractionResponse
                {
                    ToolCallId = response.ToolCallId ?? string.Empty,
                    Cancelled = true,
                    ErrorCode = brokerResponse.ErrorCode ?? string.Empty,
                    ErrorMessage = brokerResponse.ErrorMessage ?? string.Empty
                };
            }

            return response; // Broker confirmed receipt
        }
    }
}
