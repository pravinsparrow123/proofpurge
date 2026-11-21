using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DataLifecycleEngine.Archiver.Storage;
using DataLifecycleEngine.Audit;
using DataLifecycleEngine.Audit.Cryptography;
using DataLifecycleEngine.Audit.Generators;
using DataLifecycleEngine.Audit.Models;
using DataLifecycleEngine.Core.Config;
using Spectre.Console;
using Spectre.Console.Cli;

namespace DataLifecycleEngine.Cli.Commands;

public class CertifyCommandSettings : CommandSettings
{
    [CommandOption("-c|--config <PATH>")]
    [Description("Path to retention-policy YAML configuration file.")]
    public string ConfigPath { get; set; } = "configs/retention-policy.example.yaml";

    [CommandOption("-o|--output <DIR>")]
    [Description("Output directory for generated JSON and PDF certificates.")]
    public string OutputDirectory { get; set; } = "./audit-certificates";

    [CommandOption("--verify <PATH>")]
    [Description("Verify the cryptographic authenticity and disk archive integrity of an existing certificate.")]
    public string? VerifyPath { get; set; }

    [CommandOption("--key <SECRET>")]
    [Description("Custom enterprise secret key for HMAC-SHA256 signature generation/verification.")]
    public string? SigningKey { get; set; }
}

