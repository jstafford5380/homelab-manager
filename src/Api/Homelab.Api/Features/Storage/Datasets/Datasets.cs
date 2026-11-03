using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace Homelab.Api;
public static class Datasets
{
    public static DatasetUsage[] ForStoragePool(DatasetSnapshot? snapshot, string poolName)
    {
        var rows = snapshot?.Items.Where(x => x.Pool == poolName).ToArray() ?? [];
        var children = rows.Where(x => x.Name != poolName).ToArray();
        // A root-only response is still useful usage data. When children exist,
        // omit their cumulative parent rather than counting it a second time.
        return (children.Length > 0 ? children : rows).OrderByDescending(x => x.UsedBytes).ToArray();
    }

    public static DatasetSnapshot Normalize(JsonElement data, DateTime fetchedUtc)
    {
        if (data.ValueKind != JsonValueKind.Array || data.GetArrayLength() > 256)
            throw new InvalidDataException("Dataset inventory shape or count exceeds the supported bound.");
        var items = new List<DatasetUsage>();
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var row in data.EnumerateArray())
        {
            if (row.ValueKind != JsonValueKind.Object || row.EnumerateObject().Select(x => x.Name).Distinct(StringComparer.Ordinal).Count() != row.EnumerateObject().Count())
                throw new InvalidDataException("Ambiguous dataset row.");
            var name = Text(row, "name");
            var pool = Text(row, "pool");
            var type = Text(row, "type");
            if (name is null || pool is null || Text(row, "id") != name || type is not ("FILESYSTEM" or "VOLUME") || name.Length > 256 || name.Any(char.IsControl) || pool != name.Split('/')[0] || name.Split('/').Any(x => string.IsNullOrWhiteSpace(x) || x is "." or "..") || !names.Add(name))
                throw new InvalidDataException("Unsupported dataset identity or type.");
            items.Add(new(name, pool, type, Bytes(row, "used"), Bytes(row, "available"), Bytes(row, "usedbysnapshots"), Bytes(row, "quota"), Bytes(row, "refquota")));
        }

        return new(fetchedUtc, items.OrderBy(x => x.Name, StringComparer.Ordinal).ToArray());
    }

    internal static string? Text(JsonElement row, string field) => row.TryGetProperty(field, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    internal static long? Bytes(JsonElement row, string field)
    {
        if (!row.TryGetProperty(field, out var property) || property.ValueKind != JsonValueKind.Object || property.EnumerateObject().Select(x => x.Name).Distinct(StringComparer.Ordinal).Count() != property.EnumerateObject().Count() || !property.TryGetProperty("parsed", out var parsed) || parsed.ValueKind != JsonValueKind.Number || !parsed.TryGetInt64(out var number) || number < 0 || number > 9007199254740991)
            return null;
        return number;
    }

    public static async Task RecordCapabilityAsync(HomelabDb db, NasConnection nas, DateTime started, string status, CancellationToken ct)
    {
        var capability = db.Capabilities.Local.SingleOrDefault(x => x.ConnectionId == nas.Id && x.Name == "datasets") ?? await db.Capabilities.SingleOrDefaultAsync(x => x.ConnectionId == nas.Id && x.Name == "datasets", ct);
        if (capability is null)
        {
            capability = new()
            {
                ConnectionId = nas.Id,
                Name = "datasets"
            };
            db.Capabilities.Add(capability);
        }

        capability.Status = status;
        capability.ExpectedSeconds = nas.PollSeconds;
        if (status != "disabled")
            capability.LastAttemptUtc = started;
        if (status == "supported")
            capability.LastSuccessUtc = DateTime.UtcNow;
        capability.Detail = status == "disabled" ? "Optional dataset usage collection is off in Settings." : "Read-only dataset usage, up to 256 entries. Names are not permanent identities; dataset totals are not pool capacity. Failed reads retain the last saved snapshot.";
    }
}
