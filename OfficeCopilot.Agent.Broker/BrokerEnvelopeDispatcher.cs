using OfficeCopilot.Agent.Client;
using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace OfficeCopilot.Agent.Broker
{
    internal sealed class BrokerEnvelopeDispatcher : IBrokerEnvelopeDispatcher
    {
        private readonly BrokerTurnCancellationRegistry _cancellationRegistry;
        private readonly BrokerApplicationLifetime _lifetime;
        private readonly CancellationTokenSource _shutdownCancellationSource;
        private readonly IBrokerTurnService _turnService;
        private readonly BrokerPendingUserInteractionRegistry _userInteractionRegistry;

        public BrokerEnvelopeDispatcher(IBrokerTurnService turnService)
            : this(turnService, new BrokerTurnCancellationRegistry(), new BrokerApplicationLifetime(), null, null)
        {
        }

        internal BrokerEnvelopeDispatcher(
            IBrokerTurnService turnService,
            BrokerTurnCancellationRegistry cancellationRegistry,
            BrokerApplicationLifetime lifetime,
            CancellationTokenSource shutdownCancellationSource,
            BrokerPendingUserInteractionRegistry userInteractionRegistry = null)
        {
            _turnService = turnService ?? throw new ArgumentNullException(nameof(turnService));
            _cancellationRegistry = cancellationRegistry ?? throw new ArgumentNullException(nameof(cancellationRegistry));
            _lifetime = lifetime ?? throw new ArgumentNullException(nameof(lifetime));
            _shutdownCancellationSource = shutdownCancellationSource;
            _userInteractionRegistry = userInteractionRegistry;
        }

        public async Task<AgentBrokerResponseEnvelope> DispatchAsync(
            AgentBrokerRequestEnvelope request,
            IAgentBrokerEventSink eventSink,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (request == null)
            {
                return new AgentBrokerResponseEnvelope
                {
                    RequestId = string.Empty,
                    MessageType = AgentBrokerMessageTypes.Error,
                    Success = false,
                    ErrorCode = "broker.request.invalid",
                    ErrorMessage = "Broker request envelope is required."
                };
            }

            _lifetime.TouchActivity();

            try
            {
                if (string.Equals(request.MessageType, AgentBrokerMessageTypes.PingRequest, StringComparison.OrdinalIgnoreCase))
                {
                    return new AgentBrokerResponseEnvelope
                    {
                        RequestId = request.RequestId ?? string.Empty,
                        MessageType = AgentBrokerMessageTypes.PingResponse,
                        Success = true,
                        PayloadJson = "{}"
                    };
                }

                if (string.Equals(request.MessageType, AgentBrokerMessageTypes.ShutdownRequest, StringComparison.OrdinalIgnoreCase))
                {
                    return DispatchShutdown(request);
                }

                if (string.Equals(request.MessageType, AgentBrokerMessageTypes.CancelTurnRequest, StringComparison.OrdinalIgnoreCase))
                {
                    return DispatchCancelTurn(request);
                }

                if (string.Equals(request.MessageType, AgentBrokerMessageTypes.SeedConversationRequest, StringComparison.OrdinalIgnoreCase))
                {
                    return await DispatchSeedConversationAsync(request, cancellationToken).ConfigureAwait(false);
                }

                if (string.Equals(request.MessageType, AgentBrokerMessageTypes.ClearExecutionModePreferenceRequest, StringComparison.OrdinalIgnoreCase))
                {
                    return await DispatchClearExecutionModePreferenceAsync(request, cancellationToken).ConfigureAwait(false);
                }

                if (string.Equals(request.MessageType, AgentBrokerMessageTypes.ListConversationsRequest, StringComparison.OrdinalIgnoreCase))
                {
                    return await DispatchListConversationsAsync(request, cancellationToken).ConfigureAwait(false);
                }

                if (string.Equals(request.MessageType, AgentBrokerMessageTypes.LoadConversationRequest, StringComparison.OrdinalIgnoreCase))
                {
                    return await DispatchLoadConversationAsync(request, cancellationToken).ConfigureAwait(false);
                }

                if (string.Equals(request.MessageType, AgentBrokerMessageTypes.DeleteConversationRequest, StringComparison.OrdinalIgnoreCase))
                {
                    return await DispatchDeleteConversationAsync(request, cancellationToken).ConfigureAwait(false);
                }

                if (string.Equals(request.MessageType, AgentBrokerMessageTypes.CompleteRequest, StringComparison.OrdinalIgnoreCase))
                {
                    return await DispatchCompleteAsync(request, cancellationToken).ConfigureAwait(false);
                }

                // Phase 0: Handle Panel → Broker user interaction response
                if (string.Equals(request.MessageType, AgentBrokerMessageTypes.ToolUserInteractionResponse, StringComparison.OrdinalIgnoreCase))
                {
                    return DispatchUserInteractionResponse(request);
                }

                if (!string.Equals(request.MessageType, AgentBrokerMessageTypes.ExecuteTurnRequest, StringComparison.OrdinalIgnoreCase))
                {
                    return new AgentBrokerResponseEnvelope
                    {
                        RequestId = request.RequestId ?? string.Empty,
                        MessageType = AgentBrokerMessageTypes.Error,
                        Success = false,
                        ErrorCode = "broker.request.unsupported",
                        ErrorMessage = "Unsupported broker request message type."
                    };
                }

                return await DispatchExecuteTurnAsync(request, eventSink, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return new AgentBrokerResponseEnvelope
                {
                    RequestId = request.RequestId ?? string.Empty,
                    MessageType = AgentBrokerMessageTypes.Error,
                    Success = false,
                    ErrorCode = "broker.cancelled",
                    ErrorMessage = "The broker request was cancelled."
                };
            }
            catch (Exception ex)
            {
                // === 防崩溃: DispatchAsync 顶层异常兜底 ===
                // 任何未被下层捕获的异常在此被转换为错误响应，
                // 不会导致连接 task 崩溃。
                BrokerRuntimeLogger.Error("BrokerEnvelopeDispatcher", "DispatchAsync unhandled exception. MessageType=" + (request?.MessageType ?? "null"), ex);
                return new AgentBrokerResponseEnvelope
                {
                    RequestId = request.RequestId ?? string.Empty,
                    MessageType = AgentBrokerMessageTypes.Error,
                    Success = false,
                    ErrorCode = "broker.internal_error",
                    ErrorMessage = GetDetailedExceptionMessage(ex)
                };
            }
        }

        private async Task<AgentBrokerResponseEnvelope> DispatchCompleteAsync(
            AgentBrokerRequestEnvelope request,
            CancellationToken cancellationToken)
        {
            AgentBrokerCompletionRequest completionRequest;
            try
            {
                completionRequest = JsonSerializer.Deserialize<AgentBrokerCompletionRequest>(request.PayloadJson ?? "{}")
                    ?? new AgentBrokerCompletionRequest();
            }
            catch (JsonException ex)
            {
                return new AgentBrokerResponseEnvelope
                {
                    RequestId = request.RequestId ?? string.Empty,
                    MessageType = AgentBrokerMessageTypes.Error,
                    Success = false,
                    ErrorCode = "broker.request.invalid_payload",
                    ErrorMessage = ex.Message
                };
            }

            var result = await _turnService.CompleteAsync(completionRequest, cancellationToken).ConfigureAwait(false);
            return new AgentBrokerResponseEnvelope
            {
                RequestId = request.RequestId ?? string.Empty,
                MessageType = AgentBrokerMessageTypes.CompleteResponse,
                Success = result != null && result.Success,
                PayloadJson = JsonSerializer.Serialize(result ?? new AgentBrokerCompletionResult()),
                ErrorCode = result == null ? "broker.completion.failed" : result.ErrorCode ?? string.Empty,
                ErrorMessage = result == null ? "Broker completion returned no result." : result.ErrorMessage ?? string.Empty
            };
        }

        private async Task<AgentBrokerResponseEnvelope> DispatchExecuteTurnAsync(
            AgentBrokerRequestEnvelope request,
            IAgentBrokerEventSink eventSink,
            CancellationToken cancellationToken)
        {
            AgentBrokerTurnRequest turnRequest;
            try
            {
                turnRequest = JsonSerializer.Deserialize<AgentBrokerTurnRequest>(request.PayloadJson ?? "{}")
                    ?? new AgentBrokerTurnRequest();
            }
            catch (JsonException ex)
            {
                return new AgentBrokerResponseEnvelope
                {
                    RequestId = request.RequestId ?? string.Empty,
                    MessageType = AgentBrokerMessageTypes.Error,
                    Success = false,
                    ErrorCode = "broker.request.invalid_payload",
                    ErrorMessage = ex.Message
                };
            }

            using (var requestCancellationSource = new CancellationTokenSource())
            using (var linkedCancellationSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, requestCancellationSource.Token))
            {
                _cancellationRegistry.Register(request.RequestId, turnRequest, requestCancellationSource);

                try
                {
                    // === 防崩溃: Turn 执行超时保护 ===
                    // 默认单次 turn 最多执行 10 分钟。超时后自动取消，防止 HTTP 流式读取挂死。
                    const int defaultTurnTimeoutSeconds = 600; // 10 minutes
                    var turnTimeoutSeconds = defaultTurnTimeoutSeconds;
                    if (turnRequest.Metadata != null
                        && turnRequest.Metadata.TryGetValue("runtime.turnTimeoutSeconds", out var ttsStr)
                        && int.TryParse(ttsStr, out var parsed) && parsed > 0)
                    {
                        turnTimeoutSeconds = Math.Min(parsed, 3600); // max 1 hour
                    }

                    using (var turnTimeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(turnTimeoutSeconds)))
                    using (var turnLinkedCts = CancellationTokenSource.CreateLinkedTokenSource(
                        linkedCancellationSource.Token, turnTimeoutCts.Token))
                    {
                        var executeTask = _turnService.ExecuteAsync(turnRequest, eventSink, turnLinkedCts.Token);

                        // 使用 Task.WhenAny 实现超时，确保即使 ExecuteAsync 不响应 CancellationToken，
                        // 超时后也能返回错误而非永久挂起。
                        var timeoutTask = Task.Delay(TimeSpan.FromSeconds(turnTimeoutSeconds + 5), CancellationToken.None);
                        var completed = await Task.WhenAny(executeTask, timeoutTask).ConfigureAwait(false);

                        AgentBrokerTurnResult result;
                        if (completed == timeoutTask)
                        {
                            // === 防崩溃: Turn 超时未返回 ===
                            // 这表示 ExecuteAsync 即使被取消也没有返回（如 HTTP 流挂死）。
                            // 记录严重错误日志，返回超时错误给 Panel。
                            BrokerRuntimeLogger.Error("BrokerEnvelopeDispatcher",
                                $"Turn execution TIMED OUT after {turnTimeoutSeconds}s. ConversationId={turnRequest?.ConversationId ?? "null"}, " +
                                $"MessageId={turnRequest?.MessageId ?? "null"}. The turn task may still be running in background!");
                            result = new AgentBrokerTurnResult
                            {
                                ConversationId = turnRequest.ConversationId ?? string.Empty,
                                MessageId = turnRequest.MessageId ?? string.Empty,
                                Success = false,
                                FinishReason = "timeout",
                                ErrorCode = "broker.turn.timeout",
                                ErrorMessage = $"Turn execution timed out after {turnTimeoutSeconds} seconds."
                            };
                        }
                        else
                        {
                            result = await executeTask.ConfigureAwait(false);
                        }

                        return new AgentBrokerResponseEnvelope
                        {
                            RequestId = request.RequestId ?? string.Empty,
                            MessageType = AgentBrokerMessageTypes.ExecuteTurnResponse,
                            Success = result != null && result.Success,
                            PayloadJson = JsonSerializer.Serialize(result ?? new AgentBrokerTurnResult(), new JsonSerializerOptions { PropertyNamingPolicy = null }),
                            ErrorCode = result == null ? string.Empty : result.ErrorCode ?? string.Empty,
                            ErrorMessage = result == null ? string.Empty : result.ErrorMessage ?? string.Empty
                        };
                    }
                }
                catch (OperationCanceledException) when (requestCancellationSource.IsCancellationRequested)
                {
                    var cancelledResult = new AgentBrokerTurnResult
                    {
                        ConversationId = turnRequest.ConversationId ?? string.Empty,
                        MessageId = turnRequest.MessageId ?? string.Empty,
                        Success = false,
                        FinishReason = "cancelled",
                        ErrorCode = "broker.turn.cancelled",
                        ErrorMessage = "The broker turn was cancelled."
                    };

                    return new AgentBrokerResponseEnvelope
                    {
                        RequestId = request.RequestId ?? string.Empty,
                        MessageType = AgentBrokerMessageTypes.ExecuteTurnResponse,
                        Success = false,
                        PayloadJson = JsonSerializer.Serialize(cancelledResult),
                        ErrorCode = cancelledResult.ErrorCode,
                        ErrorMessage = cancelledResult.ErrorMessage
                    };
                }
                catch (Exception ex)
                {
                    BrokerRuntimeLogger.Error("BrokerEnvelopeDispatcher", "ExecuteTurn failed. ConversationId=" + (turnRequest?.ConversationId ?? "null"), ex);
                    var failedResult = new AgentBrokerTurnResult
                    {
                        ConversationId = turnRequest.ConversationId ?? string.Empty,
                        MessageId = turnRequest.MessageId ?? string.Empty,
                        Success = false,
                        FinishReason = "error",
                        ErrorCode = "broker.turn.failed",
                        ErrorMessage = GetDetailedExceptionMessage(ex)
                    };

                    return new AgentBrokerResponseEnvelope
                    {
                        RequestId = request.RequestId ?? string.Empty,
                        MessageType = AgentBrokerMessageTypes.ExecuteTurnResponse,
                        Success = false,
                        PayloadJson = JsonSerializer.Serialize(failedResult),
                        ErrorCode = failedResult.ErrorCode,
                        ErrorMessage = failedResult.ErrorMessage
                    };
                }
                finally
                {
                    _cancellationRegistry.Unregister(request.RequestId, turnRequest);
                }
            }
        }

        private AgentBrokerResponseEnvelope DispatchCancelTurn(AgentBrokerRequestEnvelope request)
        {
            AgentBrokerCancelTurnRequest cancelRequest;
            try
            {
                cancelRequest = JsonSerializer.Deserialize<AgentBrokerCancelTurnRequest>(request.PayloadJson ?? "{}")
                    ?? new AgentBrokerCancelTurnRequest();
            }
            catch (JsonException ex)
            {
                return new AgentBrokerResponseEnvelope
                {
                    RequestId = request.RequestId ?? string.Empty,
                    MessageType = AgentBrokerMessageTypes.Error,
                    Success = false,
                    ErrorCode = "broker.request.invalid_payload",
                    ErrorMessage = ex.Message
                };
            }

            var wasCancelled = _cancellationRegistry.Cancel(cancelRequest);
            var cancelResult = new AgentBrokerCancelTurnResult
            {
                ConversationId = cancelRequest.ConversationId ?? string.Empty,
                MessageId = cancelRequest.MessageId ?? string.Empty,
                Success = wasCancelled,
                WasCancelled = wasCancelled,
                ErrorCode = wasCancelled ? string.Empty : "broker.turn.not_found",
                ErrorMessage = wasCancelled
                    ? string.Empty
                    : "The specified broker turn is not active or could not be found."
            };

            return new AgentBrokerResponseEnvelope
            {
                RequestId = request.RequestId ?? string.Empty,
                MessageType = AgentBrokerMessageTypes.CancelTurnResponse,
                Success = cancelResult.Success,
                PayloadJson = JsonSerializer.Serialize(cancelResult),
                ErrorCode = cancelResult.ErrorCode,
                ErrorMessage = cancelResult.ErrorMessage
            };
        }

        private AgentBrokerResponseEnvelope DispatchShutdown(AgentBrokerRequestEnvelope request)
        {
            _lifetime.RequestStop("shutdown_request", _shutdownCancellationSource);

            return new AgentBrokerResponseEnvelope
            {
                RequestId = request.RequestId ?? string.Empty,
                MessageType = AgentBrokerMessageTypes.ShutdownResponse,
                Success = true,
                PayloadJson = "{}"
            };
        }

        private async Task<AgentBrokerResponseEnvelope> DispatchSeedConversationAsync(
            AgentBrokerRequestEnvelope request,
            CancellationToken cancellationToken)
        {
            AgentBrokerSeedConversationRequest seedRequest;
            try
            {
                seedRequest = JsonSerializer.Deserialize<AgentBrokerSeedConversationRequest>(request.PayloadJson ?? "{}")
                    ?? new AgentBrokerSeedConversationRequest();
            }
            catch (JsonException ex)
            {
                return new AgentBrokerResponseEnvelope
                {
                    RequestId = request.RequestId ?? string.Empty,
                    MessageType = AgentBrokerMessageTypes.Error,
                    Success = false,
                    ErrorCode = "broker.request.invalid_payload",
                    ErrorMessage = ex.Message
                };
            }

            try
            {
                var result = await _turnService.SeedConversationAsync(seedRequest, cancellationToken).ConfigureAwait(false)
                    ?? new AgentBrokerSeedConversationResult();
                return new AgentBrokerResponseEnvelope
                {
                    RequestId = request.RequestId ?? string.Empty,
                    MessageType = AgentBrokerMessageTypes.SeedConversationResponse,
                    Success = result.Success,
                    PayloadJson = JsonSerializer.Serialize(result),
                    ErrorCode = result.ErrorCode ?? string.Empty,
                    ErrorMessage = result.ErrorMessage ?? string.Empty
                };
            }
            catch (OperationCanceledException)
            {
                return new AgentBrokerResponseEnvelope
                {
                    RequestId = request.RequestId ?? string.Empty,
                    MessageType = AgentBrokerMessageTypes.SeedConversationResponse,
                    Success = false,
                    ErrorCode = "broker.conversation.seed.cancelled",
                    ErrorMessage = "The seed conversation operation was cancelled."
                };
            }
            catch (Exception ex)
            {
                return new AgentBrokerResponseEnvelope
                {
                    RequestId = request.RequestId ?? string.Empty,
                    MessageType = AgentBrokerMessageTypes.SeedConversationResponse,
                    Success = false,
                    ErrorCode = "broker.conversation.seed.failed",
                    ErrorMessage = GetDetailedExceptionMessage(ex)
                };
            }
        }

        private async Task<AgentBrokerResponseEnvelope> DispatchClearExecutionModePreferenceAsync(
            AgentBrokerRequestEnvelope request,
            CancellationToken cancellationToken)
        {
            AgentBrokerClearExecutionModePreferenceRequest clearRequest;
            try
            {
                clearRequest = JsonSerializer.Deserialize<AgentBrokerClearExecutionModePreferenceRequest>(
                    request.PayloadJson ?? "{}")
                    ?? new AgentBrokerClearExecutionModePreferenceRequest();
            }
            catch (JsonException ex)
            {
                return new AgentBrokerResponseEnvelope
                {
                    RequestId = request.RequestId ?? string.Empty,
                    MessageType = AgentBrokerMessageTypes.Error,
                    Success = false,
                    ErrorCode = "broker.request.invalid_payload",
                    ErrorMessage = ex.Message
                };
            }

            try
            {
                var result = await _turnService
                    .ClearExecutionModePreferenceAsync(clearRequest, cancellationToken)
                    .ConfigureAwait(false) ?? new AgentBrokerClearExecutionModePreferenceResult();
                return new AgentBrokerResponseEnvelope
                {
                    RequestId = request.RequestId ?? string.Empty,
                    MessageType = AgentBrokerMessageTypes.ClearExecutionModePreferenceResponse,
                    Success = result.Success,
                    PayloadJson = JsonSerializer.Serialize(result),
                    ErrorCode = result.ErrorCode ?? string.Empty,
                    ErrorMessage = result.ErrorMessage ?? string.Empty
                };
            }
            catch (OperationCanceledException)
            {
                return new AgentBrokerResponseEnvelope
                {
                    RequestId = request.RequestId ?? string.Empty,
                    MessageType = AgentBrokerMessageTypes.ClearExecutionModePreferenceResponse,
                    Success = false,
                    ErrorCode = "broker.execution_mode.clear.cancelled",
                    ErrorMessage = "Clearing the execution mode preference was cancelled."
                };
            }
            catch (Exception ex)
            {
                return new AgentBrokerResponseEnvelope
                {
                    RequestId = request.RequestId ?? string.Empty,
                    MessageType = AgentBrokerMessageTypes.ClearExecutionModePreferenceResponse,
                    Success = false,
                    ErrorCode = "broker.execution_mode.clear.failed",
                    ErrorMessage = GetDetailedExceptionMessage(ex)
                };
            }
        }

        private async Task<AgentBrokerResponseEnvelope> DispatchListConversationsAsync(
            AgentBrokerRequestEnvelope request,
            CancellationToken cancellationToken)
        {
            AgentBrokerListConversationsRequest listRequest;
            try
            {
                listRequest = JsonSerializer.Deserialize<AgentBrokerListConversationsRequest>(request.PayloadJson ?? "{}")
                    ?? new AgentBrokerListConversationsRequest();
            }
            catch (JsonException ex)
            {
                return new AgentBrokerResponseEnvelope
                {
                    RequestId = request.RequestId ?? string.Empty,
                    MessageType = AgentBrokerMessageTypes.Error,
                    Success = false,
                    ErrorCode = "broker.request.invalid_payload",
                    ErrorMessage = ex.Message
                };
            }

            try
            {
                var result = await _turnService.ListConversationsAsync(listRequest, cancellationToken).ConfigureAwait(false)
                    ?? new AgentBrokerListConversationsResult();
                return new AgentBrokerResponseEnvelope
                {
                    RequestId = request.RequestId ?? string.Empty,
                    MessageType = AgentBrokerMessageTypes.ListConversationsResponse,
                    Success = result.Success,
                    PayloadJson = JsonSerializer.Serialize(result),
                    ErrorCode = result.ErrorCode ?? string.Empty,
                    ErrorMessage = result.ErrorMessage ?? string.Empty
                };
            }
            catch (OperationCanceledException)
            {
                return new AgentBrokerResponseEnvelope
                {
                    RequestId = request.RequestId ?? string.Empty,
                    MessageType = AgentBrokerMessageTypes.ListConversationsResponse,
                    Success = false,
                    ErrorCode = "broker.conversation.list.cancelled",
                    ErrorMessage = "The list conversations operation was cancelled."
                };
            }
            catch (Exception ex)
            {
                return new AgentBrokerResponseEnvelope
                {
                    RequestId = request.RequestId ?? string.Empty,
                    MessageType = AgentBrokerMessageTypes.ListConversationsResponse,
                    Success = false,
                    ErrorCode = "broker.conversation.list.failed",
                    ErrorMessage = GetDetailedExceptionMessage(ex)
                };
            }
        }

        private async Task<AgentBrokerResponseEnvelope> DispatchLoadConversationAsync(
            AgentBrokerRequestEnvelope request,
            CancellationToken cancellationToken)
        {
            AgentBrokerLoadConversationRequest loadRequest;
            try
            {
                loadRequest = JsonSerializer.Deserialize<AgentBrokerLoadConversationRequest>(request.PayloadJson ?? "{}")
                    ?? new AgentBrokerLoadConversationRequest();
            }
            catch (JsonException ex)
            {
                return new AgentBrokerResponseEnvelope
                {
                    RequestId = request.RequestId ?? string.Empty,
                    MessageType = AgentBrokerMessageTypes.Error,
                    Success = false,
                    ErrorCode = "broker.request.invalid_payload",
                    ErrorMessage = ex.Message
                };
            }

            try
            {
                var result = await _turnService.LoadConversationAsync(loadRequest, cancellationToken).ConfigureAwait(false)
                    ?? new AgentBrokerLoadConversationResult();
                return new AgentBrokerResponseEnvelope
                {
                    RequestId = request.RequestId ?? string.Empty,
                    MessageType = AgentBrokerMessageTypes.LoadConversationResponse,
                    Success = result.Success,
                    PayloadJson = JsonSerializer.Serialize(result),
                    ErrorCode = result.ErrorCode ?? string.Empty,
                    ErrorMessage = result.ErrorMessage ?? string.Empty
                };
            }
            catch (OperationCanceledException)
            {
                return new AgentBrokerResponseEnvelope
                {
                    RequestId = request.RequestId ?? string.Empty,
                    MessageType = AgentBrokerMessageTypes.LoadConversationResponse,
                    Success = false,
                    ErrorCode = "broker.conversation.load.cancelled",
                    ErrorMessage = "The load conversation operation was cancelled."
                };
            }
            catch (Exception ex)
            {
                return new AgentBrokerResponseEnvelope
                {
                    RequestId = request.RequestId ?? string.Empty,
                    MessageType = AgentBrokerMessageTypes.LoadConversationResponse,
                    Success = false,
                    ErrorCode = "broker.conversation.load.failed",
                    ErrorMessage = GetDetailedExceptionMessage(ex)
                };
            }
        }

        private async Task<AgentBrokerResponseEnvelope> DispatchDeleteConversationAsync(
            AgentBrokerRequestEnvelope request,
            CancellationToken cancellationToken)
        {
            AgentBrokerDeleteConversationRequest deleteRequest;
            try
            {
                deleteRequest = JsonSerializer.Deserialize<AgentBrokerDeleteConversationRequest>(request.PayloadJson ?? "{}")
                    ?? new AgentBrokerDeleteConversationRequest();
            }
            catch (JsonException ex)
            {
                return new AgentBrokerResponseEnvelope
                {
                    RequestId = request.RequestId ?? string.Empty,
                    MessageType = AgentBrokerMessageTypes.Error,
                    Success = false,
                    ErrorCode = "broker.request.invalid_payload",
                    ErrorMessage = ex.Message
                };
            }

            try
            {
                var result = await _turnService.DeleteConversationAsync(deleteRequest, cancellationToken).ConfigureAwait(false)
                    ?? new AgentBrokerDeleteConversationResult();
                return new AgentBrokerResponseEnvelope
                {
                    RequestId = request.RequestId ?? string.Empty,
                    MessageType = AgentBrokerMessageTypes.DeleteConversationResponse,
                    Success = result.Success,
                    PayloadJson = JsonSerializer.Serialize(result),
                    ErrorCode = result.ErrorCode ?? string.Empty,
                    ErrorMessage = result.ErrorMessage ?? string.Empty
                };
            }
            catch (OperationCanceledException)
            {
                return new AgentBrokerResponseEnvelope
                {
                    RequestId = request.RequestId ?? string.Empty,
                    MessageType = AgentBrokerMessageTypes.DeleteConversationResponse,
                    Success = false,
                    ErrorCode = "broker.conversation.delete.cancelled",
                    ErrorMessage = "The delete conversation operation was cancelled."
                };
            }
            catch (Exception ex)
            {
                return new AgentBrokerResponseEnvelope
                {
                    RequestId = request.RequestId ?? string.Empty,
                    MessageType = AgentBrokerMessageTypes.DeleteConversationResponse,
                    Success = false,
                    ErrorCode = "broker.conversation.delete.failed",
                    ErrorMessage = GetDetailedExceptionMessage(ex)
                };
            }
        }

        private static string GetDetailedExceptionMessage(Exception ex)
        {
            var builder = new System.Text.StringBuilder();

            while (ex != null)
            {
                var message = (ex.Message ?? string.Empty).Trim();
                if (!string.IsNullOrWhiteSpace(message)
                    && builder.ToString().IndexOf(message, StringComparison.OrdinalIgnoreCase) < 0)
                {
                    if (builder.Length > 0)
                    {
                        builder.Append(" --> ");
                    }

                    builder.Append(message);
                }

                ex = ex.InnerException;
            }

            return builder.Length == 0
                ? "Broker turn failed."
                : builder.ToString();
        }

        /// <summary>
        /// Phase 0: 处理 Panel → Broker 的用户交互响应。
        /// 反序列化 PayloadJson 为 <see cref="AgentBrokerToolUserInteractionResponse"/>，
        /// 然后通过 <see cref="BrokerPendingUserInteractionRegistry.CompleteResponse"/> 完成等待中的 TCS。
        /// </summary>
        private AgentBrokerResponseEnvelope DispatchUserInteractionResponse(AgentBrokerRequestEnvelope request)
        {
            if (_userInteractionRegistry == null)
            {
                return new AgentBrokerResponseEnvelope
                {
                    RequestId = request.RequestId ?? string.Empty,
                    MessageType = AgentBrokerMessageTypes.Error,
                    Success = false,
                    ErrorCode = "user_interaction.not_configured",
                    ErrorMessage = "User interaction registry is not configured on this broker instance."
                };
            }

            AgentBrokerToolUserInteractionResponse response;
            try
            {
                response = JsonSerializer.Deserialize<AgentBrokerToolUserInteractionResponse>(request.PayloadJson ?? "{}")
                    ?? new AgentBrokerToolUserInteractionResponse();
            }
            catch (JsonException ex)
            {
                return new AgentBrokerResponseEnvelope
                {
                    RequestId = request.RequestId ?? string.Empty,
                    MessageType = AgentBrokerMessageTypes.Error,
                    Success = false,
                    ErrorCode = "user_interaction.invalid_payload",
                    ErrorMessage = $"Failed to deserialize user interaction response: {ex.Message}"
                };
            }

            var completed = _userInteractionRegistry.CompleteResponse(response);

            return new AgentBrokerResponseEnvelope
            {
                RequestId = request.RequestId ?? string.Empty,
                MessageType = AgentBrokerMessageTypes.ToolUserInteractionResponse,
                Success = completed,
                PayloadJson = completed ? "{}" : "{\"toolCallId\":\"" + (response.ToolCallId ?? string.Empty) + "\"}",
                ErrorCode = completed ? string.Empty : "user_interaction.not_found",
                ErrorMessage = completed
                    ? string.Empty
                    : $"No pending user interaction found for toolCallId '{response.ToolCallId ?? "null"}'. It may have already timed out."
            };
        }
    }
}
