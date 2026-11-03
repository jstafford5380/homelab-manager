using System.Text.Json;

namespace Homelab.Api;
public static class VdevEvidence
{
    public const string Schema = "truenas-24.10-vdev-evidence-v1";
    private static readonly string[] Roles = ["data", "special", "dedup", "log", "cache", "spare"];
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    public const int MaximumLength = 128 * 1024;
    public static string Capture(JsonElement pool)
    {
        var json = JsonSerializer.Serialize(Parse(pool), JsonOptions);
        return json.Length <= MaximumLength ? json : JsonSerializer.Serialize(new VdevSnapshotEvidence(Schema, "unavailable", "Curated vdev evidence exceeded the 128 KiB storage bound.", []), JsonOptions);
    }

    public static VdevSnapshotEvidence Read(string json)
    {
        if (json.Length > MaximumLength)
            throw new InvalidDataException("Stored vdev evidence exceeds its bound.");
        var parsed = JsonSerializer.Deserialize<VdevSnapshotEvidence>(json, JsonOptions) ?? throw new InvalidDataException("Stored vdev evidence is missing.");
        if (parsed.Schema != Schema || parsed.Nodes is null)
            throw new InvalidDataException("Stored vdev evidence schema is unsupported.");
        return parsed;
    }

    public static string? Context(string? json, bool includeMapping = true)
    {
        if (json is null)
            return null;
        var parsed = Read(json);
        return JsonSerializer.Serialize(parsed with { Nodes = parsed.Nodes.Select(x => x with { ReadErrors = null, WriteErrors = null, ChecksumErrors = null, DriveMapping = includeMapping ? x.DriveMapping : null }).ToArray() }, JsonOptions);
    }

    public static VdevSnapshotEvidence Parse(JsonElement pool)
    {
        VdevSnapshotEvidence Unknown(string reason) => new(Schema, "unavailable", reason, []);
        var topology = Get(pool, "topology");
        if (topology.ValueKind != JsonValueKind.Object)
            return Unknown("Topology was not reported as an object.");
        if (topology.EnumerateObject().Any(x => !Roles.Contains(x.Name)) || Duplicate(topology))
            return Unknown("Unsupported or duplicated topology roles.");
        var rows = new List<VdevNodeEvidence>();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        bool Visit(JsonElement node, string role, string? parent, int depth)
        {
            if (depth > 24 || rows.Count >= 4096 || node.ValueKind != JsonValueKind.Object || Duplicate(node))
                return false;
            var guid = Identifier(Get(node, "guid"));
            var type = Text(node, "type", 64);
            if (guid is null || type is null || !ids.Add(guid))
                return false;
            var stats = Get(node, "stats");
            if (stats.ValueKind == JsonValueKind.Object && Duplicate(stats))
                return false;
            rows.Add(new(guid, parent, role, type, Text(node, "status", 64), Text(node, "path", 1024), Text(node, "device", 256), Text(node, "disk", 256), Count(stats, "read_errors"), Count(stats, "write_errors"), Count(stats, "checksum_errors")));
            var children = Get(node, "children");
            if (children.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
                return true;
            if (children.ValueKind != JsonValueKind.Array)
                return false;
            foreach (var child in children.EnumerateArray())
                if (!Visit(child, role, guid, depth + 1))
                    return false;
            return true;
        }

        foreach (var role in Roles)
        {
            var roots = Get(topology, role);
            if (roots.ValueKind == JsonValueKind.Undefined)
                continue;
            if (roots.ValueKind != JsonValueKind.Array)
                return Unknown("A topology role was not an array.");
            foreach (var root in roots.EnumerateArray())
                if (!Visit(root, role, null, 0))
                    return Unknown("Vdev identities or tree structure are missing, duplicated, malformed or beyond supported bounds.");
        }

        return new(Schema, "reported", "Reported ZFS objects and counters only. GUIDs are not physical drive identities; device/partition names are source-reported aliases, not verified disk mappings. Missing, malformed or out-of-range counters remain unknown. Counter changes, reset/reboot boundaries and repair are not inferred from this snapshot.", rows.OrderBy(x => x.Role, StringComparer.Ordinal).ThenBy(x => x.Guid, StringComparer.Ordinal).ToArray());
    }

    private static JsonElement Get(JsonElement row, string name) => row.ValueKind == JsonValueKind.Object && row.TryGetProperty(name, out var value) ? value : default;
    private static bool Duplicate(JsonElement row) => row.EnumerateObject().Select(x => x.Name).Distinct(StringComparer.Ordinal).Count() != row.EnumerateObject().Count();
    private static string? Text(JsonElement row, string name, int max)
    {
        var value = Get(row, name);
        if (value.ValueKind != JsonValueKind.String)
            return null;
        var text = value.GetString();
        return string.IsNullOrWhiteSpace(text) || text.Length > max || text.Any(char.IsControl) ? null : text;
    }

    private static string? Identifier(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Number && value.TryGetUInt64(out var number) && number > 0)
            return number.ToString(System.Globalization.CultureInfo.InvariantCulture);
        if (value.ValueKind != JsonValueKind.String)
            return null;
        var text = value.GetString()!;
        return string.IsNullOrWhiteSpace(text) || text.Length > 128 || text.Any(char.IsControl) || text.All(x => x == '0') ? null : text;
    }

    private static long? Count(JsonElement row, string name)
    {
        var value = Get(row, name);
        return value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var number) && number >= 0 && number <= 9007199254740991L ? number : null;
    }
}
