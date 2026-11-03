using System.Text.Json;

namespace Homelab.Api;
public static class StorageInventory
{
    public static string Identity(Disk disk) => JsonSerializer.Serialize(new { disk.IdentityKey, disk.Serial, disk.Model, wwn = IdentityCandidate.NormalizeWwn(disk.Wwn) });
    public static bool BayConfirmed(Disk disk) => disk.BayVerified && disk.MappingConfidence != "ambiguous" && disk.BayConfirmedIdentity == Identity(disk) && !string.IsNullOrWhiteSpace(disk.Bay);
    internal static DiskInventory Parse(JsonElement row)
    {
        var size = Number(row, "size");
        var type = Text(row, "type");
        var rotation = Number(row, "rotationrate");
        var enclosure = row.TryGetProperty("enclosure", out var value) && value.ValueKind == JsonValueKind.Object ? value : default;
        var number = Number(enclosure, "number");
        var slot = Number(enclosure, "slot");
        return new(new(ReportedText(row, "serial"), ReportedText(row, "model"), ReportedText(row, "wwn"), ReportedText(row, "name")),
            size is > 0 and <= 9007199254740991L ? size : null,
            type is "HDD" or "SSD" ? type : null,
            rotation is > 0 and <= 100000 ? (int?)rotation : null,
            number is >= 0 and <= int.MaxValue ? (int?)number : null,
            slot is >= 0 and <= int.MaxValue ? (int?)slot : null);
    }

    private static string ReportedText(JsonElement row, string key) => row.TryGetProperty(key, out var value) ? value.ToString() : "";

    public static void Attach(IReadOnlyList<Disk> disks, JsonElement inventory) =>
        Attach(disks, inventory.EnumerateArray().Select(Parse).ToArray());

    internal static void Attach(IReadOnlyList<Disk> disks, DiskInventory[] inventory)
    {
        foreach (var disk in disks.Where(d => d.Present && d.MappingConfidence != "ambiguous"))
        {
            var rows = inventory.Where(r => r.Identity.Alias == disk.Alias && r.Identity.Serial == disk.Serial && r.Identity.Model == disk.Model).ToArray();
            if (rows.Length != 1)
                continue;
            var row = rows[0];
            disk.SizeBytes = row.SizeBytes;
            disk.DriveType = row.DriveType;
            disk.RotationRate = row.RotationRate;
            disk.Enclosure = row.Enclosure;
            disk.Slot = row.Slot;
        }
    }

    public static string Text(JsonElement row, string key) => row.ValueKind == JsonValueKind.Object && row.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString()! : "";
    private static long? Number(JsonElement row, string key) => row.ValueKind == JsonValueKind.Object && row.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out var n) ? n : null;
    public static string SuggestedName(string model) => model.StartsWith("WDC ", StringComparison.OrdinalIgnoreCase) || model.StartsWith("WDC_", StringComparison.OrdinalIgnoreCase) ? "WD " + model[4..] : model.StartsWith("ST", StringComparison.OrdinalIgnoreCase) ? "Seagate " + model : string.IsNullOrWhiteSpace(model) ? "Unknown model" : model;
}

internal sealed record DiskInventory(IdentityCandidate Identity, long? SizeBytes, string? DriveType, int? RotationRate, int? Enclosure, int? Slot);
