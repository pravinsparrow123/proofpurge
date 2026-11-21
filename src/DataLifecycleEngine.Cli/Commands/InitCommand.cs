using System.ComponentModel;
using System.IO;
using System.Threading.Tasks;
using Spectre.Console;
using Spectre.Console.Cli;

namespace DataLifecycleEngine.Cli.Commands;

public class InitCommandSettings : CommandSettings
{
    [CommandOption("-o|--output <PATH>")]
    [Description("Destination file path for the retention policy YAML configuration.")]
    public string OutputPath { get; set; } = "retention-policy.yaml";

    [CommandOption("-q|--quick")]
    [Description("Quick non-interactive mode: creates default placeholder YAML without prompts.")]
    public bool Quick { get; set; }
}

public class InitCommand : AsyncCommand<InitCommandSettings>
{
    public override Task<int> ExecuteAsync(CommandContext context, InitCommandSettings settings)
    {
        if (File.Exists(settings.OutputPath))
        {
            bool overwrite = AnsiConsole.Confirm($"[yellow]File '{settings.OutputPath}' already exists. Overwrite?[/]", false);
            if (!overwrite)
            {
                AnsiConsole.MarkupLine("[grey]Setup cancelled.[/]");
                return Task.FromResult(0);
            }
        }

        string driver = "sqlserver";
        string connStr = "Server=.\\SQLEXPRESS;Database=LifecycleDemo;Trusted_Connection=True;TrustServerCertificate=True;";
        string policyName = "purge_inactive_records";
        string rootTable = "Accounts";
        string dateColumn = "LastActivityDate";
        string cutoff = "3y";
        string whereClause = "Status = 'CLOSED'";
        string action = "archive_and_purge";
        string sinkType = "local";
        string sinkPath = "./archives";

        if (!settings.Quick)
        {
            AnsiConsole.Write(
                new FigletText("ProofPurge Setup")
                    .Color(Color.DeepSkyBlue1));

            AnsiConsole.Write(new Rule("[yellow]Interactive Step-by-Step Configuration Wizard[/]").LeftJustified());
            AnsiConsole.MarkupLine("[grey]Configure your database rules interactively below (press Enter to accept suggested defaults):\n[/]");

            // 1. Driver
            driver = AnsiConsole.Prompt(
                new SelectionPrompt<string>()
                    .Title("[bold cyan]1. Select Target Database Engine:[/]")
                    .AddChoices("sqlserver", "postgres"));

            // 2. Connection String
            string defaultConnStr = driver == "sqlserver"
                ? "Server=.\\SQLEXPRESS;Database=LifecycleDemo;Trusted_Connection=True;TrustServerCertificate=True;"
                : "Host=localhost;Port=5432;Database=LifecycleDemo;Username=postgres;Password=password123;";

            connStr = AnsiConsole.Prompt(
                new TextPrompt<string>("[bold cyan]2. Enter Database Connection String (or ${ENV_VAR}):[/]")
                    .DefaultValue(defaultConnStr));

            // 3. Policy Name
            policyName = AnsiConsole.Prompt(
                new TextPrompt<string>("[bold cyan]3. Policy Name (e.g. purge_inactive_accounts):[/]")
                    .DefaultValue("purge_inactive_records"));

            // 4. Root Table
            rootTable = AnsiConsole.Prompt(
                new TextPrompt<string>("[bold cyan]4. Root Target Table (Parent table to inspect & purge):[/]")
                    .DefaultValue("Accounts"));

            // 5. Date Column
            dateColumn = AnsiConsole.Prompt(
                new TextPrompt<string>("[bold cyan]5. Date/Timestamp Column to check for age:[/]")
                    .DefaultValue("LastActivityDate"));

            // 6. Cutoff
            cutoff = AnsiConsole.Prompt(
                new TextPrompt<string>("[bold cyan]6. Retention Cutoff Window (e.g. 30d, 90d, 12m, 3y, 7y):[/]")
                    .DefaultValue("3y"));

            // 7. Filter Predicate
            whereClause = AnsiConsole.Prompt(
                new TextPrompt<string>("[bold cyan]7. Filter Condition (WHERE clause, or press Enter for empty):[/]")
                    .AllowEmpty()
                    .DefaultValue("Status = 'CLOSED'"));

            // 8. Action
            action = AnsiConsole.Prompt(
                new SelectionPrompt<string>()
                    .Title("[bold cyan]8. Default Purge Action:[/]")
                    .AddChoices("archive_and_purge", "dry_run", "archive_only"));

            // 9. Storage Sink
            sinkType = AnsiConsole.Prompt(
                new SelectionPrompt<string>()
                    .Title("[bold cyan]9. Cold Storage Sink for Parquet Files:[/]")
                    .AddChoices("local", "s3", "azure"));

            if (sinkType == "local")
            {
                sinkPath = AnsiConsole.Prompt(
                    new TextPrompt<string>("[bold cyan]   Local Directory Path for Parquet Archives:[/]")
                        .DefaultValue("./archives"));
            }
        }

        string generatedYaml = $@"# ProofPurge Declarative Retention Policy Configuration
version: ""1.0""

target_database:
  driver: ""{driver}""
  connection_string: ""{connStr}""

storage_sink:
  type: ""{sinkType}""
  local_path: ""{sinkPath}""
  compression: ""snappy""

safety_thresholds:
  max_batch_size: 2000
  throttle_pause_ms: 1000
  max_transaction_log_percent: 65.0
  max_replication_lag_seconds: 3

policies:
  - name: ""{policyName}""
    description: ""Cold-archive and purge records matching retention cutoff""
    root_table: ""{rootTable}""
    retention_cutoff: ""{cutoff}""
    date_column: ""{dateColumn}""
    where_clause: ""{whereClause}""
    action: ""{action}""
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

        File.WriteAllText(settings.OutputPath, generatedYaml);

        AnsiConsole.WriteLine();
        var panel = new Panel(
            $"[bold green]✓ Configuration Saved:[/] [cyan]{Path.GetFullPath(settings.OutputPath)}[/]\n\n" +
            $"[bold]Database Engine:[/] {driver}\n" +
            $"[bold]Target Table:[/] {rootTable} ({dateColumn} < {cutoff})\n" +
            $"[bold]Filter Condition:[/] {(string.IsNullOrWhiteSpace(whereClause) ? "[dim]None (All records older than cutoff)[/]" : whereClause)}\n" +
            $"[bold]Action:[/] {action}\n" +
            $"[bold]Cold Sink:[/] {sinkType} ({sinkPath})"
        )
        {
            Header = new PanelHeader("[bold green]Setup Complete[/]"),
            Border = BoxBorder.Rounded
        };
        AnsiConsole.Write(panel);

        AnsiConsole.MarkupLine($"\n[bold yellow]Next Step:[/] Run the non-destructive safety scan to inspect your live database:");
        AnsiConsole.MarkupLine($"[bold cyan]dotnet run --project src/DataLifecycleEngine.Cli -- scan -c {settings.OutputPath}[/]\n");

        return Task.FromResult(0);
    }
}
