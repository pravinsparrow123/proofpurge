using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DataLifecycleEngine.Archiver.Storage;
using DataLifecycleEngine.Audit.Cryptography;
using DataLifecycleEngine.Audit.Generators;
using DataLifecycleEngine.Audit.Models;
using DataLifecycleEngine.Core.Config;
using DataLifecycleEngine.Purger;

namespace DataLifecycleEngine.Audit;

public class AuditCertificateService
{
    public static async Task<AuditCertificate> CreateCertificateAsync(
        RetentionConfiguration config,
        RetentionPolicy policy,
        List<ArchiveMetadata> archives,
        PurgeRunResult? purgeResult,
        string? signingKey = null,
        CancellationToken ct = default)
    {
        string certId = $"CERT-{DateTime.UtcNow:yyyyMMdd}-{Guid.NewGuid():N}"[..22].ToUpperInvariant();

        var cert = new AuditCertificate
        {
            CertificateId = certId,
            IssuedAtUtc = DateTime.UtcNow,
            PolicyName = policy.Name,
            RootTable = policy.RootTable,
            Operator = $"{Environment.UserName}@{Environment.MachineName}",
            DatabaseTarget = $"{config.TargetDatabase.Driver} ({config.TargetDatabase.ConnectionString.Split(';').FirstOrDefault() ?? "TargetDB"})",
            RetentionCutoff = policy.RetentionCutoff,
            PurgeAction = policy.Action
        };

        var purgeDict = purgeResult?.TableResults?.ToDictionary(r => r.TableName, r => r.RowsPurged, StringComparer.OrdinalIgnoreCase) 
            ?? new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);

        foreach (var arch in archives)
        {
            purgeDict.TryGetValue(arch.TableName, out long rowsPurged);
            // If purgeResult wasn't provided or dry run, rows purged equals rows archived
            if (rowsPurged == 0 && purgeResult == null)
            {
                rowsPurged = arch.RowCount;
            }

            cert.TableRecords.Add(new TableAuditRecord
            {
                TableName = arch.TableName,
                RowsArchived = arch.RowCount,
                RowsPurged = rowsPurged,
                ArchiveFileName = Path.GetFileName(arch.StorageLocation),
                ArchiveFilePath = arch.StorageLocation,
                ParquetSha256Checksum = arch.Sha256Checksum,
                OriginalSizeBytes = arch.OriginalSizeBytes,
                ParquetSizeBytes = arch.CompressedSizeBytes,
                CompressionRatio = arch.CompressionRatio
            });

            cert.TotalRowsArchived += arch.RowCount;
            cert.TotalRowsPurged += rowsPurged;
            cert.TotalOriginalSizeBytes += arch.OriginalSizeBytes;
            cert.TotalParquetSizeBytes += arch.CompressedSizeBytes;
        }

        // Cryptographically sign the certificate
        AuditCryptographer.SignCertificate(cert, signingKey);

        return cert;
    }

    public static async Task<(string JsonPath, string PdfPath)> ExportCertificateAsync(
        AuditCertificate cert,
        string outputDirectory = "./audit-certificates",
        CancellationToken ct = default)
    {
        if (!Directory.Exists(outputDirectory))
        {
            Directory.CreateDirectory(outputDirectory);
        }

        string baseName = $"{cert.PolicyName}_{cert.CertificateId}";
        string jsonPath = Path.Combine(outputDirectory, $"{baseName}.audit.json");
        string pdfPath = Path.Combine(outputDirectory, $"{baseName}.audit.pdf");

        await JsonCertificateGenerator.GenerateAsync(cert, jsonPath, ct);
        PdfCertificateGenerator.Generate(cert, pdfPath);

        return (jsonPath, pdfPath);
    }
}
