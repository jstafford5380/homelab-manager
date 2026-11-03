namespace Homelab.Api;
public sealed class HardwareEvent
{
    public long Id { get; set; }
    public Guid ComponentId { get; set; }
    public DateTime AtUtc { get; set; } = DateTime.UtcNow;
    public string Kind { get; set; } = "note";
    public string Text { get; set; } = "";
}
