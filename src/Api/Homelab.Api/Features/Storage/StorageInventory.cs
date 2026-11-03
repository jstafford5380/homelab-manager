using System.Text.Json;

namespace Homelab.Api;
public static class StorageInventory
{
    public static string Identity(Disk disk) => JsonSerializer.Serialize(new { disk.IdentityKey, disk.Serial, disk.Model, wwn = IdentityCandidate.NormalizeWwn(disk.Wwn) });
    public static bool BayConfirmed(Disk disk) => disk.BayVerified && disk.MappingConfidence != "ambiguous" && disk.BayConfirmedIdentity == Identity(disk) && !string.IsNullOrWhiteSpace(disk.Bay);
    public static void Attach(IReadOnlyList<Disk> disks, JsonElement inventory)
    {
        foreach (var disk in disks.Where(d => d.Present && d.MappingConfidence != "ambiguous"))
        {
            var rows = inventory.EnumerateArray().Where(r => Text(r, "name") == disk.Alias && Text(r, "serial") == disk.Serial && Text(r, "model") == disk.Model).ToArray();
            if (rows.Length != 1)
                continue;
            var row = rows[0];
            disk.SizeBytes = Number(row, "size")is> 0 and <= 9007199254740991L ? Number(row, "size") : null;
            disk.DriveType = Text(row, "type")is "HDD" or "SSD" ? Text(row, "type") : null;
            disk.RotationRate = Number(row, "rotationrate")is> 0 and <= 100000 ? (int? )Number(row, "rotationrate") : null;
            var enclosure = row.TryGetProperty("enclosure", out var e) && e.ValueKind == JsonValueKind.Object ? e : default;
            disk.Enclosure = Number(enclosure, "number")is >= 0 and <= int.MaxValue ? (int? )Number(enclosure, "number") : null;
            disk.Slot = Number(enclosure, "slot")is >= 0 and <= int.MaxValue ? (int? )Number(enclosure, "slot") : null;
        }
    }

    public static string Text(JsonElement row, string key) => row.ValueKind == JsonValueKind.Object && row.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString()! : "";
    private static long? Number(JsonElement row, string key) => row.ValueKind == JsonValueKind.Object && row.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out var n) ? n : null;
    public static string SuggestedName(string model) => model.StartsWith("WDC ", StringComparison.OrdinalIgnoreCase) || model.StartsWith("WDC_", StringComparison.OrdinalIgnoreCase) ? "WD " + model[4..] : model.StartsWith("ST", StringComparison.OrdinalIgnoreCase) ? "Seagate " + model : string.IsNullOrWhiteSpace(model) ? "Unknown model" : model;
}