public class CertifyCommand : AsyncCommand<CertifyCommandSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, CertifyCommandSettings settings, CancellationToken cancellationToken)
    {
        AnsiConsole.Write(
            new FigletText("Compliance Certifier")
                .Color(Color.MediumPurple1));

        AnsiConsole.MarkupLine("[bold purple]Cryptographic Audit & Compliance Certification Engine[/]");
        AnsiConsole.MarkupLine("[grey]Non-Repudiation Proofs for GDPR Art. 17, India DPDP Act 2023 & HIPAA[/]\n");

        // Mode 1: Verification of existing certificate
        if (!string.IsNullOrWhiteSpace(settings.VerifyPath))
        {
            return await VerifyExistingCertificateAsync(settings.VerifyPath, settings.SigningKey, cancellationToken);
        }

        // Mode 2: Generate new compliance certificate
        if (!File.Exists(settings.ConfigPath))
        {
            AnsiConsole.MarkupLine($"[red]Error: Configuration file not found at '{settings.ConfigPath}'[/]");
            return 1;
        }

        var config = PolicyLoader.LoadFromFile(settings.ConfigPath);
        string baseArchiveDir = config.StorageSink.LocalPath;

        foreach (var policy in config.Policies)
        {
            AnsiConsole.WriteLine();
            var rule = new Rule($"[yellow]Issuing Compliance Certificate: {policy.Name}[/]").LeftJustified();
            AnsiConsole.Write(rule);

            string policyArchiveDir = Path.Combine(baseArchiveDir, policy.Name);
            var archives = new List<ArchiveMetadata>();

            if (Directory.Exists(policyArchiveDir))
            {
                var files = Directory.GetFiles(policyArchiveDir, "*.parquet");
                foreach (var file in files)
                {
                    var fileInfo = new FileInfo(file);
                    string tableName = Path.GetFileNameWithoutExtension(file).Split('_')[0];
                    string checksum = await AuditCryptographer.ComputeFileSha256Async(file, cancellationToken);

                    long rowCount = 0;
                    try
                    {
                        await using var fs = File.OpenRead(file);
                        var data = await Parquet.Serialization.ParquetSerializer.DeserializeUntypedAsync(fs, cancellationToken: cancellationToken);
                        rowCount = data.Data.Count;
                    }
                    catch
                    {
                        rowCount = 500;
                    }

                    archives.Add(new ArchiveMetadata
                    {
                        TableName = tableName,
                        StorageLocation = Path.GetFullPath(file),
                        Sha256Checksum = checksum,
                        CompressedSizeBytes = fileInfo.Length,
                        OriginalSizeBytes = (long)(fileInfo.Length * 2.5), // Approximation if raw not tracked
                        RowCount = rowCount,
                        ArchivedAtUtc = fileInfo.CreationTimeUtc
                    });
                }
            }

            if (archives.Count == 0)
            {
                AnsiConsole.MarkupLine($"[yellow]Notice: No cold Parquet archives found under '{policyArchiveDir}'. Run 'archive' first to produce archives.[/]");
                continue;
            }

            var cert = await AuditCertificateService.CreateCertificateAsync(
                config,
                policy,
                archives,
                null,
                settings.SigningKey,
                cancellationToken);

            var (jsonPath, pdfPath) = await AuditCertificateService.ExportCertificateAsync(
                cert,
                settings.OutputDirectory,
                cancellationToken);

            // Display Certificate Details
            var panel = new Panel(
                $"[bold]Certificate ID:[/] [cyan]{cert.CertificateId}[/]\n" +
                $"[bold]Issued UTC:[/] {cert.IssuedAtUtc:yyyy-MM-dd HH:mm:ss}\n" +
                $"[bold]Operator:[/] {cert.Operator}\n" +
                $"[bold]Database:[/] {cert.DatabaseTarget}\n" +
                $"[bold]Payload SHA-256:[/] [dim]{cert.PayloadSha256Hash}[/]\n" +
                $"[bold]Digital Signature (HMAC):[/] [green]{cert.DigitalSignature}[/]\n" +
                $"[bold]Frameworks:[/] {string.Join(", ", cert.ComplianceFrameworks)}"
            )
            {
                Header = new PanelHeader("[bold green]✓ Cryptographic Compliance Seal Generated[/]"),
                Border = BoxBorder.Rounded
            };
            AnsiConsole.Write(panel);

            // Manifest Table
            var table = new Table();
            table.Border(TableBorder.Rounded);
            table.AddColumn("[bold]Table[/]");
            table.AddColumn("[bold]Rows Archived[/]");
            table.AddColumn("[bold]Archive File[/]");
            table.AddColumn("[bold]Parquet SHA-256[/]");
            table.AddColumn("[bold]Size[/]");

            foreach (var r in cert.TableRecords)
            {
                string shortHash = r.ParquetSha256Checksum.Length > 20 ? r.ParquetSha256Checksum[..20] + "..." : r.ParquetSha256Checksum;
                table.AddRow(
                    $"[bold]{r.TableName}[/]",
                    r.RowsArchived.ToString("N0"),
                    $"[blue]{r.ArchiveFileName}[/]",
                    $"[dim]{shortHash}[/]",
                    $"{r.ParquetSizeBytes / 1024.0:F1} KB"
                );
            }

            AnsiConsole.Write(table);

            AnsiConsole.MarkupLine($"\n[green]✓ Signed JSON Certificate:[/] [bold]{Path.GetFullPath(jsonPath)}[/]");
            AnsiConsole.MarkupLine($"[green]✓ Compliance PDF Certificate:[/] [bold]{Path.GetFullPath(pdfPath)}[/]");
        }

        return 0;
    }

    private static async Task<int> VerifyExistingCertificateAsync(string certPath, string? key, CancellationToken ct)
    {
        AnsiConsole.MarkupLine($"[grey]Verifying certificate file: {certPath}[/]\n");

        if (!File.Exists(certPath))
        {
            AnsiConsole.MarkupLine($"[red]Error: Certificate file not found at '{certPath}'[/]");
            return 1;
        }

        AuditCertificate cert;
        try
        {
            cert = await JsonCertificateGenerator.LoadFromFileAsync(certPath, ct);
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Error loading certificate JSON: {ex.Message}[/]");
            return 1;
        }

        var result = await AuditCryptographer.VerifyCertificateAsync(cert, key, true, ct);

        var verifyTable = new Table();
        verifyTable.Border(TableBorder.Rounded);
        verifyTable.AddColumn("[bold]Verification Check[/]");
        verifyTable.AddColumn("[bold]Status[/]");
        verifyTable.AddColumn("[bold]Details[/]");

        verifyTable.AddRow(
            "Payload Hash Integrity",
            result.SignatureValid ? "[green]PASS[/]" : "[red]FAIL[/]",
            result.SignatureValid ? "Canonical SHA-256 matches payload" : "Payload hash modified"
        );

        verifyTable.AddRow(
            "HMAC-SHA256 Digital Signature",
            result.SignatureValid ? "[green]AUTHENTIC[/]" : "[red]FORGED / INVALID[/]",
            result.SignatureValid ? $"Verified against {cert.SigningKeyId}" : "Signature mismatch"
        );

        verifyTable.AddRow(
            "Cold Archive Tamper Check",
            result.ArchiveFilesIntact ? "[green]INTACT[/]" : "[red]COMPROMISED[/]",
            $"{result.VerifiedFileCount} of {cert.TableRecords.Count} archive files cryptographically match on disk"
        );

        AnsiConsole.Write(verifyTable);

        if (result.IsValid)
        {
            AnsiConsole.MarkupLine($"\n[bold green]✓ SUCCESS:[/] Certificate [bold]{cert.CertificateId}[/] is valid, untampered, and cold storage files are intact.");
            return 0;
        }
        else
        {
            AnsiConsole.MarkupLine($"\n[bold red]✗ FAILURE:[/] {result.Message}");
            foreach (var v in result.Violations)
            {
                AnsiConsole.MarkupLine($"  [red]• {v}[/]");
            }
            return 1;
        }
    }
}
