
namespace Homelab.Api;
public sealed class DiskAnnotation
{
    public long Id { get; set; }
    public Guid DiskId { get; set; }
    public string Actor { get; set; } = "";
    public DateTime AtUtc { get; set; } = DateTime.UtcNow;
    public string BeforeJson { get; set; } = "";
    public string AfterJson { get; set; } = "";
}
