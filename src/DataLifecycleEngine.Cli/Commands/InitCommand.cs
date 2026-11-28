using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DataLifecycleEngine.Catalog;
using DataLifecycleEngine.Core.Graph;
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

    [CommandOption("-f|--force")]
    [Description("Overwrite existing configuration file without prompting.")]
    public bool Force { get; set; }
}

public class InitCommand : AsyncCommand<InitCommandSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, InitCommandSettings settings, CancellationToken cancellationToken)
    {
        if (File.Exists(settings.OutputPath) && !settings.Force)
        {
            bool overwrite = AnsiConsole.Confirm($"[yellow]File '{settings.OutputPath}' already exists. Overwrite?[/]", false);
            if (!overwrite)
            {
                AnsiConsole.MarkupLine("[grey]Setup cancelled.[/]");
                return 0;
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
        string cascadeYamlBlock = DefaultAccountsCascadeYaml;

        if (!settings.Quick)
        {
            AnsiConsole.Write(
                new FigletText("ProofPurge Setup")
                    .Color(Color.DeepSkyBlue1));

            AnsiConsole.Write(new Rule("[yellow]Interactive Step-by-Step Configuration Wizard[/]").LeftJustified());
            AnsiConsole.MarkupLine("[grey]Configure your database rules interactively below (press Enter to accept suggested defaults):\n[/]");

            driver = AnsiConsole.Prompt(
                new SelectionPrompt<string>()
                    .Title("[bold cyan]1. Select Target Database Engine:[/]")
                    .AddChoices("sqlserver", "postgres"));

            string defaultConnStr = driver == "sqlserver"
                ? "Server=.\\SQLEXPRESS;Database=LifecycleDemo;Trusted_Connection=True;TrustServerCertificate=True;"
                : "Host=localhost;Port=5432;Database=LifecycleDemo;Username=postgres;Password=password123;";

            connStr = AnsiConsole.Prompt(
                new TextPrompt<string>("[bold cyan]2. Enter Database Connection String (or ${ENV_VAR}):[/]")
                    .DefaultValue(defaultConnStr));

            policyName = AnsiConsole.Prompt(
                new TextPrompt<string>("[bold cyan]3. Policy Name (e.g. purge_inactive_accounts):[/]")
                    .DefaultValue("purge_inactive_records"));

            rootTable = AnsiConsole.Prompt(
                new TextPrompt<string>("[bold cyan]4. Root Target Table (Parent table to inspect & purge):[/]")
                    .DefaultValue("Accounts"));

            dateColumn = AnsiConsole.Prompt(
                new TextPrompt<string>("[bold cyan]5. Date/Timestamp Column to check for age:[/]")
                    .DefaultValue("LastActivityDate"));

            cutoff = AnsiConsole.Prompt(
                new TextPrompt<string>("[bold cyan]6. Retention Cutoff Window (e.g. 30d, 90d, 12m, 3y, 7y):[/]")
                    .DefaultValue("3y"));

            whereClause = AnsiConsole.Prompt(
                new TextPrompt<string>("[bold cyan]7. Filter Condition (WHERE clause, or press Enter for empty):[/]")
                    .AllowEmpty()
                    .DefaultValue("Status = 'CLOSED'"));

            action = AnsiConsole.Prompt(
                new SelectionPrompt<string>()
                    .Title("[bold cyan]8. Default Purge Action:[/]")
                    .AddChoices("archive_and_purge", "dry_run", "archive_only"));

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

            // 10. Live Foreign-Key Auto-Discovery
            bool canAutoDiscover = !connStr.Contains("${") &&
                AnsiConsole.Confirm("[bold cyan]10. Auto-discover foreign-key child tables from your live database?[/]", true);

            if (canAutoDiscover)
            {
                cascadeYamlBlock = await TryDiscoverCascadeYamlAsync(driver, connStr, rootTable, cancellationToken);
            }
            else if (!string.Equals(rootTable, "Accounts", StringComparison.OrdinalIgnoreCase))
            {
                cascadeYamlBlock = "    cascade_children: []\n";
            }
        }

        string safeConnStr = connStr.Contains('\\') ? connStr.Replace("\\", "\\\\") : connStr;

        string generatedYaml = $@"# ProofPurge Declarative Retention Policy Configuration
version: ""1.0""

target_database:
  driver: ""{driver}""
  connection_string: ""{safeConnStr}""

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
{cascadeYamlBlock}";

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
        AnsiConsole.MarkupLine($"[bold cyan]proofpurge scan -c {settings.OutputPath}[/]\n");

        return 0;
    }

    private static async Task<string> TryDiscoverCascadeYamlAsync(string driver, string connStr, string rootTable, CancellationToken ct)
    {
        try
        {
            ICatalogInspector inspector = driver.ToLowerInvariant() switch
            {
                "postgres" or "postgresql" or "npgsql" => new PostgresCatalogInspector(),
                _ => new SqlServerCatalogInspector()
            };

            List<ForeignKeyInfo> fks = new();
            await AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .StartAsync($"Inspecting foreign keys referencing '{rootTable}'...", async _ =>
                {
                    fks = await inspector.GetForeignKeysAsync(connStr, ct);
                });

            var sb = new StringBuilder();
            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { rootTable };
            AppendChildrenYaml(sb, rootTable, fks, visited, indentLevel: 2);

            if (sb.Length == 0)
            {
                AnsiConsole.MarkupLine($"[grey]No foreign-key child tables found referencing '{rootTable}'.[/]");
                return "    cascade_children: []\n";
            }

            AnsiConsole.MarkupLine($"[green]✓ Auto-discovered foreign-key hierarchy under '{rootTable}'.[/]");
            return "    cascade_children:\n" + sb.ToString();
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[yellow]⚠ Could not auto-discover foreign keys ({Markup.Escape(ex.Message)}). Using empty cascade list.[/]");
            return string.Equals(rootTable, "Accounts", StringComparison.OrdinalIgnoreCase)
                ? DefaultAccountsCascadeYaml
                : "    cascade_children: []\n";
        }
    }

    private static void AppendChildrenYaml(
        StringBuilder sb,
        string parentTable,
        List<ForeignKeyInfo> allFks,
        HashSet<string> visited,
        int indentLevel)
    {
        var children = allFks
            .Where(fk => string.Equals(fk.ParentTable, parentTable, StringComparison.OrdinalIgnoreCase)
                      && !visited.Contains(fk.ChildTable))
            .GroupBy(fk => fk.ChildTable, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToList();

        if (children.Count == 0) return;

        string indent = new string(' ', indentLevel * 2);
        string propIndent = new string(' ', (indentLevel + 1) * 2);

        foreach (var child in children)
        {
            visited.Add(child.ChildTable);
            sb.AppendLine($"{indent}- table: \"{child.ChildTable}\"");
            sb.AppendLine($"{propIndent}foreign_key: \"{child.ChildColumn}\"");

            var subBuilder = new StringBuilder();
            AppendChildrenYaml(subBuilder, child.ChildTable, allFks, visited, indentLevel + 2);
            if (subBuilder.Length > 0)
            {
                sb.AppendLine($"{propIndent}children:");
                sb.Append(subBuilder);
            }
        }
    }

    private const string DefaultAccountsCascadeYaml = @"    cascade_children:
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
}
