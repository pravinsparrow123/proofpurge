using System.Threading;
using System.Threading.Tasks;

namespace DataLifecycleEngine.Purger;

public interface IDatabaseVitalsMonitor
{
    Task<DatabaseVitals> GetVitalsAsync(string connectionString, CancellationToken ct = default);
}
