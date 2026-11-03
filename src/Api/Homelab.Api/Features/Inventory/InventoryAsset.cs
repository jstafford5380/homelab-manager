
namespace Homelab.Api;
public sealed class InventoryAsset
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Type { get; set; } = "host";
    public string Name { get; set; } = "";
    public string Purpose { get; set; } = "";
    public string Owner { get; set; } = "";
    public string Criticality { get; set; } = "normal";
    public string Location { get; set; } = "";
    public string Version { get; set; } = "";
    public string ManagementUrl { get; set; } = "";
    public string Documentation { get; set; } = "";
    public string Runbook { get; set; } = "";
    public string Verification { get; set; } = "unverified";
    public int Quantity { get; set; } = 1;
    public string CompatibilityNotes { get; set; } = "";
    public decimal? EstimatedUnitCost { get; set; }
    public string Currency { get; set; } = "USD";
    public bool Archived { get; set; }
    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;
}
