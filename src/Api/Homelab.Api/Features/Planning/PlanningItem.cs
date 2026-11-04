
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json;
using System.Text.Json.Serialization;
namespace Homelab.Api;
public sealed class PlanningItem
{
    // Keep Reason as the stored notes field for existing clients and records.
    [NotMapped] public string Notes { get => Reason; set => Reason = value; }
    [JsonIgnore] public string EvidenceJson { get; set; } = "[]";
    [NotMapped] public PlanningReason[] Evidence { get => JsonSerializer.Deserialize<PlanningReason[]>(EvidenceJson) ?? []; set => EvidenceJson = JsonSerializer.Serialize(value); }
    public DateTime? CompletedUtc { get; set; }
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
