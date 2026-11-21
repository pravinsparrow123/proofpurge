using System.Collections.Generic;

namespace DataLifecycleEngine.Audit.Models;

public class CertificateVerificationResult
{
    public bool IsValid { get; set; }
    public bool SignatureValid { get; set; }
    public bool ArchiveFilesIntact { get; set; }
    public int VerifiedFileCount { get; set; }
    public List<string> Violations { get; set; } = new();
    public string Message { get; set; } = string.Empty;
}
