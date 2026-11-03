using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace Homelab.Api;
public record IdentityHistoryGraph(Disk[] Disks, IdentityHistoryMerge[] Links)
{
    public Guid[] Group(Guid id)
    {
        var seen = new HashSet<Guid>
        {
            id
        };
        var queue = new Queue<Guid>();
        queue.Enqueue(id);
        while (queue.TryDequeue(out var next))
            foreach (var link in Links.Where(x => x.ReversedUtc == null && (x.FirstDiskId == next || x.SecondDiskId == next)))
            {
                var other = link.FirstDiskId == next ? link.SecondDiskId : link.FirstDiskId;
                if (seen.Add(other))
                    queue.Enqueue(other);
            }

        return seen.Order().ToArray();
    }

    public string Token(IEnumerable<Guid> members)
    {
        var ids = members.ToHashSet();
        var data = new
        {
            records = Disks.Where(x => ids.Contains(x.Id)).OrderBy(x => x.Id).Select(x => new { x.Id, x.ConnectionId, x.Serial, x.Model, x.Wwn, x.IdentityKey, x.MappingConfidence, x.Present, x.Lifecycle, x.Bay, x.BayVerified }),
            links = Links.Where(x => x.ReversedUtc == null && (ids.Contains(x.FirstDiskId) || ids.Contains(x.SecondDiskId))).OrderBy(x => x.Id).Select(x => new { x.Id, x.FirstDiskId, x.SecondDiskId })
        };
        return Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(data)));
    }
}
