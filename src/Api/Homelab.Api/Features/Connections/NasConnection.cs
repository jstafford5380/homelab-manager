
namespace Homelab.Api;
public sealed class NasConnection
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public string ApiUri { get; set; } = "";
    public string? ConnectHost { get; set; }
    public int? ConnectPort { get; set; }
    public string ProtectedApiKey { get; set; } = "";
    public string? Version { get; set; }
    public string Status { get; set; } = "Not collected";
    public DateTime? LastSuccessUtc { get; set; }
    public int PollSeconds { get; set; } = 60;
    public bool Enabled { get; set; } = true;
    public string? DiscoveryIdentityJson { get; set; }
    public string? TopologyJson { get; set; }
    public string? CachedTemperaturesJson { get; set; }
    public string? DatasetsJson { get; set; }
    public DateTime? LastAttemptUtc { get; set; }
    public DateTime? NextAttemptUtc { get; set; }
    public int ConsecutiveFailures { get; set; }
    public string? TrustedCaPem { get; set; }
    public string? CertificateSha256 { get; set; }
    public string? SystemSerial { get; set; }
    public string? ObservedSystemSerial { get; set; }
    public string SystemIdentityState { get; set; } = "unavailable";
    public DateTime? SystemIdentityCheckedUtc { get; set; }
    public Guid ConfigurationRevision { get; set; } = Guid.NewGuid();
}
