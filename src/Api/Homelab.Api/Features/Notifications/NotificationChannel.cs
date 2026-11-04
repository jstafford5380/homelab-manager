
namespace Homelab.Api;
public sealed class NotificationChannel
{
    public string Kind { get; set; } = "webhook";
    public int Id { get; set; } = 1;
    public Guid Revision { get; set; } = Guid.NewGuid();
    public string ProtectedUrl { get; set; } = "";
    public string Host { get; set; } = "";
    public bool Enabled { get; set; }
    public bool MaskIdentifiers { get; set; } = true;
    public DateTime? TestedUtc { get; set; }
    public string? QuietStart { get; set; }
    public string? QuietEnd { get; set; }
    public DateTime? MaintenanceUntilUtc { get; set; }
}
