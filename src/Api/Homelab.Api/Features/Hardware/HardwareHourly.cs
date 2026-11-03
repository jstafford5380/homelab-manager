namespace Homelab.Api;
public sealed class HardwareHourly
{
    public long Id { get; set; }
    public Guid ServerId { get; set; }
    public Guid ConfigurationRevision { get; set; }
    public DateTime HourUtc { get; set; }
    public int SampleCount { get; set; }
    public double? CpuAverage { get; set; }
    public double? AppsAverageBytes { get; set; }
    public double? CacheAverageBytes { get; set; }
    public double? FreeAverageBytes { get; set; }
    public double? TemperatureAverageC { get; set; }
    public double? TemperatureMaxC { get; set; }
}
