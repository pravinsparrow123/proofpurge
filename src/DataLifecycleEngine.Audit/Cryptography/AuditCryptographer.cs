using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DataLifecycleEngine.Audit.Models;

namespace DataLifecycleEngine.Audit.Cryptography;

public class AuditCryptographer
{
    public const string DefaultSigningKey = "DLE-Enterprise-Audit-Master-Signing-Key-2026-DPDP-GDPR-V1";

    public static string ComputeCanonicalPayload(AuditCertificate cert)
    {
        var sb = new StringBuilder();
        sb.Append(cert.CertificateId).Append('|')
          .Append(cert.IssuedAtUtc.ToString("O")).Append('|')
          .Append(cert.PolicyName).Append('|')
          .Append(cert.RootTable).Append('|')
          .Append(cert.Operator).Append('|')
          .Append(cert.DatabaseTarget).Append('|')
          .Append(cert.RetentionCutoff).Append('|')
          .Append(cert.TotalRowsArchived).Append('|')
          .Append(cert.TotalRowsPurged).Append('|');

        var sortedRecords = cert.TableRecords.OrderBy(r => r.TableName, StringComparer.OrdinalIgnoreCase);
        foreach (var r in sortedRecords)
        {
            sb.Append(r.TableName).Append(':')
              .Append(r.RowsArchived).Append(':')
              .Append(r.RowsPurged).Append(':')
              .Append(r.ParquetSha256Checksum.ToLowerInvariant()).Append(':')
              .Append(r.ParquetSizeBytes).Append(';');
        }

        return sb.ToString();
    }

    public static string ComputeSha256(string content)
    {
        using var sha256 = SHA256.Create();
        byte[] bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(content));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    public static async Task<string> ComputeFileSha256Async(string filePath, CancellationToken ct = default)
    {
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException($"File not found for checksum verification: {filePath}");
        }

        using var sha256 = SHA256.Create();
        await using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true);
        byte[] hash = await sha256.ComputeHashAsync(stream, ct);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    public static void SignCertificate(AuditCertificate cert, string? secretKey = null)
    {
        string canonical = ComputeCanonicalPayload(cert);
        cert.PayloadSha256Hash = ComputeSha256(canonical);

        string keyToUse = secretKey ?? DefaultSigningKey;
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(keyToUse));
        byte[] signatureBytes = hmac.ComputeHash(Encoding.UTF8.GetBytes(cert.PayloadSha256Hash));
        cert.DigitalSignature = Convert.ToHexString(signatureBytes).ToLowerInvariant();
    }

    public static async Task<CertificateVerificationResult> VerifyCertificateAsync(
        AuditCertificate cert, 
        string? secretKey = null, 
        bool verifyFilesOnDisk = true, 
        CancellationToken ct = default)
    {
        var result = new CertificateVerificationResult { IsValid = true, SignatureValid = true, ArchiveFilesIntact = true };

        // 1. Verify Payload Hash
        string expectedCanonical = ComputeCanonicalPayload(cert);
        string expectedHash = ComputeSha256(expectedCanonical);

        if (!string.Equals(expectedHash, cert.PayloadSha256Hash, StringComparison.OrdinalIgnoreCase))
        {
            result.IsValid = false;
            result.SignatureValid = false;
            result.Violations.Add("Payload hash mismatch: Certificate content was modified after signing.");
        }

        // 2. Verify HMAC Signature
        string keyToUse = secretKey ?? DefaultSigningKey;
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(keyToUse));
        byte[] signatureBytes = hmac.ComputeHash(Encoding.UTF8.GetBytes(expectedHash));
        string expectedSig = Convert.ToHexString(signatureBytes).ToLowerInvariant();

        if (!string.Equals(expectedSig, cert.DigitalSignature, StringComparison.OrdinalIgnoreCase))
        {
            result.IsValid = false;
            result.SignatureValid = false;
            result.Violations.Add("Digital signature verification failed: Invalid key or forged signature.");
        }

        // 3. Verify referenced Parquet files on disk
        if (verifyFilesOnDisk)
        {
            foreach (var rec in cert.TableRecords)
            {
                if (string.IsNullOrWhiteSpace(rec.ArchiveFilePath) || !File.Exists(rec.ArchiveFilePath))
                {
                    result.ArchiveFilesIntact = false;
                    result.Violations.Add($"Archive file not found on storage: {rec.ArchiveFileName} ({rec.ArchiveFilePath})");
                    continue;
                }

                string actualHash = await ComputeFileSha256Async(rec.ArchiveFilePath, ct);
                if (!string.Equals(actualHash, rec.ParquetSha256Checksum, StringComparison.OrdinalIgnoreCase))
                {
                    result.IsValid = false;
                    result.ArchiveFilesIntact = false;
                    result.Violations.Add($"Checksum corruption in {rec.ArchiveFileName}: Stored hash {rec.ParquetSha256Checksum} does not match file contents {actualHash}.");
                }
                else
                {
                    result.VerifiedFileCount++;
                }
            }
        }

        if (result.Violations.Count > 0)
        {
            result.IsValid = false;
            result.Message = $"Certificate verification failed with {result.Violations.Count} violation(s).";
        }
        else
        {
            result.Message = "Certificate is authentic, digitally signed, and cold archives are cryptographically verified.";
        }

        return result;
    }
}
