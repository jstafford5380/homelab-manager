using System.Text.Json;
using System.Text.RegularExpressions;

namespace Homelab.Api;
public static class PoolRedundancy
{
    private static readonly string[] Roles = ["data", "special", "dedup", "log", "cache", "spare"];
    public const string Limitation = "Configured redundancy describes layout, not remaining safe removals or recoverability. Group tolerances cannot be added across vdevs. Shared hardware, unreadable data and rebuild state are not assessed.";
    public static RedundancyAssessment Assess(JsonElement pool, bool fresh)
    {
        string Text(JsonElement node, string name) => node.ValueKind == JsonValueKind.Object && node.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString()! : "";
        var poolGuid = Text(pool, "guid");
        RedundancyAssessment Unknown(string reason) => new(poolGuid, "unavailable", reason + " " + Limitation, []);
        if (string.IsNullOrWhiteSpace(poolGuid))
            return Unknown("Pool identity is missing.");
        if (!fresh)
            return Unknown("Pool evidence is stale, unavailable or collection is paused.");
        if (!pool.TryGetProperty("topology", out var topology) || topology.ValueKind != JsonValueKind.Object)
            return Unknown("Topology is missing.");
        if (topology.EnumerateObject().Any(x => !Roles.Contains(x.Name)))
            return Unknown("An unsupported topology role is present.");
        var groups = new List<VdevLayout>();
        var identifiers = new HashSet<string>();
        var nodes = 0;
        bool Identity(JsonElement node) => ++nodes <= 4096 && !string.IsNullOrWhiteSpace(Text(node, "guid")) && identifiers.Add(Text(node, "guid"));
        bool Leaf(JsonElement node) => node.ValueKind == JsonValueKind.Object && Text(node, "type") == "DISK" && Identity(node) && (!node.TryGetProperty("children", out var children) || children.ValueKind == JsonValueKind.Array && children.GetArrayLength() == 0);
        foreach (var role in Roles)
        {
            if (!topology.TryGetProperty(role, out var roots) || roots.ValueKind != JsonValueKind.Array || role == "data" && roots.GetArrayLength() == 0)
                return Unknown("All topology roles and at least one data vdev must be reported.");
            foreach (var root in roots.EnumerateArray())
            {
                var type = Text(root, "type");
                var members = new List<JsonElement>();
                int? tolerance;
                if (type == "DISK")
                {
                    if (!Leaf(root))
                        return Unknown("Member identity or leaf structure is missing, duplicated or unsupported.");
                    members.Add(root);
                    tolerance = 0;
                }
                else
                {
                    if (!Identity(root) || !root.TryGetProperty("children", out var children) || children.ValueKind != JsonValueKind.Array)
                        return Unknown("Vdev identity or member structure is missing, duplicated or unsupported.");
                    foreach (var child in children.EnumerateArray())
                    {
                        if (!Leaf(child))
                            return Unknown("Nested replacement/spare trees or unsupported members require native review.");
                        members.Add(child);
                    }

                    if (type == "MIRROR" && members.Count >= 2)
                        tolerance = members.Count - 1;
                    else
                    {
                        var parity = type switch
                        {
                            "RAIDZ1" => 1,
                            "RAIDZ2" => 2,
                            "RAIDZ3" => 3,
                            _ => 0
                        };
                        if (type == "RAIDZ")
                        {
                            var match = Regex.Match(Text(root, "name"), "^raidz([1-3])-[0-9]+$", RegexOptions.CultureInvariant);
                            if (match.Success)
                                parity = match.Groups[1].Value[0] - '0';
                        }

                        if (parity == 0 || members.Count <= parity || role is "log" or "cache" or "spare")
                            return Unknown("This vdev layout or parity is not supported by the assessment.");
                        tolerance = parity;
                        type = "RAIDZ" + parity;
                    }
                }

                if (role is "cache" or "spare")
                    tolerance = null;
                var states = members.Select(x => Text(x, "status")).Append(Text(root, "status")).ToArray();
                if (states.Any(string.IsNullOrWhiteSpace))
                    return Unknown("A member or vdev state is missing.");
                groups.Add(new(role, Text(root, "guid"), type, members.Count, tolerance, states.All(x => x == "ONLINE" || role == "spare" && x == "AVAIL") ? role == "spare" ? "reported available" : "reported online" : "requires native review"));
            }
        }

        var degraded = Text(pool, "status") != "ONLINE" || groups.Any(x => x.State == "requires native review");
        var noRedundancy = groups.Any(x => x.Role is "data" or "special" or "dedup" && x.ConfiguredTolerance == 0);
        return new(poolGuid, degraded ? "review reported state" : noRedundancy ? "nonredundant data role" : "layout recognized", (degraded ? "At least one pool/member state requires review. No remaining-failure allowance is inferred. " : noRedundancy ? "A data, special or dedup group has no configured disk redundancy. " : "Simple vdev layouts are recognized in the latest pool evidence. ") + Limitation, groups.ToArray());
    }

    public static RedundancyAssessment[] FromStored(string? json, bool fresh)
    {
        if (string.IsNullOrWhiteSpace(json) || json.Length > 4 * 1024 * 1024)
            return[];
        try
        {
            using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 32 });
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
                return[];
            return doc.RootElement.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.Object).Select(x => Assess(x, fresh)).ToArray();
        }
        catch (JsonException)
        {
            return[];
        }
    }
}
