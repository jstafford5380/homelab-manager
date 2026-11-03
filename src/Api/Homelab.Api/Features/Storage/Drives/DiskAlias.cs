
namespace Homelab.Api;
public sealed class DiskAlias
{
    public long Id { get; set; }
    public Guid DiskId { get; set; }
    public string Path { get; set; } = "";
    public DateTime FirstSeenUtc { get; set; } = DateTime.UtcNow;
    public DateTime LastSeenUtc { get; set; } = DateTime.UtcNow;
    public DateTime? EndedUtc { get; set; }
}
