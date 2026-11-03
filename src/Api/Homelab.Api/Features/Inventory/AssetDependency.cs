
namespace Homelab.Api;
public sealed class AssetDependency
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SourceId { get; set; }
    public Guid TargetId { get; set; }
    public string Relationship { get; set; } = "runs_on";
    public string Verification { get; set; } = "unverified";
    public string Evidence { get; set; } = "";
    public string Author { get; set; } = "";
    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;
}
