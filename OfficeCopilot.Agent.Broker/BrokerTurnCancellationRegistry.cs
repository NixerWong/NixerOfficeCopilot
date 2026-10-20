using OfficeCopilot.Agent.Client;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;

namespace OfficeCopilot.Agent.Broker
{
    internal sealed class BrokerTurnCancellationRegistry
    {
        private readonly ConcurrentDictionary<string, CancellationRegistration> _registrations;

        public BrokerTurnCancellationRegistry()
        {
            _registrations = new ConcurrentDictionary<string, CancellationRegistration>(StringComparer.OrdinalIgnoreCase);
        }

        public void Register(string brokerRequestId, AgentBrokerTurnRequest request, CancellationTokenSource cancellationTokenSource)
        {
            if (cancellationTokenSource == null)
            {
                throw new ArgumentNullException(nameof(cancellationTokenSource));
            }

            foreach (var key in GetKeys(brokerRequestId, request))
            {
                _registrations[key] = new CancellationRegistration(cancellationTokenSource);
            }
        }

        public bool Cancel(AgentBrokerCancelTurnRequest request)
        {
            if (request == null)
            {
                return false;
            }

            foreach (var key in GetKeys(request.TargetRequestId, request.ConversationId, request.MessageId, request.SessionId))
            {
                CancellationRegistration registration;
                if (_registrations.TryGetValue(key, out registration) && registration != null)
                {
                    registration.Cancel();
                    return true;
                }
            }

            return false;
        }

        public void Unregister(string brokerRequestId, AgentBrokerTurnRequest request)
        {
            foreach (var key in GetKeys(brokerRequestId, request))
            {
                CancellationRegistration ignored;
                _registrations.TryRemove(key, out ignored);
            }
        }

        private static IEnumerable<string> GetKeys(string brokerRequestId, AgentBrokerTurnRequest request)
        {
            return GetKeys(
                brokerRequestId,
                request == null ? null : request.ConversationId,
                request == null ? null : request.MessageId,
                request == null ? null : request.SessionId);
        }

        private static IEnumerable<string> GetKeys(
            string brokerRequestId,
            string conversationId,
            string messageId,
            string sessionId)
        {
            var yielded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            TryYield(CreateRequestKey(brokerRequestId), yielded, out var requestKey);
            if (requestKey != null)
            {
                yield return requestKey;
            }

            TryYield(CreateConversationMessageKey(conversationId, messageId), yielded, out var conversationMessageKey);
            if (conversationMessageKey != null)
            {
                yield return conversationMessageKey;
            }

            TryYield(CreateSessionMessageKey(sessionId, messageId), yielded, out var sessionMessageKey);
            if (sessionMessageKey != null)
            {
                yield return sessionMessageKey;
            }

            TryYield(CreateMessageKey(messageId), yielded, out var messageKey);
            if (messageKey != null)
            {
                yield return messageKey;
            }
        }

        private static void TryYield(string key, ISet<string> yielded, out string value)
        {
            value = null;
            if (string.IsNullOrWhiteSpace(key) || yielded == null)
            {
                return;
            }

            if (yielded.Add(key))
            {
                value = key;
            }
        }

        private static string CreateRequestKey(string brokerRequestId)
        {
            return string.IsNullOrWhiteSpace(brokerRequestId)
                ? null
                : "request:" + brokerRequestId.Trim();
        }

        private static string CreateConversationMessageKey(string conversationId, string messageId)
        {
            if (string.IsNullOrWhiteSpace(conversationId) || string.IsNullOrWhiteSpace(messageId))
            {
                return null;
            }

            return "conversation:" + conversationId.Trim() + ":" + messageId.Trim();
        }

        private static string CreateSessionMessageKey(string sessionId, string messageId)
        {
            if (string.IsNullOrWhiteSpace(sessionId) || string.IsNullOrWhiteSpace(messageId))
            {
                return null;
            }

            return "session:" + sessionId.Trim() + ":" + messageId.Trim();
        }

        private static string CreateMessageKey(string messageId)
        {
            return string.IsNullOrWhiteSpace(messageId)
                ? null
                : "message:" + messageId.Trim();
        }

        private sealed class CancellationRegistration
        {
            private readonly CancellationTokenSource _cancellationTokenSource;

            public CancellationRegistration(CancellationTokenSource cancellationTokenSource)
            {
                _cancellationTokenSource = cancellationTokenSource;
            }

            public void Cancel()
            {
                if (_cancellationTokenSource == null || _cancellationTokenSource.IsCancellationRequested)
                {
                    return;
                }

                try
                {
                    _cancellationTokenSource.Cancel();
                }
                catch (ObjectDisposedException)
                {
                }
            }
        }
    }
}
