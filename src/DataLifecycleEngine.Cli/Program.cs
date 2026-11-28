using System.Threading.Tasks;
using DataLifecycleEngine.Cli.Commands;
using Spectre.Console.Cli;

namespace DataLifecycleEngine.Cli;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        var app = new CommandApp();
        app.Configure(config =>
        {
            config.SetApplicationName("proofpurge");
            
            config.AddCommand<InitCommand>("init")
                .WithDescription("Initializes a new starter retention-policy YAML configuration file.");

            config.AddCommand<ScanCommand>("scan")
                .WithDescription("Scans the database schema, validates foreign keys, and computes safe topological deletion order.");

            config.AddCommand<ArchiveCommand>("archive")
                .WithDescription("Extracts historical candidate records across cascade tables and streams them into compressed Parquet files.");

            config.AddCommand<RestoreCommand>("restore")
                .WithDescription("Restores archived records from a Parquet file back into the database using SqlBulkCopy with identity preservation.");

            config.AddCommand<PurgeCommand>("purge")
                .WithDescription("Executes zero-lock, micro-batched cascade deletions with real-time transaction log space monitoring.");

            config.AddCommand<CertifyCommand>("certify")
                .WithDescription("Issues cryptographically signed compliance audit certificates (PDF/JSON) and verifies tamper-evidence.");

            config.AddCommand<UiCommand>("ui")
                .WithDescription("Launches the ProofPurge Web Cockpit dashboard for interactive demo and monitoring.");
        });

        return await app.RunAsync(args);
    }
}
