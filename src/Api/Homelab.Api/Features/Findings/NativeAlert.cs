
namespace Homelab.Api;
public sealed class NativeAlert
{
    public long Id { get; set; }
    public Guid ConnectionId { get; set; }
    public string SourceId { get; set; } = "";
    public string Level { get; set; } = "";
    public string Class { get; set; } = "";
    public string Message { get; set; } = "";
    public bool DismissedOnNas { get; set; }
    public DateTime FirstSeenUtc { get; set; } = DateTime.UtcNow;
    public DateTime LastSeenUtc { get; set; } = DateTime.UtcNow;
    public DateTime? AbsentSinceUtc { get; set; }
}
