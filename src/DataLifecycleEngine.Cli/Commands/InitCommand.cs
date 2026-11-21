using System.ComponentModel;
using System.IO;
using System.Threading.Tasks;
using Spectre.Console;
using Spectre.Console.Cli;

namespace DataLifecycleEngine.Cli.Commands;

public class InitCommandSettings : CommandSettings
{
    [CommandOption("-o|--output <PATH>")]
    [Description("Destination file path for the starter policy YAML configuration.")]
    public string OutputPath { get; set; } = "retention-policy.yaml";
}

public class InitCommand : AsyncCommand<InitCommandSettings>
{
    public override Task<int> ExecuteAsync(CommandContext context, InitCommandSettings settings)
    {
        if (File.Exists(settings.OutputPath))
        {
            AnsiConsole.MarkupLine($"[yellow]Notice: Configuration file '{settings.OutputPath}' already exists.[/]");
            return Task.FromResult(0);
        }

        const string sampleYaml = @"# ProofPurge Declarative Retention Policy Configuration
version: ""1.0""

# Target Database Connection
target_database:
  driver: ""sqlserver"" # options: 'sqlserver' or 'postgres'
  connection_string: ""${DB_CONNECTION_STRING}""

# Cold Storage Sink for Parquet Archives
storage_sink:
  type: ""local"" # options: 'local', 's3', or 'azure'
  local_path: ""./archives""
  compression: ""snappy"" # options: 'snappy', 'zstd', or 'gzip'

# Zero-Lock Safety Guardrails
safety_thresholds:
  max_batch_size: 2000
  throttle_pause_ms: 1000
  max_transaction_log_percent: 65.0
  max_replication_lag_seconds: 3

# Data Lifecycle & Cascade Retention Policies
policies:
  - name: ""purge_historical_data""
    description: ""Cold-archive and purge historical data exceeding retention cutoff""
    root_table: ""Accounts""
    retention_cutoff: ""3y"" # e.g. 3y (3 years), 90d (90 days), 12m (12 months)
    date_column: ""CreatedAt""
    where_clause: ""Status = 'CLOSED'""
    action: ""archive_and_purge"" # options: dry_run | archive_only | archive_and_purge

    # Cascade foreign key child tables to delete prior to root parent
    cascade_children:
      - table: ""Orders""
        foreign_key: ""AccountId""
        children:
          - table: ""OrderItems""
            foreign_key: ""OrderId""
          - table: ""Shipments""
            foreign_key: ""OrderId""
      - table: ""AuditLogs""
        foreign_key: ""AccountId""
";

        File.WriteAllText(settings.OutputPath, sampleYaml);
        AnsiConsole.MarkupLine($"[bold green]✓ Created starter configuration:[/] [cyan]{Path.GetFullPath(settings.OutputPath)}[/]");
        AnsiConsole.MarkupLine("[grey]Next step: Run 'proofpurge scan -c retention-policy.yaml' to inspect your database schema safely.[/]");
        return Task.FromResult(0);
    }
}
