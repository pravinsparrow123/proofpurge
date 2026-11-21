using System;
using System.Text.RegularExpressions;

namespace DataLifecycleEngine.Core;

public static class DateHelper
{
    public static DateTime CalculateCutoffDate(string cutoff)
    {
        if (string.IsNullOrWhiteSpace(cutoff))
            return DateTime.UtcNow.AddYears(-3);

        var match = Regex.Match(cutoff.Trim().ToLowerInvariant(), @"^(\d+)([dmy])$");
        if (!match.Success)
            throw new ArgumentException($"Invalid retention cutoff format: '{cutoff}'. Expected format like '3y', '90d', '12m'.");

        int value = int.Parse(match.Groups[1].Value);
        string unit = match.Groups[2].Value;

        return unit switch
        {
            "d" => DateTime.UtcNow.AddDays(-value),
            "m" => DateTime.UtcNow.AddMonths(-value),
            "y" => DateTime.UtcNow.AddYears(-value),
            _ => throw new ArgumentException($"Unsupported unit '{unit}'")
        };
    }
}

