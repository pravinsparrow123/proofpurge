using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace DataLifecycleEngine.Purger;

public class TablePurgeState
{
    public string TableName { get; set; } = string.Empty;
    public long RowsDeleted { get; set; }
    public string Status { get; set; } = "PENDING"; // PENDING, IN_PROGRESS, COMPLETED, FAILED
    public DateTime LastUpdatedUtc { get; set; } = DateTime.UtcNow;
}

public interface ICheckpointStore
{
    Task InitializeAsync(CancellationToken ct = default);
    Task SaveProgressAsync(string policyName, string tableName, long rowsDeleted, string status, CancellationToken ct = default);
    Task<Dictionary<string, TablePurgeState>> GetProgressAsync(string policyName, CancellationToken ct = default);
    Task ClearPolicyProgressAsync(string policyName, CancellationToken ct = default);
}
