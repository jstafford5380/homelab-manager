
namespace Homelab.Api;
public sealed class BackupRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public string FileName { get; set; } = "";
    public string Status { get; set; } = "pending";
    public string? Sha256 { get; set; }
    public long SizeBytes { get; set; }
}
