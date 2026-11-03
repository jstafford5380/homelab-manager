using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Homelab.Api;

internal static class SettingsSections
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    public static readonly IReadOnlyDictionary<string, string[]> Fields = new Dictionary<string, string[]>
    {
        ["monitoring"] = ["inventoryMinutes", "smartMinutes", "readDatasets", "readApiPowerOnHours", "readCachedTemperatures"],
        ["review-levels"] = ["driveAgeReviewYears", "warrantyNoticeDays", "temperatureReviewCelsius", "temperaturePolicyReason",
            "hardwareCpuPercent", "hardwareMemoryPercent", "hardwareTemperatureC", "hardwareSustainedMinutes"],
        ["data"] = ["sampleRetentionDays", "backupsEnabled", "backupDailyCopies", "backupWeeklyCopies"],
        ["display"] = ["timezone", "capacityUnits"]
    };

    public static JsonObject Document(OperatingSettings settings) => JsonSerializer.SerializeToNode(settings, Json)!.AsObject();
    public static OperatingSettings Read(JsonObject document) => document.Deserialize<OperatingSettings>(Json)!;

    public static string Revision(JsonObject document, string[] fields)
    {
        var values = new JsonArray(fields.Select(field => document[field]?.DeepClone()).ToArray());
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(values.ToJsonString())));
    }

    public static Dictionary<string, string> Revisions(OperatingSettings settings)
    {
        var document = Document(settings);
        return Fields.ToDictionary(section => section.Key, section => Revision(document, section.Value));
    }

    public static Dictionary<string, string[]> Validate(OperatingSettings settings, bool backupConfigured)
    {
        var errors = new Dictionary<string, string[]>();
        void Range(string field, double value, double min, double max)
        {
            if (!double.IsFinite(value) || value < min || value > max)
                errors[field] = [$"Enter a value from {min} to {max}."];
        }
        try { TimeZoneInfo.FindSystemTimeZoneById(settings.Timezone); }
        catch (Exception e) when (e is ArgumentException or TimeZoneNotFoundException or InvalidTimeZoneException)
        { errors["timezone"] = ["Choose a valid timezone."]; }
        if (settings.CapacityUnits is not ("binary" or "decimal"))
            errors["capacityUnits"] = ["Choose binary or decimal units."];
        Range("inventoryMinutes", settings.InventoryMinutes, 1, 1440);
        Range("smartMinutes", settings.SmartMinutes, 15, 10080);
        Range("driveAgeReviewYears", settings.DriveAgeReviewYears, 1, 30);
        Range("warrantyNoticeDays", settings.WarrantyNoticeDays, 0, 730);
        Range("hardwareCpuPercent", settings.HardwareCpuPercent, 1, 100);
        Range("hardwareMemoryPercent", settings.HardwareMemoryPercent, 1, 100);
        Range("hardwareTemperatureC", settings.HardwareTemperatureC, -50, 200);
        Range("hardwareSustainedMinutes", settings.HardwareSustainedMinutes, 5, 1440);
        Range("sampleRetentionDays", settings.SampleRetentionDays, 7, 3650);
        Range("rawRetentionDays", settings.RawRetentionDays, 0, 90);
        Range("backupDailyCopies", settings.BackupDailyCopies, 1, 90);
        Range("backupWeeklyCopies", settings.BackupWeeklyCopies, 1, 52);
        if (settings.TemperatureReviewCelsius is { } temperature)
            Range("temperatureReviewCelsius", temperature, -273.15, 1000);
        if (TemperaturePolicy.Validate(settings) is { } policyError && !errors.ContainsKey("temperatureReviewCelsius"))
            errors["temperaturePolicyReason"] = [policyError];
        if (settings.BackupsEnabled && !backupConfigured)
            errors["backupsEnabled"] = ["Configure the server backup destination first."];
        return errors;
    }
}
