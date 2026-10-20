using System;
using System.Collections.Generic;
using System.Threading;

namespace OfficeCopilot.Agent.Abstractions
{
    public sealed class ToolExecutionContext
    {
        public ToolExecutionContext()
        {
            ConversationId = string.Empty;
            MessageId = string.Empty;
            HostApplicationName = string.Empty;
            Items = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            CancellationToken = CancellationToken.None;
        }

        public string ConversationId { get; set; }

        public string MessageId { get; set; }

        public string HostApplicationName { get; set; }

        public IServiceProvider Services { get; set; }

        public IDictionary<string, object> Items { get; set; }

        public CancellationToken CancellationToken { get; set; }

        public T GetService<T>() where T : class
        {
            if (Services == null)
            {
                return null;
            }

            return Services.GetService(typeof(T)) as T;
        }

        public bool TryGetItem<T>(string key, out T value)
        {
            value = default(T);

            if (string.IsNullOrWhiteSpace(key) || Items == null)
            {
                return false;
            }

            object rawValue;
            if (!Items.TryGetValue(key, out rawValue))
            {
                return false;
            }

            if (!(rawValue is T))
            {
                return false;
            }

            value = (T)rawValue;
            return true;
        }
    }
}