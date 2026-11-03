
namespace Homelab.Api;
public sealed class DriveAcquisition
{
    public Guid DiskId { get; set; }
    public Guid Revision { get; set; } = Guid.NewGuid();
    public DateTime? InstalledUtc { get; set; }
    public string InstallationConfidence { get; set; } = "unknown";
    public string InstallationEvidence { get; set; } = "";
    public decimal? PurchaseCost { get; set; }
    public string Currency { get; set; } = "";
    public string CostConfidence { get; set; } = "unknown";
    public string PurchaseEvidence { get; set; } = "";
    public string Author { get; set; } = "";
    public DateTime RecordedUtc { get; set; }
}
