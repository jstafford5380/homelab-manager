namespace Homelab.Api;
public sealed class HardwareComponent
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ServerId { get; set; }
    public Guid Revision { get; set; } = Guid.NewGuid();
    public string Kind { get; set; } = "other";
    public string? DetectedKey { get; set; }
    public string? DetectedName { get; set; }
    public string? DetectedSpec { get; set; }
    public double? DetectedTotalBytes { get; set; }
    public bool? FanFailed { get; set; }
    public string Name { get; set; } = "";
    public string Model { get; set; } = "";
    public string Spec { get; set; } = "";
    public string Notes { get; set; } = "";
    public decimal? PricePaid { get; set; }
    public DateTime? PurchasedUtc { get; set; }
    public DateTime? InstalledUtc { get; set; }
    public DateTime? WarrantyEndUtc { get; set; }
    public bool DatesEstimated { get; set; }
    public string SlotsJson { get; set; } = "[]";
    public double? SolderedBytes { get; set; }
    public double? MaxSupportedBytes { get; set; }
    public DateTime? RetiredUtc { get; set; }
    public Guid? PredecessorId { get; set; }
    public string LastStatus { get; set; } = "Not monitored";
    public string LastWhy { get; set; } = "Nothing measures this component.";
}
