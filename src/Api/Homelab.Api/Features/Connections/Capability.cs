
namespace Homelab.Api;
public sealed class Capability
{
    public long Id { get; set; }
    public Guid ConnectionId { get; set; }
    public string Name { get; set; } = "";
    public string Status { get; set; } = "unavailable";
    public string Detail { get; set; } = "";
    public DateTime? LastSuccessUtc { get; set; }
    public DateTime? LastAttemptUtc { get; set; }
    public int ExpectedSeconds { get; set; } = 60;

    public string EffectiveStatus(DateTime now) => Status == "supported" && (LastSuccessUtc is null || now - LastSuccessUtc > TimeSpan.FromSeconds(ExpectedSeconds * 3)) ? "stale" : Status;
}
