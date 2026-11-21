using System;
using System.Collections.Generic;

namespace DataLifecycleEngine.Purger;

public class PurgeOptions
{
    public int BatchSize { get; set; } = 2000;
    public int ThrottlePauseMs { get; set; } = 1500;
    public double MaxTransactionLogPercent { get; set; } = 65.0;
    public bool DryRun { get; set; } = false;
    public bool Resume { get; set; } = false;
}

public class TablePurgeResult
{
    public string TableName { get; set; } = string.Empty;
    public long RowsPurged { get; set; }
    public string Status { get; set; } = "PENDING"; // COMPLETED, SKIPPED, PARTIAL, FAILED
    public TimeSpan Duration { get; set; }
}

public class PurgeRunResult
{
    public string PolicyName { get; set; } = string.Empty;
    public bool IsDryRun { get; set; }
    public List<TablePurgeResult> TableResults { get; set; } = new();
    public long TotalRowsPurged { get; set; }
    public TimeSpan TotalDuration { get; set; }
    public bool CompletedSuccessfully { get; set; }
    public string? ErrorMessage { get; set; }
}
