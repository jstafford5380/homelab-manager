
namespace Homelab.Api;
public sealed class OperatingSettings
{
    public int Id { get; set; } = 1;
    public string Timezone { get; set; } = "America/Phoenix";
    public string CapacityUnits { get; set; } = "binary";
    public double HardwareCpuPercent { get; set; } = 80;
    public double HardwareMemoryPercent { get; set; } = 80;
    public double HardwareTemperatureC { get; set; } = 80;
    public int HardwareSustainedMinutes { get; set; } = 60;
    public int DriveAgeReviewYears { get; set; } = 5;
    public int WarrantyNoticeDays { get; set; } = 90;
    public int InventoryMinutes { get; set; } = 15;
    public int SmartMinutes { get; set; } = 90;
    public double? TemperatureReviewCelsius { get; set; }
    public string TemperaturePolicyReason { get; set; } = "";
    public bool ReadCachedTemperatures { get; set; }
    public bool ReadApiPowerOnHours { get; set; }
    public bool ReadDatasets { get; set; }
    public int SampleRetentionDays { get; set; } = 90;
    public int RawRetentionDays { get; set; } = 14;
    public bool BackupsEnabled { get; set; }
    public int BackupDailyCopies { get; set; } = 7;
    public int BackupWeeklyCopies { get; set; } = 4;
}
