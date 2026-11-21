using System.Collections.Generic;
using YamlDotNet.Serialization;

namespace DataLifecycleEngine.Core.Config;

public class RetentionConfiguration
{
    [YamlMember(Alias = "version")]
    public string Version { get; set; } = "1.0";

    [YamlMember(Alias = "target_database")]
    public TargetDatabaseConfig TargetDatabase { get; set; } = new();

    [YamlMember(Alias = "storage_sink")]
    public StorageSinkConfig StorageSink { get; set; } = new();

    [YamlMember(Alias = "safety_thresholds")]
    public SafetyThresholdsConfig SafetyThresholds { get; set; } = new();

    [YamlMember(Alias = "policies")]
    public List<RetentionPolicy> Policies { get; set; } = new();
}

public class TargetDatabaseConfig
{
    [YamlMember(Alias = "driver")]
    public string Driver { get; set; } = "sqlserver"; // sqlserver | postgres

    [YamlMember(Alias = "connection_string")]
    public string ConnectionString { get; set; } = string.Empty;
}

public class StorageSinkConfig
{
    [YamlMember(Alias = "type")]
    public string Type { get; set; } = "local"; // local | s3 | azure

    [YamlMember(Alias = "local_path")]
    public string LocalPath { get; set; } = "./archives";

    [YamlMember(Alias = "bucket")]
    public string? Bucket { get; set; }

    [YamlMember(Alias = "prefix")]
    public string? Prefix { get; set; }

    [YamlMember(Alias = "region")]
    public string? Region { get; set; }

    [YamlMember(Alias = "compression")]
    public string Compression { get; set; } = "snappy"; // snappy | zstd | gzip
}

public class SafetyThresholdsConfig
{
    [YamlMember(Alias = "max_batch_size")]
    public int MaxBatchSize { get; set; } = 2000;

    [YamlMember(Alias = "throttle_pause_ms")]
    public int ThrottlePauseMs { get; set; } = 1500;

    [YamlMember(Alias = "max_transaction_log_percent")]
    public double MaxTransactionLogPercent { get; set; } = 65.0;

    [YamlMember(Alias = "max_replication_lag_seconds")]
    public int MaxReplicationLagSeconds { get; set; } = 3;
}

public class RetentionPolicy
{
    [YamlMember(Alias = "name")]
    public string Name { get; set; } = string.Empty;

    [YamlMember(Alias = "description")]
    public string? Description { get; set; }

    [YamlMember(Alias = "root_table")]
    public string RootTable { get; set; } = string.Empty;

    [YamlMember(Alias = "retention_cutoff")]
    public string RetentionCutoff { get; set; } = "3y"; // e.g. 3y, 90d, 12m

    [YamlMember(Alias = "date_column")]
    public string DateColumn { get; set; } = string.Empty;

    [YamlMember(Alias = "where_clause")]
    public string? WhereClause { get; set; }

    [YamlMember(Alias = "action")]
    public string Action { get; set; } = "dry_run"; // dry_run | archive_only | archive_and_purge

    [YamlMember(Alias = "cascade_children")]
    public List<CascadeChild> CascadeChildren { get; set; } = new();
}

public class CascadeChild
{
    [YamlMember(Alias = "table")]
    public string Table { get; set; } = string.Empty;

    [YamlMember(Alias = "foreign_key")]
    public string ForeignKey { get; set; } = string.Empty;

    [YamlMember(Alias = "children")]
    public List<CascadeChild> Children { get; set; } = new();
}

