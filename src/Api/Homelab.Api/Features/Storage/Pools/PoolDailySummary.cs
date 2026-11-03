
namespace Homelab.Api;
public sealed class PoolDailySummary
{
    public long Id { get; set; }
    public string GroupKey { get; set; } = "";
    public Guid ConnectionId { get; set; }
    public string PoolGuid { get; set; } = "";
    public DateTime DayUtc { get; set; }
    public string SourceSchema { get; set; } = "";
    public string Status { get; set; } = "";
    public string? StatusCode { get; set; }
    public string? VdevEvidenceJson { get; set; }
    public string? ScanFunction { get; set; }
    public string? ScanState { get; set; }
    public long SampleCount { get; set; }
    public DateTime FirstFetchedUtc { get; set; }
    public DateTime LastFetchedUtc { get; set; }
    public long SizeBytesKnownCount { get; set; }
    public long? SizeBytesMin { get; set; }
    public long? SizeBytesMax { get; set; }
    public long AllocatedBytesKnownCount { get; set; }
    public long? AllocatedBytesMin { get; set; }
    public long? AllocatedBytesMax { get; set; }
    public long FreeBytesKnownCount { get; set; }
    public long? FreeBytesMin { get; set; }
    public long? FreeBytesMax { get; set; }
    public long ScanErrorsKnownCount { get; set; }
    public long? ScanErrorsMin { get; set; }
    public long? ScanErrorsMax { get; set; }
    public long ScanPercentageKnownCount { get; set; }
    public double? ScanPercentageMin { get; set; }
    public double? ScanPercentageMax { get; set; }
}
