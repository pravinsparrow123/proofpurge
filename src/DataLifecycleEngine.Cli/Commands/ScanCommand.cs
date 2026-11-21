using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using DataLifecycleEngine.Catalog;
using DataLifecycleEngine.Core.Config;
using DataLifecycleEngine.Core.Graph;
using Spectre.Console;
using Spectre.Console.Cli;

namespace DataLifecycleEngine.Cli.Commands;

public class ScanCommandSettings : CommandSettings
{
    [CommandOption("-c|--config <PATH>")]
    public string ConfigPath { get; set; } = "configs/retention-policy.example.yaml";
}

public class ScanCommand : AsyncCommand<ScanCommandSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, ScanCommandSettings settings, System.Threading.CancellationToken cancellationToken)
    {
        AnsiConsole.Write(
            new FigletText("Safe Purge")
                .Color(Color.Cyan1));

        AnsiConsole.MarkupLine("[bold green]Enterprise Data Lifecycle & Safe Purge Engine[/]");
        AnsiConsole.MarkupLine($"[grey]Loading retention configuration: {settings.ConfigPath}[/]\n");

        if (!File.Exists(settings.ConfigPath))
        {
            AnsiConsole.MarkupLine($"[red]Error: Configuration file not found at '{settings.ConfigPath}'[/]");
            return 1;
        }

        RetentionConfiguration config;
        try
        {
            config = PolicyLoader.LoadFromFile(settings.ConfigPath);
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Error parsing configuration: {ex.Message}[/]");
            return 1;
        }

        AnsiConsole.MarkupLine($"[bold]Target Driver:[/] [yellow]{config.TargetDatabase.Driver}[/]");
        AnsiConsole.MarkupLine($"[bold]Policies Defined:[/] [yellow]{config.Policies.Count}[/]\n");

        ICatalogInspector inspector = config.TargetDatabase.Driver.ToLowerInvariant() switch
        {
            "postgres" or "postgresql" or "npgsql" => new PostgresCatalogInspector(),
            _ => new SqlServerCatalogInspector()
        };

        await AnsiConsole.Status()
            .Spinner(Spinner.Known.Dots)
            .StartAsync("Connecting to database & scanning foreign-key catalog...", async ctx =>
            {
                ctx.Status("Reading database catalog & schema...");
                var tables = await inspector.GetTablesAsync(config.TargetDatabase.ConnectionString);
                var fks = await inspector.GetForeignKeysAsync(config.TargetDatabase.ConnectionString);

                AnsiConsole.MarkupLine($"[green]✓[/] Discovered [bold]{tables.Count}[/] user tables and [bold]{fks.Count}[/] foreign key relationships.");

                foreach (var policy in config.Policies)
                {
                    AnsiConsole.WriteLine();
                    var rule = new Rule($"[yellow]Policy: {policy.Name}[/]").LeftJustified();
                    AnsiConsole.Write(rule);

                    // Collect all tables involved in the policy
                    var involvedTables = CollectInvolvedTables(policy);
                    var plan = TopologicalSorter.ComputeDeletionOrder(involvedTables, fks);

                    // Display Topological Deletion Order
                    AnsiConsole.MarkupLine("[bold underline]Computed Safe Deletion Order (Leaf to Root):[/]");
                    var tree = new Tree("[bold blue]Purge Pipeline[/]");
                    for (int i = 0; i < plan.DeletionOrder.Count; i++)
                    {
                        string tableName = plan.DeletionOrder[i];
                        string stepLabel = i == plan.DeletionOrder.Count - 1
                            ? $"[bold red]Step {i + 1}: {tableName} (Root Parent)[/]"
                            : $"[green]Step {i + 1}: {tableName} (Child Leaf)[/]";

                        tree.AddNode(stepLabel);
                    }
                    AnsiConsole.Write(tree);
                    AnsiConsole.WriteLine();

                    // Display Table Bloat & Candidate Counts
                    var tableGrid = new Table();
                    tableGrid.Border(TableBorder.Rounded);
                    tableGrid.AddColumn("[bold]Table[/]");
                    tableGrid.AddColumn("[bold]Est. Rows[/]");
                    tableGrid.AddColumn("[bold]Size (MB)[/]");
                    tableGrid.AddColumn("[bold]Candidate Purge Rows[/]");

                    DateTime cutoffDate = DataLifecycleEngine.Core.DateHelper.CalculateCutoffDate(policy.RetentionCutoff);
                    string rootWhere = $"[{policy.DateColumn}] < @Cutoff";
                    if (!string.IsNullOrWhiteSpace(policy.WhereClause))
                    {
                        rootWhere += $" AND ({policy.WhereClause})";
                    }

                    foreach (var tblName in plan.DeletionOrder)
                    {
                        var meta = tables.FirstOrDefault(t => string.Equals(t.TableName, tblName, StringComparison.OrdinalIgnoreCase));
                        long estRows = meta?.EstimatedRowCount ?? 0;
                        double sizeMb = (meta?.TotalSizeBytes ?? 0) / (1024.0 * 1024.0);

                        long candidateCount = 0;
                        try
                        {
                            string countSql = DataLifecycleEngine.Purger.AdaptiveCascadePurger.BuildDryRunCountQuery(tblName, policy, rootWhere);
                            await using var conn = new Microsoft.Data.SqlClient.SqlConnection(config.TargetDatabase.ConnectionString);
                            await conn.OpenAsync();
                            await using var cmd = new Microsoft.Data.SqlClient.SqlCommand(countSql, conn);
                            cmd.Parameters.Add(new Microsoft.Data.SqlClient.SqlParameter("@Cutoff", System.Data.SqlDbType.DateTime2) { Value = cutoffDate });
                            var resultObj = await cmd.ExecuteScalarAsync();
                            candidateCount = Convert.ToInt64(resultObj ?? 0);
                        }
                        catch
                        {
                            candidateCount = -1;
                        }

                        tableGrid.AddRow(
                            $"[bold]{tblName}[/]",
                            estRows.ToString("N0"),
                            sizeMb.ToString("F2"),
                            candidateCount >= 0 ? candidateCount.ToString("N0") : "[red]Error counting[/]"
                        );
                    }

                    AnsiConsole.Write(tableGrid);

                    // Display Missing Index Warnings
                    if (plan.MissingIndexWarnings.Count > 0)
                    {
                        AnsiConsole.WriteLine();
                        var warnPanel = new Panel(string.Join("\n", plan.MissingIndexWarnings.Select(w => $"[yellow]⚠ {w}[/]")))
                        {
                            Header = new PanelHeader("[bold yellow]Performance Warnings[/]"),
                            Border = BoxBorder.Rounded
                        };
                        AnsiConsole.Write(warnPanel);
                    }

                    // Display Cycle Warnings
                    if (plan.HasCycles)
                    {
                        AnsiConsole.WriteLine();
                        var errPanel = new Panel(string.Join("\n", plan.CyclicDependencies.Select(c => $"[red]✖ {c}[/]")))
                        {
                            Header = new PanelHeader("[bold red]Circular Dependencies Detected[/]"),
                            Border = BoxBorder.Heavy
                        };
                        AnsiConsole.Write(errPanel);
                    }
                }
            });

        return 0;
    }

    private static System.Collections.Generic.HashSet<string> CollectInvolvedTables(RetentionPolicy policy)
    {
        var set = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            policy.RootTable
        };

        void Traverse(System.Collections.Generic.IEnumerable<CascadeChild> children)
        {
            foreach (var child in children)
            {
                set.Add(child.Table);
                if (child.Children.Count > 0)
                    Traverse(child.Children);
            }
        }

        Traverse(policy.CascadeChildren);
        return set;
    }
}
