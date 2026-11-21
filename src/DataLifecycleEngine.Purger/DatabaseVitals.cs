using System;

namespace DataLifecycleEngine.Purger;

public class DatabaseVitals
{
    public double UsedLogSpacePercent { get; set; }
    public double TotalLogSizeMb { get; set; }
    public double UsedLogSizeMb { get; set; }
    public int ActiveTransactionsCount { get; set; }
    public DateTime CheckedAtUtc { get; set; } = DateTime.UtcNow;

    public bool IsThrottlingRecommended(double maxThresholdPercent)
    {
        return UsedLogSpacePercent >= maxThresholdPercent;
    }
}
