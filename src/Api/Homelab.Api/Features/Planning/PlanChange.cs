
namespace Homelab.Api;
public sealed class PlanChange
{
    public long Id { get; set; }
    public Guid PlanId { get; set; }
    public DateTime AtUtc { get; set; } = DateTime.UtcNow;
    public string Actor { get; set; } = "";
    public string? BeforeJson { get; set; }
    public string AfterJson { get; set; } = "";
}
