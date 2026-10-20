using System;
using System.Threading;

namespace OfficeCopilot.Agent.Broker
{
    internal sealed class BrokerApplicationLifetime
    {
        private readonly object _syncRoot;
        private string _stopReason;

        public BrokerApplicationLifetime()
        {
            _syncRoot = new object();
            _stopReason = string.Empty;
            LastActivityUtc = DateTime.UtcNow;
        }

        public DateTime LastActivityUtc { get; private set; }

        public string StopReason
        {
            get
            {
                lock (_syncRoot)
                {
                    return _stopReason;
                }
            }
        }

        public bool IsStopRequested { get; private set; }

        public void TouchActivity()
        {
            LastActivityUtc = DateTime.UtcNow;
        }

        public bool RequestStop(string reason, CancellationTokenSource cancellationSource)
        {
            lock (_syncRoot)
            {
                if (IsStopRequested)
                {
                    return false;
                }

                IsStopRequested = true;
                _stopReason = reason ?? string.Empty;
            }

            if (cancellationSource != null)
            {
                try
                {
                    cancellationSource.Cancel();
                }
                catch
                {
                }
            }

            return true;
        }
    }
}
