namespace Homelab.Api;
public sealed class HardwareServer
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid? ConnectionId { get; set; }
    public string Name { get; set; } = "";
    public string Host { get; set; } = "";
    public string Platform { get; set; } = "truenas";
    public string? Version { get; set; }
    public double? UptimeSeconds { get; set; }
    public DateTime? LastSuccessUtc { get; set; }
    public DateTime? LastMetricsUtc { get; set; }
    public DateTime? CollectionStartedUtc { get; set; }
    public Guid ConfigurationRevision { get; set; }
    public string? ProtectedCommunity { get; set; }
    public bool Connected { get; set; }
    public DateTime? NextAttemptUtc { get; set; }
}
