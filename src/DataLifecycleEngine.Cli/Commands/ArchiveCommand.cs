using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using DataLifecycleEngine.Archiver;
using DataLifecycleEngine.Archiver.Storage;
using DataLifecycleEngine.Catalog;
using DataLifecycleEngine.Core.Config;
using DataLifecycleEngine.Core.Graph;
using Spectre.Console;
using Spectre.Console.Cli;

namespace DataLifecycleEngine.Cli.Commands;

public class ArchiveCommandSettings : CommandSettings
{
    [CommandOption("-c|--config <PATH>")]
    public string ConfigPath { get; set; } = "configs/retention-policy.example.yaml";

    [CommandOption("--certify")]
    public bool Certify { get; set; }
}

public class ArchiveCommand : AsyncCommand<ArchiveCommandSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, ArchiveCommandSettings settings, CancellationToken cancellationToken)
    {
        AnsiConsole.Write(
            new FigletText("Parquet Archiver")
                .Color(Color.DeepSkyBlue1));

        AnsiConsole.MarkupLine("[bold green]Zero-Downtime Columnar Archival Engine[/]");
        AnsiConsole.MarkupLine($"[grey]Loading retention configuration: {settings.ConfigPath}[/]\n");

        if (!File.Exists(settings.ConfigPath))
        {
            AnsiConsole.MarkupLine($"[red]Error: Configuration file not found at '{settings.ConfigPath}'[/]");
            return 1;
        }

        var config = PolicyLoader.LoadFromFile(settings.ConfigPath);
        ICatalogInspector inspector = config.TargetDatabase.Driver.ToLowerInvariant() switch
        {
            "postgres" or "postgresql" or "npgsql" => new PostgresCatalogInspector(),
            _ => new SqlServerCatalogInspector()
        };
        var storageSink = StorageSinkFactory.CreateSink(config.StorageSink);
        var parquetArchiver = new ParquetArchiver(storageSink);
        var cascadeArchiver = new CascadeArchiver(parquetArchiver);

        var fks = await inspector.GetForeignKeysAsync(config.TargetDatabase.ConnectionString, cancellationToken);

        foreach (var policy in config.Policies)
        {
            AnsiConsole.WriteLine();
            var rule = new Rule($"[yellow]Archiving Policy: {policy.Name}[/]").LeftJustified();
            AnsiConsole.Write(rule);

            var involvedTables = CollectInvolvedTables(policy);
            var plan = TopologicalSorter.ComputeDeletionOrder(involvedTables, fks);

            AnsiConsole.MarkupLine($"[grey]Executing keyset archival across {plan.DeletionOrder.Count} cascade tables...[/]\n");

            var resultTable = new Table();
            resultTable.Border(TableBorder.Rounded);
            resultTable.AddColumn("[bold]Table[/]");
            resultTable.AddColumn("[bold]Rows Archived[/]");
            resultTable.AddColumn("[bold]Raw (KB)[/]");
            resultTable.AddColumn("[bold]Parquet (KB)[/]");
            resultTable.AddColumn("[bold]Savings[/]");
            resultTable.AddColumn("[bold]SHA-256 Checksum[/]");
            resultTable.AddColumn("[bold]Archive Path[/]");

            System.Collections.Generic.List<ArchiveMetadata> completedArchives = new();

            await AnsiConsole.Status()
                .Spinner(Spinner.Known.Aesthetic)
                .StartAsync("Serializing rows into compressed Parquet...", async ctx =>
                {
                    var archives = await cascadeArchiver.ArchivePolicyCascadeAsync(
                        config.TargetDatabase.ConnectionString,
                        policy,
                        plan,
                        cancellationToken);

                    completedArchives = archives;

                    foreach (var m in archives)
                    {
                        double rawKb = m.OriginalSizeBytes / 1024.0;
                        double parquetKb = m.CompressedSizeBytes / 1024.0;
                        string savings = m.CompressionRatio > 0 ? $"{m.CompressionRatio:F1}%" : "0%";
                        string shortHash = m.Sha256Checksum.Length > 16 ? m.Sha256Checksum[..16] + "..." : m.Sha256Checksum;

                        resultTable.AddRow(
                            $"[bold]{m.TableName}[/]",
                            m.RowCount.ToString("N0"),
                            rawKb.ToString("F1"),
                            parquetKb.ToString("F1"),
                            $"[green]{savings}[/]",
                            $"[dim]{shortHash}[/]",
                            $"[blue]{Path.GetFileName(m.StorageLocation)}[/]"
                        );
                    }
                });

            AnsiConsole.Write(resultTable);
            AnsiConsole.MarkupLine($"\n[green]✓ Archival successfully completed for policy '{policy.Name}'![/]");

            if (settings.Certify && completedArchives.Count > 0)
            {
                var cert = await DataLifecycleEngine.Audit.AuditCertificateService.CreateCertificateAsync(
                    config,
                    policy,
                    completedArchives,
                    null,
                    null,
                    cancellationToken);

                var (jsonPath, pdfPath) = await DataLifecycleEngine.Audit.AuditCertificateService.ExportCertificateAsync(
                    cert,
                    "./audit-certificates",
                    cancellationToken);

                AnsiConsole.MarkupLine($"[green]✓ Signed Compliance Certificate emitted:[/] [bold]{Path.GetFileName(pdfPath)}[/]");
            }
        }

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

