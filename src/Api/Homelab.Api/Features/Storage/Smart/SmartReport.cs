
namespace Homelab.Api;
public sealed class SmartReport
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid DiskId { get; set; }
    public string ReportHash { get; set; } = "";
    public DateTime ImportedUtc { get; set; } = DateTime.UtcNow;
    public string Actor { get; set; } = "";
    public string CuratedJson { get; set; } = "";
}
