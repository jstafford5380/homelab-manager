namespace Homelab.Api;
public sealed class HardwareReading
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ServerId { get; set; }
    public Guid Revision { get; set; } = Guid.NewGuid();
    public Guid ConfigurationRevision { get; set; }
    public DateTime TakenUtc { get; set; }
    public string Source { get; set; } = "entered";
    public string Kind { get; set; } = "moment";
    public int? PeriodMinutes { get; set; }
    public double? CpuPercent { get; set; }
    public double? MemoryUsedBytes { get; set; }
    public double? TotalBytes { get; set; }
    public double? CacheBytes { get; set; }
    public double? FreeBytes { get; set; }
    public double? TemperatureC { get; set; }
    public double? PowerWatts { get; set; }
    public string Note { get; set; } = "";
    public DateTime? DeletedUtc { get; set; }
}
