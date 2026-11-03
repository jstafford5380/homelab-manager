using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace Homelab.Api;
public static class DiscoveryAssignments
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
    public static string? Capture(Guid revision, DateTime at, IdentityCandidate[] rows)
    {
        var json = JsonSerializer.Serialize(new DiscoverySnapshot(revision, at, rows), Options);
        return rows.Length <= 4096 && json.Length <= 256 * 1024 ? json : null;
    }

    public static DiscoverySnapshot? Read(NasConnection nas)
    {
        try
        {
            if (nas.DiscoveryIdentityJson is not { } json || json.Length > 256 * 1024)
                return null;
            var value = JsonSerializer.Deserialize<DiscoverySnapshot>(json, Options);
            return value is { Rows: not null } && value.Rows.Length <= 4096 && value.Rows.All(x => x is not null && x.Serial is not null && x.Model is not null && x.Alias is not null) ? value : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static bool Usable(NasConnection nas, DiscoverySnapshot? snapshot, DateTime now) => nas.Enabled && snapshot is not null && snapshot.Revision == nas.ConfigurationRevision && snapshot.FetchedUtc <= now && snapshot.FetchedUtc >= now.AddDays(-1);
    public static bool Compatible(IEnumerable<IdentityCandidate> rows) => rows.Any() && rows.All(x => IdentityCandidate.NormalizeWwn(x.Wwn) != null && !string.IsNullOrWhiteSpace(x.Model)) && rows.Select(x => (IdentityCandidate.NormalizeWwn(x.Wwn), x.Serial.Trim(), x.Model.Trim())).Distinct().Count() == 1;
    public static bool AliasConsistent(IEnumerable<IdentityCandidate> group, IEnumerable<IdentityCandidate> all) => !group.Any(x => all.Any(other => other.Alias == x.Alias && other.Key != x.Key));
    public static Disk? Target(DiscoveryAssignment decision, IEnumerable<IdentityCandidate> rows, IReadOnlyList<Disk> disks, Guid revision, DateTime at)
    {
        if (decision.RevokedUtc != null || decision.ValidUntilUtc <= at || decision.ConfigurationRevision != revision || !Compatible(rows))
            return null;
        var row = rows.First();
        if (IdentityCandidate.NormalizeWwn(row.Wwn) != decision.Wwn || row.Serial.Trim() != decision.Serial || row.Model.Trim() != decision.Model)
            return null;
        return disks.SingleOrDefault(x => x.Id == decision.DiskId && IdentityCandidate.NormalizeWwn(x.Wwn) == decision.Wwn && x.Serial.Trim() == decision.Serial && x.Model.Trim() == decision.Model && x.Lifecycle != "retired");
    }

    internal static string Hash<T>(T value) => Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(value)));
    public static string Token(NasConnection nas, DiscoverySnapshot snapshot, IEnumerable<Disk> disks, IEnumerable<DiscoveryAssignment> assignments) => Hash(new { nas.ConfigurationRevision, rows = snapshot.Rows.OrderBy(x => x.Key).ThenBy(x => x.Alias).ThenBy(x => x.Serial).ThenBy(x => x.Model), records = disks.OrderBy(x => x.Id).Select(x => new { x.Id, x.Serial, x.Model, x.Wwn, x.MappingConfidence, x.Lifecycle }), assignments = assignments.Where(x => x.RevokedUtc == null).OrderBy(x => x.Id).Select(x => new { x.Id, x.ValidUntilUtc }) });
}
