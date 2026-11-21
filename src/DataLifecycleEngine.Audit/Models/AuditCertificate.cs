using System;
using System.Collections.Generic;

namespace DataLifecycleEngine.Audit.Models;

public class AuditCertificate
{
    public string CertificateId { get; set; } = string.Empty;
    public DateTime IssuedAtUtc { get; set; } = DateTime.UtcNow;
    public string PolicyName { get; set; } = string.Empty;
    public string RootTable { get; set; } = string.Empty;
    public string Operator { get; set; } = string.Empty;
    public string DatabaseTarget { get; set; } = string.Empty;
    public string RetentionCutoff { get; set; } = string.Empty;
    public string PurgeAction { get; set; } = "archive_and_purge";
    
    public List<string> ComplianceFrameworks { get; set; } = new()
    {
        "GDPR Article 17 (Right to Erasure)",
        "India DPDP Act 2023 Section 12",
        "HIPAA Safe Harbor Retention",
        "SOC 2 Type II Privacy & Confidentiality"
    };

    public List<TableAuditRecord> TableRecords { get; set; } = new();

    public long TotalRowsArchived { get; set; }
    public long TotalRowsPurged { get; set; }
    public long TotalOriginalSizeBytes { get; set; }
    public long TotalParquetSizeBytes { get; set; }

    public double OverallCompressionRatio => TotalOriginalSizeBytes > 0
        ? (1.0 - ((double)TotalParquetSizeBytes / TotalOriginalSizeBytes)) * 100.0
        : 0;

    public string PayloadSha256Hash { get; set; } = string.Empty;
    public string DigitalSignature { get; set; } = string.Empty;
    public string SigningKeyId { get; set; } = "DLE-MASTER-KEY-01";
}
