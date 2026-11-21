using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using DataLifecycleEngine.Audit.Cryptography;
using DataLifecycleEngine.Audit.Generators;
using DataLifecycleEngine.Audit.Models;
using Xunit;

namespace DataLifecycleEngine.Tests;

public class AuditTests
{
    private AuditCertificate CreateSampleCertificate()
    {
        var cert = new AuditCertificate
        {
            CertificateId = "CERT-TEST-2026-001",
            IssuedAtUtc = new DateTime(2026, 11, 21, 10, 0, 0, DateTimeKind.Utc),
            PolicyName = "purge_inactive_accounts_and_orders",
            RootTable = "Accounts",
            Operator = "test_auditor@corp.local",
            DatabaseTarget = "sqlserver (LifecycleDemo)",
            RetentionCutoff = "3y",
            PurgeAction = "archive_and_purge",
            TotalRowsArchived = 1500,
            TotalRowsPurged = 1500,
            TotalOriginalSizeBytes = 120000,
            TotalParquetSizeBytes = 25000,
            TableRecords = new List<TableAuditRecord>
            {
                new()
                {
                    TableName = "OrderItems",
                    RowsArchived = 500,
                    RowsPurged = 500,
                    ArchiveFileName = "OrderItems_20261121.parquet",
                    ArchiveFilePath = "/archives/OrderItems_20261121.parquet",
                    ParquetSha256Checksum = "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855",
                    ParquetSizeBytes = 6000,
                    OriginalSizeBytes = 30000,
                    CompressionRatio = 80.0
                },
                new()
                {
                    TableName = "Accounts",
                    RowsArchived = 250,
                    RowsPurged = 250,
                    ArchiveFileName = "Accounts_20261121.parquet",
                    ArchiveFilePath = "/archives/Accounts_20261121.parquet",
                    ParquetSha256Checksum = "ca978112ca1bbdcafac231b39a23dc4da786eff8147c4e72b9807785afee48bb",
                    ParquetSizeBytes = 7000,
                    OriginalSizeBytes = 15000,
                    CompressionRatio = 53.3
                }
            }
        };

        AuditCryptographer.SignCertificate(cert);
        return cert;
    }

    [Fact]
    public void SignCertificate_GeneratesNonEmptyHashAndSignature()
    {
        var cert = CreateSampleCertificate();

        Assert.False(string.IsNullOrWhiteSpace(cert.PayloadSha256Hash));
        Assert.False(string.IsNullOrWhiteSpace(cert.DigitalSignature));
        Assert.Equal(64, cert.PayloadSha256Hash.Length); // 64-char hex
        Assert.Equal(64, cert.DigitalSignature.Length); // HMAC-SHA256 64-char hex
    }

    [Fact]
    public async Task VerifyCertificateAsync_ValidCertificate_PassesVerification()
    {
        var cert = CreateSampleCertificate();

        var result = await AuditCryptographer.VerifyCertificateAsync(cert, verifyFilesOnDisk: false);

        Assert.True(result.IsValid);
        Assert.True(result.SignatureValid);
        Assert.Empty(result.Violations);
    }

    [Fact]
    public async Task VerifyCertificateAsync_TamperedRowCount_FailsVerification()
    {
        var cert = CreateSampleCertificate();

        // Tamper with record count after signing
        cert.TotalRowsPurged = 999999;

        var result = await AuditCryptographer.VerifyCertificateAsync(cert, verifyFilesOnDisk: false);

        Assert.False(result.IsValid);
        Assert.False(result.SignatureValid);
        Assert.Contains(result.Violations, v => v.Contains("Payload hash mismatch"));
    }

    [Fact]
    public async Task VerifyCertificateAsync_TamperedTableChecksum_FailsVerification()
    {
        var cert = CreateSampleCertificate();

        // Tamper with table record checksum
        cert.TableRecords[0].ParquetSha256Checksum = "badhash0000000000000000000000000000000000000000000000000000000000";

        var result = await AuditCryptographer.VerifyCertificateAsync(cert, verifyFilesOnDisk: false);

        Assert.False(result.IsValid);
        Assert.False(result.SignatureValid);
    }

    [Fact]
    public async Task JsonAndPdfGenerator_GeneratesValidFiles()
    {
        var cert = CreateSampleCertificate();
        string tempDir = Path.Combine(Path.GetTempPath(), $"audit_gen_{Guid.NewGuid():N}");

        try
        {
            Directory.CreateDirectory(tempDir);
            string jsonFile = Path.Combine(tempDir, "cert.audit.json");
            string pdfFile = Path.Combine(tempDir, "cert.audit.pdf");

            await JsonCertificateGenerator.GenerateAsync(cert, jsonFile);
            PdfCertificateGenerator.Generate(cert, pdfFile);

            Assert.True(File.Exists(jsonFile));
            Assert.True(new FileInfo(jsonFile).Length > 0);

            Assert.True(File.Exists(pdfFile));
            Assert.True(new FileInfo(pdfFile).Length > 1000); // Valid PDF

            // Load back JSON and verify
            var loaded = await JsonCertificateGenerator.LoadFromFileAsync(jsonFile);
            Assert.Equal(cert.CertificateId, loaded.CertificateId);
            Assert.Equal(cert.DigitalSignature, loaded.DigitalSignature);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }
        }
    }
}
