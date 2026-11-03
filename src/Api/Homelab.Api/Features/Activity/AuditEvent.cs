
namespace Homelab.Api;
public sealed class AuditEvent
{
    public long Id { get; set; }
    public DateTime AtUtc { get; set; } = DateTime.UtcNow;
    public string Actor { get; set; } = "system";
    public string Action { get; set; } = "";
    public string Detail { get; set; } = "";
}
