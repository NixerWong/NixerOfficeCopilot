using OfficeCopilot.HostBridge.Contracts;
using System.Threading;
using System.Threading.Tasks;

namespace OfficeCopilot.Agent.Runtime
{
    public interface IHostContextReader
    {
        Task<HostContextSnapshotDto> GetSnapshotAsync(CancellationToken cancellationToken);
    }
}
