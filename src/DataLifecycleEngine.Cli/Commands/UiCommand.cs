using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using DataLifecycleEngine.Archiver;
using DataLifecycleEngine.Archiver.Storage;
using DataLifecycleEngine.Audit;
using DataLifecycleEngine.Audit.Cryptography;
using DataLifecycleEngine.Audit.Generators;
using DataLifecycleEngine.Catalog;
using DataLifecycleEngine.Core;
using DataLifecycleEngine.Core.Config;
using DataLifecycleEngine.Core.Graph;
using DataLifecycleEngine.Purger;
using Microsoft.Data.SqlClient;
using Spectre.Console;
using Spectre.Console.Cli;

namespace DataLifecycleEngine.Cli.Commands;

public class UiCommandSettings : CommandSettings
{
    [CommandOption("-c|--config <PATH>")]
    [Description("Path to retention-policy YAML configuration file.")]
    public string ConfigPath { get; set; } = "configs/retention-policy.example.yaml";

    [CommandOption("-p|--port <PORT>")]
    [Description("Local HTTP port for the ProofPurge Web Cockpit.")]
    public int Port { get; set; } = 5050;
}

public class UiCommand : AsyncCommand<UiCommandSettings>
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public override async Task<int> ExecuteAsync(CommandContext context, UiCommandSettings settings, CancellationToken cancellationToken)
    {
        if (!File.Exists(settings.ConfigPath))
        {
            AnsiConsole.MarkupLine($"[red]Error: Configuration file not found at '{settings.ConfigPath}'[/]");
            return 1;
        }

        var config = PolicyLoader.LoadFromFile(settings.ConfigPath);
        string url = $"http://localhost:{settings.Port}/";

        using var listener = new HttpListener();
        listener.Prefixes.Add(url);
        listener.Start();

        AnsiConsole.MarkupLine($"[bold green]ProofPurge Web Cockpit running at[/] [bold cyan]{url}[/]");
        AnsiConsole.MarkupLine("[grey]Press Ctrl+C to stop the server.[/]");

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            cts.Cancel();
        };

        PurgeRunResult? lastPurgeResult = null;

        while (!cts.Token.IsCancellationRequested)
        {
            HttpListenerContext ctx;
            try
            {
                ctx = await listener.GetContextAsync().WaitAsync(cts.Token);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (HttpListenerException)
            {
                break;
            }

            _ = Task.Run(async () =>
            {
                try
                {
                    lastPurgeResult = await HandleRequestAsync(ctx, config, lastPurgeResult, cts.Token);
                }
                catch (Exception ex)
                {
                    await WriteJsonAsync(ctx.Response, 500, new { error = ex.Message });
                }
            }, cts.Token);
        }

        listener.Stop();
        return 0;
    }

    private static async Task<PurgeRunResult?> HandleRequestAsync(
        HttpListenerContext ctx,
        RetentionConfiguration config,
        PurgeRunResult? lastPurgeResult,
        CancellationToken ct)
    {
        string path = ctx.Request.Url?.AbsolutePath ?? "/";
        string method = ctx.Request.HttpMethod;
        var policy = config.Policies.First();

        if (method == "GET" && path == "/")
        {
            byte[] bytes = Encoding.UTF8.GetBytes(CockpitHtml.Html);
            ctx.Response.StatusCode = 200;
            ctx.Response.ContentType = "text/html; charset=utf-8";
            await ctx.Response.OutputStream.WriteAsync(bytes, ct);
            ctx.Response.Close();
            return lastPurgeResult;
        }

        if (method == "GET" && path == "/api/status")
        {
            var vitalsMonitor = CreateVitalsMonitor(config.TargetDatabase.Driver);
            var vitals = await vitalsMonitor.GetVitalsAsync(config.TargetDatabase.ConnectionString, ct);
            await WriteJsonAsync(ctx.Response, 200, new
            {
                driver = config.TargetDatabase.Driver,
                policyName = policy.Name,
                rootTable = policy.RootTable,
                dateColumn = policy.DateColumn,
                retentionCutoff = policy.RetentionCutoff,
                maxBatchSize = config.SafetyThresholds.MaxBatchSize,
                maxLogPercent = config.SafetyThresholds.MaxTransactionLogPercent,
                vitals
            });
            return lastPurgeResult;
        }

        if (method == "POST" && path == "/api/scan")
        {
            var inspector = CreateInspector(config.TargetDatabase.Driver);
            var tables = await inspector.GetTablesAsync(config.TargetDatabase.ConnectionString, ct);
            var fks = await inspector.GetForeignKeysAsync(config.TargetDatabase.ConnectionString, ct);
            var involvedTables = CollectInvolvedTables(policy);
            var plan = TopologicalSorter.ComputeDeletionOrder(involvedTables, fks);

            DateTime cutoffDate = DateHelper.CalculateCutoffDate(policy.RetentionCutoff);
            string rootWhere = $"[{policy.DateColumn}] < @Cutoff";
            if (!string.IsNullOrWhiteSpace(policy.WhereClause))
                rootWhere += $" AND ({policy.WhereClause})";

            var tableRows = new List<object>();
            foreach (var tblName in plan.DeletionOrder)
            {
                var meta = tables.FirstOrDefault(t => string.Equals(t.TableName, tblName, StringComparison.OrdinalIgnoreCase));
                string countSql = AdaptiveCascadePurger.BuildDryRunCountQuery(tblName, policy, rootWhere);
                await using var conn = new SqlConnection(config.TargetDatabase.ConnectionString);
                await conn.OpenAsync(ct);
                await using var cmd = new SqlCommand(countSql, conn);
                cmd.Parameters.Add(new SqlParameter("@Cutoff", SqlDbType.DateTime2) { Value = cutoffDate });
                long candidateCount = Convert.ToInt64(await cmd.ExecuteScalarAsync(ct) ?? 0);

                tableRows.Add(new
                {
                    name = tblName,
                    estimatedRows = meta?.EstimatedRowCount ?? 0,
                    sizeMb = Math.Round((meta?.TotalSizeBytes ?? 0) / (1024.0 * 1024.0), 2),
                    candidateRows = candidateCount
                });
            }

            await WriteJsonAsync(ctx.Response, 200, new
            {
                deletionOrder = plan.DeletionOrder,
                tables = tableRows,
                warnings = plan.MissingIndexWarnings
            });
            return lastPurgeResult;
        }

        if (method == "POST" && path == "/api/archive")
        {
            var inspector = CreateInspector(config.TargetDatabase.Driver);
            var fks = await inspector.GetForeignKeysAsync(config.TargetDatabase.ConnectionString, ct);
            var plan = TopologicalSorter.ComputeDeletionOrder(CollectInvolvedTables(policy), fks);
            var sink = StorageSinkFactory.CreateSink(config.StorageSink);
            var archiver = new CascadeArchiver(new ParquetArchiver(sink));

            var archives = await archiver.ArchivePolicyCascadeAsync(config.TargetDatabase.ConnectionString, policy, plan, ct);
            await WriteJsonAsync(ctx.Response, 200, new
            {
                archives = archives.Select(a => new
                {
                    a.TableName,
                    a.RowCount,
                    a.CompressionRatio,
                    fileName = Path.GetFileName(a.StorageLocation),
                    a.Sha256Checksum
                })
            });
            return lastPurgeResult;
        }

        if (method == "POST" && (path == "/api/purge/dry-run" || path == "/api/purge/live"))
        {
            bool isDryRun = path == "/api/purge/dry-run";
            var inspector = CreateInspector(config.TargetDatabase.Driver);
            var vitalsMonitor = CreateVitalsMonitor(config.TargetDatabase.Driver);
            var fks = await inspector.GetForeignKeysAsync(config.TargetDatabase.ConnectionString, ct);
            var plan = TopologicalSorter.ComputeDeletionOrder(CollectInvolvedTables(policy), fks);
            var purger = new AdaptiveCascadePurger(vitalsMonitor, new SqliteCheckpointStore());

            var runResult = await purger.PurgeCascadeAsync(
                config.TargetDatabase.ConnectionString,
                policy,
                plan,
                new PurgeOptions
                {
                    BatchSize = config.SafetyThresholds.MaxBatchSize,
                    ThrottlePauseMs = config.SafetyThresholds.ThrottlePauseMs,
                    MaxTransactionLogPercent = config.SafetyThresholds.MaxTransactionLogPercent,
                    DryRun = isDryRun
                },
                ct);

            await WriteJsonAsync(ctx.Response, 200, new
            {
                runResult.PolicyName,
                runResult.IsDryRun,
                runResult.CompletedSuccessfully,
                runResult.TotalRowsPurged,
                totalDurationSeconds = runResult.TotalDuration.TotalSeconds,
                tables = runResult.TableResults.Select(t => new
                {
                    t.TableName,
                    t.Status,
                    t.RowsPurged,
                    durationSeconds = t.Duration.TotalSeconds
                })
            });
            return runResult;
        }

        if (method == "POST" && path == "/api/certify")
        {
            string policyArchiveDir = Path.Combine(config.StorageSink.LocalPath, policy.Name);
            var archives = new List<ArchiveMetadata>();

            if (Directory.Exists(policyArchiveDir))
            {
                // Pick the latest archive file per table for clean certification
                var latestFiles = Directory.GetFiles(policyArchiveDir, "*.parquet")
                    .Select(f => new FileInfo(f))
                    .GroupBy(fi => Path.GetFileNameWithoutExtension(fi.Name).Split('_')[0], StringComparer.OrdinalIgnoreCase)
                    .Select(g => g.OrderByDescending(fi => fi.LastWriteTimeUtc).First());

                foreach (var fi in latestFiles)
                {
                    string tableName = Path.GetFileNameWithoutExtension(fi.Name).Split('_')[0];
                    string checksum = await AuditCryptographer.ComputeFileSha256Async(fi.FullName, ct);
                    await using var fs = File.OpenRead(fi.FullName);
                    var data = await Parquet.Serialization.ParquetSerializer.DeserializeUntypedAsync(fs, cancellationToken: ct);

                    archives.Add(new ArchiveMetadata
                    {
                        TableName = tableName,
                        StorageLocation = fi.FullName,
                        Sha256Checksum = checksum,
                        CompressedSizeBytes = fi.Length,
                        OriginalSizeBytes = (long)(fi.Length * 2.5),
                        RowCount = data.Data.Count,
                        ArchivedAtUtc = fi.CreationTimeUtc
                    });
                }
            }

            var cert = await AuditCertificateService.CreateCertificateAsync(config, policy, archives, lastPurgeResult, null, ct);
            var (jsonPath, pdfPath) = await AuditCertificateService.ExportCertificateAsync(cert, "./audit-certificates", ct);

            await WriteJsonAsync(ctx.Response, 200, new
            {
                cert.CertificateId,
                issuedAtUtc = cert.IssuedAtUtc.ToString("yyyy-MM-dd HH:mm:ss 'UTC'"),
                cert.TotalRowsArchived,
                cert.TotalRowsPurged,
                cert.PayloadSha256Hash,
                cert.DigitalSignature,
                jsonPath = Path.GetFullPath(jsonPath),
                pdfPath = Path.GetFullPath(pdfPath)
            });
            return lastPurgeResult;
        }

        if (method == "POST" && path == "/api/verify")
        {
            using var reader = new StreamReader(ctx.Request.InputStream, ctx.Request.ContentEncoding);
            string body = await reader.ReadToEndAsync(ct);
            using var doc = JsonDocument.Parse(body);
            string jsonPath = doc.RootElement.GetProperty("jsonPath").GetString() ?? string.Empty;

            var cert = await JsonCertificateGenerator.LoadFromFileAsync(jsonPath, ct);
            var res = await AuditCryptographer.VerifyCertificateAsync(cert, null, true, ct);

            await WriteJsonAsync(ctx.Response, 200, new
            {
                cert.CertificateId,
                res.IsValid,
                res.SignatureValid,
                res.ArchiveFilesIntact,
                res.VerifiedFileCount,
                totalFiles = cert.TableRecords.Count,
                res.Message,
                res.Violations
            });
            return lastPurgeResult;
        }

        await WriteJsonAsync(ctx.Response, 404, new { error = "Not found" });
        return lastPurgeResult;
    }

    private static ICatalogInspector CreateInspector(string driver) =>
        driver.ToLowerInvariant() switch
        {
            "postgres" or "postgresql" or "npgsql" => new PostgresCatalogInspector(),
            _ => new SqlServerCatalogInspector()
        };

    private static IDatabaseVitalsMonitor CreateVitalsMonitor(string driver) =>
        driver.ToLowerInvariant() switch
        {
            "postgres" or "postgresql" or "npgsql" => new PostgresVitalsMonitor(),
            _ => new SqlServerVitalsMonitor()
        };

    private static HashSet<string> CollectInvolvedTables(RetentionPolicy policy)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { policy.RootTable };
        void Traverse(IEnumerable<CascadeChild> children)
        {
            foreach (var child in children)
            {
                set.Add(child.Table);
                if (child.Children.Count > 0) Traverse(child.Children);
            }
        }
        Traverse(policy.CascadeChildren);
        return set;
    }

    private static async Task WriteJsonAsync(HttpListenerResponse response, int statusCode, object payload)
    {
        string json = JsonSerializer.Serialize(payload, JsonOptions);
        byte[] bytes = Encoding.UTF8.GetBytes(json);
        response.StatusCode = statusCode;
        response.ContentType = "application/json; charset=utf-8";
        await response.OutputStream.WriteAsync(bytes);
        response.Close();
    }
}
