using System;

namespace DataLifecycleEngine.Audit.Models;

public class TableAuditRecord
{
    public string TableName { get; set; } = string.Empty;
    public long RowsArchived { get; set; }
    public long RowsPurged { get; set; }
    public string ArchiveFileName { get; set; } = string.Empty;
    public string ArchiveFilePath { get; set; } = string.Empty;
    public string ParquetSha256Checksum { get; set; } = string.Empty;
    public long OriginalSizeBytes { get; set; }
    public long ParquetSizeBytes { get; set; }
    public double CompressionRatio { get; set; }
}
