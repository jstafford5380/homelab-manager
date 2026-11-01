using Microsoft.EntityFrameworkCore;
namespace Homelab.Api;

public static class VdevFindings
{
    public const string RulePrefix="vdev-counter-increase-v1:";
    // Caller owns the collection transaction and writer lease. Never issues a NAS command.
    public static async Task RecordAsync(HomelabDb db,PoolSnapshot current,CancellationToken ct=default)
    {
        var previous=await db.PoolSnapshots.AsNoTracking().Where(x=>x.ConnectionId==current.ConnectionId && x.PoolGuid==current.PoolGuid)
            .OrderByDescending(x=>x.Id).FirstOrDefaultAsync(ct);
        var comparison=VdevChanges.Compare(current,previous);
        if(comparison.Status!="compared")return;
        var increased=comparison.Members.Where(x=>x.Read.Difference>0 || x.Write.Difference>0 || x.Checksum.Difference>0).ToArray();
        if(increased.Length==0)return;
        var rule=RulePrefix+current.PoolGuid;
        var local=db.Findings.Local.Where(x=>x.ConnectionId==current.ConnectionId && x.Rule==rule).ToArray();
        // Replayed or out-of-order evidence cannot reopen a reviewed episode or roll it back.
        if(local.Any(x=>x.EvidenceObservedUtc>=current.FetchedUtc) || await db.Findings.AnyAsync(x=>x.ConnectionId==current.ConnectionId && x.Rule==rule && x.EvidenceObservedUtc>=current.FetchedUtc,ct))return;
        var finding=local.FirstOrDefault(x=>x.State!="resolved") ?? await db.Findings.FirstOrDefaultAsync(x=>x.ConnectionId==current.ConnectionId && x.Rule==rule && x.State!="resolved",ct);
        var created=finding is null;
        if(finding is null) {
            finding=new Finding {ConnectionId=current.ConnectionId,Rule=rule,FirstSeenUtc=current.FetchedUtc};
            db.Findings.Add(finding);
        }
        var now=VdevEvidence.Read(current.VdevEvidenceJson!).Nodes.ToDictionary(x=>x.Guid);
        var before=VdevEvidence.Read(previous!.VdevEvidenceJson!).Nodes.ToDictionary(x=>x.Guid);
        var lines=increased.Take(20).Select(change=> {
            var n=now[change.Guid];var p=before[change.Guid];var parts=new List<string>();
            void Add(string name,long? a,long? b,long? difference) { if(difference>0)parts.Add($"{name} {a} → {b} (+{difference})"); }
            Add("read",p.ReadErrors,n.ReadErrors,change.Read.Difference);Add("write",p.WriteErrors,n.WriteErrors,change.Write.Difference);Add("checksum",p.ChecksumErrors,n.ChecksumErrors,change.Checksum.Difference);
            return $"Member {change.Guid} ({n.Role}/{n.Type}): {string.Join(", ",parts)}.";
        });
        finding.Severity="review";
        finding.Title=$"Pool {current.Name} member error counters increased";
        finding.Evidence=$"Pool GUID {current.PoolGuid}; adjacent reads {previous.FetchedUtc:O} → {current.FetchedUtc:O}. {increased.Length} ZFS member(s) show an observed increase. " + string.Join(" ",lines) +
            (increased.Length>20? $" Showing 20 of {increased.Length} increased members; consult retained detailed history for the others.":"") +
            " Parent and child counters can describe the same underlying errors; do not add member counts together. These are observed net differences, not guaranteed new errors or physical-drive failures. Clears between polls can be missed.";
        finding.NextStep="Open Storage and review this pool's member error history and native TrueNAS details. Check independent recovery readiness and correlate drive, cable, controller and enclosure evidence before planning replacement. ZFS GUIDs are not verified drive or bay identities. Do not remove a drive or clear counters based on this finding. Later stable, zero, missing or reset counts do not establish repair; record resolution after investigation.";
        finding.LastSeenUtc=current.FetchedUtc;finding.EvidenceObservedUtc=current.FetchedUtc;
        finding.EvidenceSource="TrueNAS pool.query + system.info";finding.EvidenceSchema=current.SourceSchema;
        finding.EvidenceUnit="observed counter differences";
        finding.EvidenceEpoch="Matching reported context and consistent uptime; unobserved clears remain possible";
        db.Activity.Add(new() {Action=created?"Pool member counter increase flagged":"Pool member counter evidence updated",Detail=$"Finding {finding.Id}; no physical disk attribution. " + finding.Evidence});
    }
}
