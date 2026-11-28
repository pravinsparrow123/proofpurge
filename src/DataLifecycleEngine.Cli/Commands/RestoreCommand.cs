using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using DataLifecycleEngine.Archiver;
using DataLifecycleEngine.Archiver.Storage;
using DataLifecycleEngine.Core.Config;
using Spectre.Console;
using Spectre.Console.Cli;

namespace DataLifecycleEngine.Cli.Commands;

public class RestoreCommandSettings : CommandSettings
{
    [CommandOption("-f|--file <FILE>")]
    public string ArchiveFile { get; set; } = string.Empty;

    [CommandOption("-t|--table <TABLE>")]
    public string Table { get; set; } = string.Empty;

    [CommandOption("-s|--schema <SCHEMA>")]
    public string Schema { get; set; } = "dbo";

    [CommandOption("-c|--config <PATH>")]
    public string ConfigPath { get; set; } = "configs/retention-policy.example.yaml";
}

public class RestoreCommand : AsyncCommand<RestoreCommandSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, RestoreCommandSettings settings, CancellationToken cancellationToken)
    {
        AnsiConsole.Write(
            new FigletText("Safe Restore")
                .Color(Color.Green1));

        if (string.IsNullOrWhiteSpace(settings.ArchiveFile) || string.IsNullOrWhiteSpace(settings.Table))
        {
            AnsiConsole.MarkupLine("[red]Error: Both --file <ARCHIVE_PATH> and --table <TABLE_NAME> are required.[/]");
            return 1;
        }

        if (!File.Exists(settings.ArchiveFile))
        {
            AnsiConsole.MarkupLine($"[red]Error: Parquet file not found at '{settings.ArchiveFile}'[/]");
            return 1;
        }

        var config = PolicyLoader.LoadFromFile(settings.ConfigPath);
        var storageSink = StorageSinkFactory.CreateSink(config.StorageSink);
        var restorer = new ParquetRestorer(storageSink);

        AnsiConsole.MarkupLine($"[grey]Target Table:[/] [bold]{settings.Schema}.{settings.Table}[/]");
        AnsiConsole.MarkupLine($"[grey]Source Archive:[/] [blue]{settings.ArchiveFile}[/]\n");

        long restoredCount = 0;
        await AnsiConsole.Status()
            .Spinner(Spinner.Known.Dots)
            .StartAsync("Restoring records into SQL Server with SqlBulkCopy...", async ctx =>
            {
                restoredCount = await restorer.RestoreTableAsync(
                    config.TargetDatabase.ConnectionString,
                    settings.ArchiveFile,
                    settings.Table,
                    settings.Schema,
                    cancellationToken);
            });

        AnsiConsole.MarkupLine($"[green]✓ Successfully restored [bold]{restoredCount:N0}[/] records into table [[{Markup.Escape(settings.Table)}]] with identity preservation![/]");
        return 0;
    }
}
