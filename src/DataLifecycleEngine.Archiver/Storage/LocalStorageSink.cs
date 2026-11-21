using System;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace DataLifecycleEngine.Archiver.Storage;

public class ArchiveMetadata
{
    public string TableName { get; set; } = string.Empty;
    public long RowCount { get; set; }
    public long OriginalSizeBytes { get; set; }
    public long CompressedSizeBytes { get; set; }
    public string Sha256Checksum { get; set; } = string.Empty;
    public string StorageLocation { get; set; } = string.Empty;
    public DateTime ArchivedAtUtc { get; set; } = DateTime.UtcNow;

    public double CompressionRatio => OriginalSizeBytes > 0 
        ? (1.0 - ((double)CompressedSizeBytes / OriginalSizeBytes)) * 100.0 
        : 0;
}

public interface IStorageSink
{
    Task<(string Location, string Checksum, long SizeBytes)> SaveArchiveAsync(
        string policyName, 
        string tableName, 
        Stream sourceStream, 
        CancellationToken ct = default);

    Task<Stream> OpenArchiveAsync(string pathOrUri, CancellationToken ct = default);
}

public class LocalStorageSink : IStorageSink
{
    private readonly string _baseDirectory;

    public LocalStorageSink(string baseDirectory = "./archives")
    {
        _baseDirectory = Path.GetFullPath(baseDirectory);
    }

    public async Task<(string Location, string Checksum, long SizeBytes)> SaveArchiveAsync(
        string policyName, 
        string tableName, 
        Stream sourceStream, 
        CancellationToken ct = default)
    {
        string targetDir = Path.Combine(_baseDirectory, policyName);
        Directory.CreateDirectory(targetDir);

        string fileName = $"{tableName}_{DateTime.UtcNow:yyyyMMdd_HHmmss}.parquet";
        string filePath = Path.Combine(targetDir, fileName);

        using var sha256 = SHA256.Create();
        await using var fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true);

        // Copy sourceStream to fileStream while computing SHA-256
        sourceStream.Position = 0;
        var buffer = new byte[81920];
        int bytesRead;
        long totalBytes = 0;

        while ((bytesRead = await sourceStream.ReadAsync(buffer, 0, buffer.Length, ct)) > 0)
        {
            await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), ct);
            sha256.TransformBlock(buffer, 0, bytesRead, null, 0);
            totalBytes += bytesRead;
        }

        sha256.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
        string checksum = Convert.ToHexString(sha256.Hash ?? Array.Empty<byte>()).ToLowerInvariant();

        return (filePath, checksum, totalBytes);
    }

    public Task<Stream> OpenArchiveAsync(string pathOrUri, CancellationToken ct = default)
    {
        if (!File.Exists(pathOrUri))
        {
            throw new FileNotFoundException($"Archive file not found: {pathOrUri}");
        }

        Stream stream = new FileStream(pathOrUri, FileMode.Open, FileAccess.Read, FileShare.Read);
        return Task.FromResult(stream);
    }
}

