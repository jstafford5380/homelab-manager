
namespace Homelab.Api;
public sealed class PlanningItem
{
    public Guid Revision { get; set; } = Guid.NewGuid();
    public string Owner { get; set; } = "";
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid? DiskId { get; set; }
    public string Title { get; set; } = "";
    public string Reason { get; set; } = "";
    public string Status { get; set; } = "planned";
    public decimal? Budget { get; set; }
    public string Currency { get; set; } = "USD";
    public DateTime? DueUtc { get; set; }
}
