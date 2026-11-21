using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using DataLifecycleEngine.Audit.Models;

namespace DataLifecycleEngine.Audit.Generators;

public class JsonCertificateGenerator
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static async Task<string> GenerateAsync(AuditCertificate cert, string outputPath, CancellationToken ct = default)
    {
        string? dir = Path.GetDirectoryName(Path.GetFullPath(outputPath));
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        string json = JsonSerializer.Serialize(cert, JsonOptions);
        await File.WriteAllTextAsync(outputPath, json, ct);
        return outputPath;
    }

    public static async Task<AuditCertificate> LoadFromFileAsync(string path, CancellationToken ct = default)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"Certificate JSON file not found: {path}");
        }

        string json = await File.ReadAllTextAsync(path, ct);
        var cert = JsonSerializer.Deserialize<AuditCertificate>(json, JsonOptions);
        return cert ?? throw new InvalidDataException("Failed to deserialize audit certificate.");
    }
}
