
namespace Homelab.Api;
public sealed class DrivePathReview
{
    public long Id { get; set; }
    public Guid DiskId { get; set; }
    public Guid AssetId { get; set; }
    public Guid RequestId { get; set; }
    public DateTime AssetUpdatedUtc { get; set; }
    public string AssetName { get; set; } = "";
    public string AssetType { get; set; } = "";
    public string ConfirmedIdentity { get; set; } = "";
    public string DiskModel { get; set; } = "";
    public bool Attached { get; set; }
    public string Evidence { get; set; } = "";
    public DateTime ValidUntilUtc { get; set; }
    public DateTime RecordedUtc { get; set; }
    public string Author { get; set; } = "";
}
