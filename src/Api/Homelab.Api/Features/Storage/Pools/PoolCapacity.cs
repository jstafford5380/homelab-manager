using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace Homelab.Api;
public static class PoolCapacity
{
    public static string? TopologySignature(JsonElement row)
    {
        if (!row.TryGetProperty("topology", out var topology) || topology.ValueKind != JsonValueKind.Object)
            return null;
        var roles = new[]
        {
            "data",
            "log",
            "cache",
            "spare",
            "special",
            "dedup"
        };
        var nodes = 0;
        string Node(JsonElement node, int depth)
        {
            if (++nodes > 4096 || depth > 20 || node.ValueKind != JsonValueKind.Object)
                throw new FormatException();
            if (!node.TryGetProperty("guid", out var guid) || guid.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(guid.GetString()) || !node.TryGetProperty("type", out var type) || type.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(type.GetString()))
                throw new FormatException();
            var children = new List<string>();
            if (node.TryGetProperty("children", out var child))
            {
                if (child.ValueKind != JsonValueKind.Array)
                    throw new FormatException();
                children.AddRange(child.EnumerateArray().Select(x => Node(x, depth + 1)));
            }

            children.Sort(StringComparer.Ordinal);
            // Device paths and counters can change without changing membership.
            var name = type.GetString() == "DISK" ? null : node.TryGetProperty("name", out var n) ? n.ToString() : null;
            return JsonSerializer.Serialize(new { guid = guid.GetString(), type = type.GetString(), name, children });
        }

        try
        {
            if (topology.EnumerateObject().Any(x => !roles.Contains(x.Name)))
                return null;
            var parts = new List<string>();
            foreach (var role in roles)
            {
                if (!topology.TryGetProperty(role, out var members) || members.ValueKind != JsonValueKind.Array || role == "data" && members.GetArrayLength() == 0)
                    return null;
                parts.Add(role + ":" + JsonSerializer.Serialize(members.EnumerateArray().Select(x => Node(x, 0)).Order(StringComparer.Ordinal)));
            }

            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join(";", parts))));
        }
        catch (FormatException)
        {
            return null;
        }
    }

    public sealed class Window(int days, DateTime now, TimeSpan maxAge)
    {
        private readonly SortedDictionary<DateTime, PoolSnapshot> daily = new();
        private PoolSnapshot? first, last;
        private int count;
        private string? reason;
        public void Add(PoolSnapshot row)
        {
            if (row.FetchedUtc < now.AddDays(-days) || row.FetchedUtc > now)
                return;
            count++;
            if (count > 300000)
            {
                reason = "Too many samples to assess this window safely.";
                return;
            }

            first ??= row;
            if (row.SizeBytes is null or <= 0 || row.AllocatedBytes is null or < 0 || row.AllocatedBytes > row.SizeBytes || row.TopologySignature is null || row.Status != "ONLINE")
                reason ??= "This window contains missing capacity, unknown topology, or a pool that was not online.";
            if (last is not null)
            {
                if (row.FetchedUtc <= last.FetchedUtc)
                    reason ??= "Sample times are duplicated or out of order.";
                if (row.FetchedUtc - last.FetchedUtc > TimeSpan.FromDays(2))
                    reason ??= "A collection gap exceeds two days.";
                if (row.SizeBytes != last.SizeBytes || row.TopologySignature != last.TopologySignature || row.SourceSchema != last.SourceSchema)
                    reason ??= "Pool size, layout or source format changed within this window.";
                if (last.AllocatedBytes - (double? )row.AllocatedBytes > row.SizeBytes * .01)
                    reason ??= "A usage drop exceeded 1% of pool capacity; wait for a stable window after deletions.";
            }

            daily[row.FetchedUtc.Date] = row;
            last = row;
        }

        public CapacityEstimate Finish()
        {
            CapacityEstimate Result(string status, string explanation, double? rate = null, double? eta = null, double? fit = null) => new(days, status, explanation, count, daily.Count, first?.FetchedUtc, last?.FetchedUtc, last?.SizeBytes, last?.AllocatedBytes, rate, eta, fit);
            if (last is null)
                return Result("insufficient", "No detailed capacity samples in this window.");
            if (now - last.FetchedUtc > maxAge)
                return Result("stale", "Latest pool measurement is stale. Restore collection before using an estimate.");
            if (reason is not null)
                return Result("suppressed", reason);
            if (daily.Count < 7 || (last.FetchedUtc - first!.FetchedUtc).TotalDays < days * .8)
                return Result("insufficient", "Need at least seven sampled UTC days spanning 80% of the selected window. Retained daily ranges cannot substitute for detailed topology evidence.");
            var points = daily.Values.Select(x => (X: (x.FetchedUtc - first!.FetchedUtc).TotalDays, Y: (double)x.AllocatedBytes!.Value)).ToArray();
            var mx = points.Average(x => x.X);
            var my = points.Average(x => x.Y);
            var slope = points.Sum(x => (x.X - mx) * (x.Y - my)) / points.Sum(x => Math.Pow(x.X - mx, 2));
            var total = points.Sum(x => Math.Pow(x.Y - my, 2));
            var residual = points.Sum(x => Math.Pow(x.Y - (my + slope * (x.X - mx)), 2));
            var fit = total == 0 ? 0 : Math.Clamp(1 - residual / total, 0, 1);
            var remaining = .8 * last.SizeBytes!.Value - last.AllocatedBytes!.Value;
            if (remaining <= 0)
                return Result("threshold-reached", "Reported allocation is already at or above the 80% planning marker.", slope, 0, fit);
            if (slope <= 0)
                return Result("not-growing", "Measured allocation is flat or declining; no exhaustion date is estimated.", slope, null, fit);
            if (fit < .7)
                return Result("variable", "Growth is too variable for a useful linear estimate (fit below 0.70).", slope, null, fit);
            var eta = remaining / slope;
            if (eta > 3650)
                return Result("distant", "The extrapolated marker is more than ten years away; no useful date is shown.", slope, null, fit);
            return Result("estimated", "Approximate days to 80% allocated if measured linear growth continues. This planning marker is not a ZFS free-space guarantee; workload, snapshots, compression and deletions can change the result.", slope, eta, fit);
        }
    }

    public static CapacityEstimate RequireCurrentContext(CapacityEstimate result, NasConnection connection, Capability? capability, string poolGuid, DateTime now)
    {
        if (!connection.Enabled)
            return result with
            {
                Status = "paused",
                Explanation = "Collection is paused. Resume monitoring before using an estimate.",
                DaysTo80Percent = null
            };
        if (capability?.EffectiveStatus(now) != "supported" || capability.LastSuccessUtc is null || capability.LastSuccessUtc > now)
            return result with
            {
                Status = "unavailable",
                Explanation = "Current pool collection is unverified. Saved measurements remain historical; restore authenticated pool coverage before using an estimate.",
                DaysTo80Percent = null
            };
        if (!PoolEvidence.ContainsPool(connection.TopologyJson, poolGuid))
            return result with
            {
                Status = "unverified",
                Explanation = "This pool is absent from the latest saved topology. Historical measurements do not confirm current pool membership.",
                DaysTo80Percent = null
            };
        return result;
    }
}
