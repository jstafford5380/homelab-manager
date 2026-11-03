using System.Globalization;
using System.Text.Json;

namespace Homelab.Api;
public static class ApiPowerOnHours
{
    public static ApiPowerOnSnapshot? Read(string? json)
    {
        try
        {
            return json is null ? null : JsonSerializer.Deserialize<ApiPowerOnSnapshot>(json);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static bool Due(NasConnection nas, string? json, DateTime now) => Read(json)is not { } snapshot || snapshot.Revision != nas.ConfigurationRevision || snapshot.FetchedUtc > now || now - snapshot.FetchedUtc >= TimeSpan.FromHours(6);
    public static long? Normalize(JsonElement attributes)
    {
        if (attributes.ValueKind != JsonValueKind.Array || attributes.GetArrayLength() > 256)
            throw new InvalidDataException();
        // Attribute 9 alone is insufficient: some vendors use other units. Require smartctl's
        // explicit hours label and a plain numeric raw value, never the normalized health score.
        var rows = attributes.EnumerateArray().Where(x => StorageInventory.Text(x, "name") == "Power_On_Hours").ToArray();
        if (rows.Length != 1)
            return null;
        var row = rows[0];
        if (!row.TryGetProperty("id", out var id) || id.ValueKind != JsonValueKind.Number || !id.TryGetInt32(out var number) || number != 9 || !row.TryGetProperty("raw", out var raw) || raw.ValueKind != JsonValueKind.Object || !raw.TryGetProperty("value", out var value) || value.ValueKind != JsonValueKind.Number || !value.TryGetInt64(out var hours) || hours < 0 || hours > 9007199254740991L || !long.TryParse(StorageInventory.Text(raw, "string"), NumberStyles.None, CultureInfo.InvariantCulture, out var labelled) || labelled != hours)
            return null;
        return hours;
    }

    private static string Text(JsonElement row, string key) => StorageInventory.Text(row, key);
    private static bool Same(JsonElement row, ApiPowerOnReading reading) => Text(row, "name") == reading.Device && Text(row, "serial") == reading.Serial && Text(row, "model") == reading.Model && IdentityCandidate.NormalizeWwn(Text(row, "wwn")) == reading.Wwn;
    // Only current inventory names are accepted, at most 64 disks per server. The second
    // inventory rejects devices replaced or renamed while SMART reads were in progress.
    public static async Task<ApiPowerOnSnapshot> CollectAsync(INasReadClient client, JsonElement inventory, Guid revision, CancellationToken ct, string? previousJson = null)
    {
        if (inventory.ValueKind != JsonValueKind.Array || inventory.GetArrayLength() > 64)
            throw new InvalidDataException();
        var previous = Read(previousJson);
        var rows = inventory.EnumerateArray().ToArray();
        var readings = new List<ApiPowerOnReading>();
        foreach (var row in rows)
        {
            var device = Text(row, "name");
            if (!TrueNasClient.ValidSmartDisk(device) || string.IsNullOrWhiteSpace(Text(row, "serial")) || string.IsNullOrWhiteSpace(Text(row, "model")) || rows.Count(x => Text(x, "name") == device) != 1)
                continue;
            string status;
            long? hours = null;
            if (device.StartsWith("nvme", StringComparison.Ordinal))
                status = "unsupported";
            else
            {
                try
                {
                    hours = Normalize(await client.ReadSmartAttributesAsync(device, ct));
                    status = hours is null ? "not_reported" : "supported";
                }
                catch (NasReadException ex)
                {
                    status = ex.Status;
                }
                catch (InvalidDataException)
                {
                    status = "unavailable";
                }
            }

            var reading = new ApiPowerOnReading(device, Text(row, "serial"), Text(row, "model"), IdentityCandidate.NormalizeWwn(Text(row, "wwn")), status, hours, hours is null ? null : DateTime.UtcNow);
            if (status is "denied" or "unavailable" && previous?.Revision == revision)
            {
                var old = previous.Readings.Where(r => Same(row, r)).ToArray();
                if (old.Length == 1 && old[0].Hours is not null)
                    reading = reading with
                    {
                        Hours = old[0].Hours,
                        ReadUtc = old[0].ReadUtc ?? previous.FetchedUtc
                    };
            }

            readings.Add(reading);
        }

        var after = await client.ReadAsync("disk.query", ct);
        if (after.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException();
        return new(revision, DateTime.UtcNow, readings.Where(r => after.EnumerateArray().Count(x => Same(x, r)) == 1).ToArray());
    }

    public static StoragePowerOnHours ForDrive(NasConnection nas, Disk disk, bool enabled, DateTime now, string? json)
    {
        if (!enabled)
            return new("disabled", null, null, "API power-on hours collection is off in Settings.");
        var snapshot = Read(json);
        var matches = snapshot?.Readings.Where(r => r.Device == disk.Alias && r.Serial == disk.Serial && r.Model == disk.Model && r.Wwn == IdentityCandidate.NormalizeWwn(disk.Wwn)).ToArray() ?? [];
        if (snapshot is null || snapshot.Revision != nas.ConfigurationRevision || matches.Length != 1 || !disk.Present || disk.MappingConfidence == "ambiguous")
            return new("unavailable", null, null, "No API reading could be matched to this drive.");
        var reading = matches[0];
        if (snapshot.FetchedUtc > now || now - snapshot.FetchedUtc > TimeSpan.FromHours(18) || !nas.Enabled)
            return new("stale", reading.Hours, reading.ReadUtc ?? snapshot.FetchedUtc, "Last known API reading; collection is paused or out of date.");
        return new(reading.Status, reading.Hours, reading.ReadUtc ?? snapshot.FetchedUtc, reading.Status switch
        {
            "supported" => "Power-on hours reported by ATA SMART through the TrueNAS API.",
            "unsupported" => "This drive is not supported by the TrueNAS ATA SMART attributes API.",
            "denied" => "The API credential cannot read SMART attributes for this drive.",
            "not_reported" => "The drive did not return an unambiguous power-on hours counter.",
            _ => "The API SMART read was unavailable; this does not establish that the drive is unsupported."
        });
    }
}
