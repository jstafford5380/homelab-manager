
namespace Homelab.Api;
public sealed class Disk
{
    public string? Nickname { get; set; }
    public long? SizeBytes { get; set; }
    public string? DriveType { get; set; }
    public int? RotationRate { get; set; }
    public int? Enclosure { get; set; }
    public int? Slot { get; set; }
    public string? BayConfirmedIdentity { get; set; }
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ConnectionId { get; set; }
    public string IdentityKey { get; set; } = "";
    public string Serial { get; set; } = "";
    public string Model { get; set; } = "";
    public string? Wwn { get; set; }
    public string Alias { get; set; } = "";
    public string MappingConfidence { get; set; } = "inferred";
    public string? Bay { get; set; }
    public bool BayVerified { get; set; }
    public bool Present { get; set; } = true;
    public DateTime FirstSeenUtc { get; set; } = DateTime.UtcNow;
    public DateTime LastSeenUtc { get; set; } = DateTime.UtcNow;
    public DateTime? PurchasedUtc { get; set; }
    public DateTime? WarrantyEndUtc { get; set; }
    public string Notes { get; set; } = "";
    public string Lifecycle { get; set; } = "installed";
    public string PurchaseDateConfidence { get; set; } = "unknown";
    public string? BayEvidence { get; set; }
    public DateTime? BayVerifiedUtc { get; set; }
}
