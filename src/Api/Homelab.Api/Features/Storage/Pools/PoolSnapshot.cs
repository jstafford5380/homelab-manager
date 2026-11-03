
namespace Homelab.Api;
public sealed class PoolSnapshot
{
    public long Id { get; set; }
    public Guid ConnectionId { get; set; }
    public string PoolGuid { get; set; } = "";
    public string Name { get; set; } = "";
    public string Status { get; set; } = "";
    public string? StatusCode { get; set; }
    public DateTime FetchedUtc { get; set; }
    public string SourceSchema { get; set; } = "truenas-24.10-pool-v4";
    public string? SourceContextJson { get; set; }
    public string? VdevEvidenceJson { get; set; }
    public string? TopologySignature { get; set; }
    public long? SizeBytes { get; set; }
    public long? AllocatedBytes { get; set; }
    public long? FreeBytes { get; set; }
    public string? ScanFunction { get; set; }
    public string? ScanState { get; set; }
    public double? ScanPercentage { get; set; }
    public long? ScanErrors { get; set; }
    public DateTime? ScanStartedUtc { get; set; }
    public DateTime? ScanEndedUtc { get; set; }
    public DateTime? ScanPausedUtc { get; set; }
    public long? ScanRemainingSeconds { get; set; }
}
