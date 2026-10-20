using System;
using System.Threading;
using System.Threading.Tasks;

namespace OfficeCopilot.Agent.Broker
{
    internal interface INamedPipeBrokerHost : IDisposable
    {
        NamedPipeBrokerHostOptions Options { get; }

        Task StartAsync(CancellationToken cancellationToken);

        Task StopAsync(CancellationToken cancellationToken);
    }
}
