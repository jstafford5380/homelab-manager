using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;

namespace Homelab.Api;

public record CachedTemperature(string DeviceName, int? Celsius, string? MissingReason);
public record CachedTemperatureSnapshot(DateTime FetchedUtc, CachedTemperature[] Readings)
{
    public string SourceSchema => "truenas-24.10-temperature-cache-v1";
}
public static partial class CachedTemperatures
{
    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9_.-]{0,127}$", RegexOptions.CultureInvariant)]
    private static partial Regex DeviceName();
    public static CachedTemperatureSnapshot Normalize(JsonElement data, DateTime fetchedUtc)
    {
        if (data.ValueKind != JsonValueKind.Object) throw new InvalidDataException("Unexpected temperature cache shape.");
        var readings = new List<CachedTemperature>(); var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in data.EnumerateObject()) {
            if (readings.Count >= 256 || !DeviceName().IsMatch(entry.Name) || !names.Add(entry.Name))
                throw new InvalidDataException("Unexpected temperature cache entries.");
            var value = entry.Value.ValueKind == JsonValueKind.Number && entry.Value.TryGetInt32(out var n) && n >= -273 && n <= 1000 ? (int?)n : null;
            readings.Add(new(entry.Name, value, value is not null ? null : entry.Value.ValueKind == JsonValueKind.Null ? "No temperature in the cache." : "Reading does not match the supported integer Celsius cache schema/range."));
        }
        return new(fetchedUtc, readings.OrderBy(x => x.DeviceName, StringComparer.Ordinal).ToArray());
    }
    public static void MapCachedTemperatures(this RouteGroupBuilder api)
    {
        api.MapGet("/connections/{id:guid}/cached-temperatures", async (Guid id, HomelabDb db) => {
            var connection = await db.Connections.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id);
            if (connection is null) return Results.NotFound();
            var settings = await db.Settings.AsNoTracking().SingleOrDefaultAsync() ?? new OperatingSettings();
            var capability = await db.Capabilities.AsNoTracking().SingleOrDefaultAsync(x => x.ConnectionId == id && x.Name == "cached_temperatures");
            CachedTemperatureSnapshot? snapshot = null;
            try { if (connection.CachedTemperaturesJson is {} json) snapshot = JsonSerializer.Deserialize<CachedTemperatureSnapshot>(json); }
            catch (JsonException) { }
            return Results.Ok(new { enabled = settings.ReadCachedTemperatures,
                status = !settings.ReadCachedTemperatures ? "disabled" : !connection.Enabled ? "paused" : capability?.EffectiveStatus(DateTime.UtcNow) ?? "unavailable",
                lastAttemptUtc = capability?.LastAttemptUtc, lastSuccessUtc = capability?.LastSuccessUtc, snapshot });
        });
    }
}
