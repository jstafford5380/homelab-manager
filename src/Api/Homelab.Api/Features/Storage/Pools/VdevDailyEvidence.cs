using System.Text.Json;

namespace Homelab.Api;
public static class VdevDailyEvidence
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
    public static string? Add(string? summary, string? snapshot)
    {
        if (snapshot is null)
            return summary;
        var source = VdevEvidence.Read(snapshot);
        var prior = summary is null ? null : JsonSerializer.Deserialize<VdevDailySnapshot>(summary, Options) ?? throw new InvalidDataException("Stored vdev daily summary is missing.");
        if (prior is not null && (prior.Schema != "truenas-24.10-vdev-daily-v1" || prior.Nodes is null || prior.Samples < 1 || prior.Nodes.Length != source.Nodes.Length))
            throw new InvalidDataException("Vdev summary context changed.");
        var rows = new List<VdevDailyNode>();
        for (var i = 0; i < source.Nodes.Length; i++)
        {
            var row = source.Nodes[i];
            var node = row with
            {
                ReadErrors = null,
                WriteErrors = null,
                ChecksumErrors = null
            };
            var old = prior?.Nodes[i];
            if (old is not null && old.Node != node)
                throw new InvalidDataException("Vdev summary identity changed.");
            rows.Add(new(node, (old?.Read ?? new(0, null, null)).Add(row.ReadErrors), (old?.Write ?? new(0, null, null)).Add(row.WriteErrors), (old?.Checksum ?? new(0, null, null)).Add(row.ChecksumErrors)));
        }

        return JsonSerializer.Serialize(new VdevDailySnapshot("truenas-24.10-vdev-daily-v1", source.Status, "Historical counter ranges and known sample counts, grouped by the same reported member/state/alias context. Ranges are not error deltas or proof of continuity; lower counts do not establish repair.", (prior?.Samples ?? 0) + 1, rows.ToArray()), Options);
    }
}
