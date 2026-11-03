using System.Text.Json;

namespace Homelab.Api;
public static class StorageDriveGroups
{
    public static Dictionary<string, StorageDriveGroup> Map(IReadOnlyList<VdevNodeEvidence> nodes, string? topologyJson, string poolGuid)
    {
        var names = new Dictionary<string, string>(StringComparer.Ordinal);
        try
        {
            using var json = JsonDocument.Parse(topologyJson ?? "[]");
            if (json.RootElement.ValueKind == JsonValueKind.Array)
                foreach (var pool in json.RootElement.EnumerateArray().Where(p => StorageInventory.Text(p, "guid") == poolGuid))
                    if (pool.TryGetProperty("topology", out var topology) && topology.ValueKind == JsonValueKind.Object)
                        foreach (var role in topology.EnumerateObject())
                            if (role.Value.ValueKind == JsonValueKind.Array)
                                foreach (var root in role.Value.EnumerateArray())
                                {
                                    var guid = StorageInventory.Text(root, "guid");
                                    var name = StorageInventory.Text(root, "name");
                                    if (guid.Length > 0 && name.Length > 0)
                                        names.TryAdd(guid, name);
                                }
        }
        catch (JsonException)
        {
        }

        var lookup = nodes.GroupBy(n => n.Guid).Where(g => g.Count() == 1).ToDictionary(g => g.Key, g => g.Single(), StringComparer.Ordinal);
        var roots = nodes.Where(n => n.ParentGuid is null).OrderBy(n => n.Role, StringComparer.Ordinal).ThenBy(n => n.Guid, StringComparer.Ordinal).ToArray();
        var result = new Dictionary<string, StorageDriveGroup>(StringComparer.Ordinal);
        foreach (var leaf in nodes.Where(n => n.Type == "DISK"))
        {
            var current = leaf;
            var seen = new HashSet<string>(StringComparer.Ordinal);
            while (current.ParentGuid is { } parent && seen.Add(current.Guid) && lookup.TryGetValue(parent, out var ancestor) && ancestor.Role == leaf.Role)
                current = ancestor;
            // Missing parents, cross-role links and cycles do not establish membership.
            if (current.ParentGuid is not null || !lookup.ContainsKey(current.Guid))
                continue;
            var layout = current.Type switch
            {
                "MIRROR" => "Mirror",
                "DISK" => "Single drive",
                _ => current.Type
            };
            var name = names.GetValueOrDefault(current.Guid) ?? layout + " group " + (Array.IndexOf(roots, current) + 1);
            result[leaf.Guid] = new(current.Guid, name, leaf.Role);
        }

        return result;
    }
}
