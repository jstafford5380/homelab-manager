using System.Text.Json;
using Microsoft.EntityFrameworkCore;
namespace Homelab.Api;

// Dataset paths are labels, not durable identity. This adapter intentionally does not
// infer renames, removals, pool GUIDs, health, or historical continuity from these names.
public record DatasetUsage(string Name, string Pool, string Type, long? UsedBytes, long? AvailableBytes,
    long? SnapshotUsedBytes, long? QuotaBytes, long? RefQuotaBytes);
public record DatasetSnapshot(DateTime FetchedUtc, DatasetUsage[] Items)
{
    public string SourceSchema => "truenas-24.10-datasets-v1";
}
public static class Datasets
{
    public static DatasetSnapshot Normalize(JsonElement data, DateTime fetchedUtc)
    {
        if (data.ValueKind != JsonValueKind.Array || data.GetArrayLength() > 256)
            throw new InvalidDataException("Dataset inventory shape or count exceeds the supported bound.");
        var items = new List<DatasetUsage>();
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var row in data.EnumerateArray()) {
            if (row.ValueKind != JsonValueKind.Object || row.EnumerateObject().Select(x => x.Name).Distinct(StringComparer.Ordinal).Count() != row.EnumerateObject().Count())
                throw new InvalidDataException("Ambiguous dataset row.");
            var name = Text(row, "name"); var pool = Text(row, "pool"); var type = Text(row, "type");
            if (name is null || pool is null || Text(row, "id") != name || type is not ("FILESYSTEM" or "VOLUME") ||
                name.Length > 256 || name.Any(char.IsControl) || pool != name.Split('/')[0] ||
                name.Split('/').Any(x => string.IsNullOrWhiteSpace(x) || x is "." or "..") || !names.Add(name))
                throw new InvalidDataException("Unsupported dataset identity or type.");
            items.Add(new(name, pool, type, Bytes(row, "used"), Bytes(row, "available"), Bytes(row, "usedbysnapshots"), Bytes(row, "quota"), Bytes(row, "refquota")));
        }
        return new(fetchedUtc, items.OrderBy(x => x.Name, StringComparer.Ordinal).ToArray());
    }
    private static string? Text(JsonElement row, string field) =>
        row.TryGetProperty(field, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    private static long? Bytes(JsonElement row, string field)
    {
        if (!row.TryGetProperty(field, out var property) || property.ValueKind != JsonValueKind.Object ||
            property.EnumerateObject().Select(x => x.Name).Distinct(StringComparer.Ordinal).Count() != property.EnumerateObject().Count() ||
            !property.TryGetProperty("parsed", out var parsed) || parsed.ValueKind != JsonValueKind.Number ||
            !parsed.TryGetInt64(out var number) || number < 0 || number > 9007199254740991) return null;
        return number;
    }
    public static async Task RecordCapabilityAsync(HomelabDb db, NasConnection nas, DateTime started, string status, CancellationToken ct)
    {
        var capability = db.Capabilities.Local.SingleOrDefault(x => x.ConnectionId == nas.Id && x.Name == "datasets") ??
            await db.Capabilities.SingleOrDefaultAsync(x => x.ConnectionId == nas.Id && x.Name == "datasets", ct);
        if (capability is null) { capability = new() { ConnectionId = nas.Id, Name = "datasets" }; db.Capabilities.Add(capability); }
        capability.Status = status; capability.ExpectedSeconds = nas.PollSeconds;
        if (status != "disabled") capability.LastAttemptUtc = started;
        if (status == "supported") capability.LastSuccessUtc = DateTime.UtcNow;
        capability.Detail = status == "disabled" ? "Optional dataset usage collection is off in Settings." :
            "Read-only dataset usage, up to 256 entries. Names are not permanent identities; dataset totals are not pool capacity. Failed reads retain the last saved snapshot.";
    }
    public static void MapDatasets(this RouteGroupBuilder api)
    {
        api.MapDatasetHistory();
        api.MapDatasetRenames();
        api.MapGet("/connections/{id:guid}/datasets", async (Guid id, HomelabDb db, CancellationToken ct) => {
            var nas = await db.Connections.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct);
            if (nas is null) return Results.NotFound();
            var settings = await db.Settings.AsNoTracking().SingleOrDefaultAsync(ct) ?? new OperatingSettings();
            var capability = await db.Capabilities.AsNoTracking().SingleOrDefaultAsync(x => x.ConnectionId == id && x.Name == "datasets", ct);
            var now = DateTime.UtcNow;
            DatasetSnapshot? snapshot = null;
            try { if (nas.DatasetsJson is {} json) snapshot = JsonSerializer.Deserialize<DatasetSnapshot>(json); }
            catch (JsonException) { }
            var validUntilUtc = capability?.LastSuccessUtc?.AddSeconds(3L * nas.PollSeconds);
            return Results.Ok(new { enabled = settings.ReadDatasets,
                status = !settings.ReadDatasets ? "disabled" : !nas.Enabled ? "paused" : capability?.EffectiveStatus(now) ?? "unavailable",
                validUntilUtc, snapshot });
        });
    }
}
