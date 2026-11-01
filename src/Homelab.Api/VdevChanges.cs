using System.Text.Json;
namespace Homelab.Api;

public record PoolSourceContext(string Schema,string? Serial,string? Version,Guid ConfigurationRevision,DateTime ReadStartedUtc,DateTime ReadFinishedUtc,double? UptimeSeconds);
public record VdevCounterChange(string Status,long? Difference);
public record VdevMemberChange(string Guid,VdevCounterChange Read,VdevCounterChange Write,VdevCounterChange Checksum);
public record VdevComparison(string Status,string Explanation,DateTime? PreviousFetchedUtc,VdevMemberChange[] Members);
public static class VdevChanges
{
    private static readonly JsonSerializerOptions Options=new(JsonSerializerDefaults.Web);
    public static string CaptureContext(JsonElement system,Guid revision,DateTime started,DateTime finished)
    {
        double? uptime=system.TryGetProperty("uptime_seconds",out var value) && value.ValueKind==JsonValueKind.Number && value.TryGetDouble(out var seconds) && double.IsFinite(seconds) && seconds>=0 && seconds<=9007199254740991d ? seconds:null;
        var version=system.TryGetProperty("version",out var v) && v.ValueKind==JsonValueKind.String ? v.GetString():null;
        return JsonSerializer.Serialize(new PoolSourceContext("truenas-24.10-system-context-v1",NasIdentity.ReadSerial(system),version,revision,started,finished,uptime),Options);
    }
    private static PoolSourceContext? Context(string? json) {
        try { if(json is null || json.Length>8192)return null;var row=JsonSerializer.Deserialize<PoolSourceContext>(json,Options);
            return row is not null && row.Schema=="truenas-24.10-system-context-v1" && row.ConfigurationRevision!=Guid.Empty && !string.IsNullOrWhiteSpace(row.Serial) && !string.IsNullOrWhiteSpace(row.Version) && row.ReadStartedUtc.Kind==DateTimeKind.Utc && row.ReadFinishedUtc.Kind==DateTimeKind.Utc && row.ReadFinishedUtc>=row.ReadStartedUtc && row.ReadFinishedUtc-row.ReadStartedUtc<=TimeSpan.FromSeconds(30) && row.UptimeSeconds is {} u && double.IsFinite(u) && u>=0 && u<=9007199254740991d ? row:null;
        } catch(JsonException) { return null; }
    }
    public static VdevComparison Compare(PoolSnapshot current,PoolSnapshot? previous)
    {
        VdevComparison Stop(string status,string explanation)=>new(status,explanation,previous?.FetchedUtc,[]);
        if(previous is null)return Stop("baseline","No preceding detailed snapshot is retained. This reading starts a baseline.");
        if(current.ConnectionId!=previous.ConnectionId || current.PoolGuid!=previous.PoolGuid || current.SourceSchema!=previous.SourceSchema)return Stop("context_changed","Pool or source context changed; counter differences are withheld.");
        var now=Context(current.SourceContextJson);var before=Context(previous.SourceContextJson);
        if(now is null || before is null)return Stop("unknown","System identity, uptime or read timing is unavailable; continuity cannot be assessed.");
        if(now.Serial!=before.Serial || now.Version!=before.Version || now.ConfigurationRevision!=before.ConfigurationRevision)return Stop("context_changed","System identity, software or connection configuration changed; a new baseline is required.");
        if(current.FetchedUtc<=previous.FetchedUtc || now.ReadStartedUtc<=before.ReadFinishedUtc || current.FetchedUtc<now.ReadFinishedUtc || previous.FetchedUtc<before.ReadFinishedUtc || current.FetchedUtc-now.ReadFinishedUtc>TimeSpan.FromSeconds(30) || previous.FetchedUtc-before.ReadFinishedUtc>TimeSpan.FromSeconds(30) || now.ReadFinishedUtc-before.ReadStartedUtc>TimeSpan.FromHours(1))return Stop("timing_gap","Read timing overlaps, is inconsistent, or exceeds the one-hour comparison window. Differences are withheld.");
        var elapsedMin=(now.ReadStartedUtc-before.ReadFinishedUtc).TotalSeconds;
        var elapsedMax=(now.ReadFinishedUtc-before.ReadStartedUtc).TotalSeconds;
        var uptimeAdvance=now.UptimeSeconds!.Value-before.UptimeSeconds!.Value;
        if(uptimeAdvance<elapsedMin-2 || uptimeAdvance>elapsedMax+2)return Stop("uptime_boundary","Reported uptime does not advance with the read interval. A restart or clock/timing discontinuity is possible; differences are withheld.");
        VdevSnapshotEvidence a,b;
        try { if(current.VdevEvidenceJson is null || previous.VdevEvidenceJson is null)return Stop("unknown","Member evidence was not retained for both readings.");a=VdevEvidence.Read(current.VdevEvidenceJson);b=VdevEvidence.Read(previous.VdevEvidenceJson); }
        catch(Exception e) when(e is JsonException or InvalidDataException) {return Stop("unknown","Stored member evidence could not be read safely.");}
        if(a.Status!="reported" || b.Status!="reported")return Stop("unknown","Both readings need reported member evidence.");
        // Includes aliases and state: topology transitions are boundaries, never silently joined.
        if(VdevEvidence.Context(current.VdevEvidenceJson,false)!=VdevEvidence.Context(previous.VdevEvidenceJson,false))return Stop("context_changed","Reported member identity, role, parent, state or aliases changed. Differences are withheld.");
        if(a.Nodes.Zip(b.Nodes).Any(pair => pair.First.DriveMapping is {Status:"inferred",DiskId:{} nowId} && pair.Second.DriveMapping is {Status:"inferred",DiskId:{} beforeId} && nowId!=beforeId))return Stop("context_changed","The inferred physical drive identity changed; differences are withheld.");
        VdevCounterChange Change(long? latest,long? older)=>latest is null || older is null ? new("unknown",null) : latest<older ? new("decreased",null) : new(latest>older?"increased":"unchanged",latest-older);
        var members=a.Nodes.Zip(b.Nodes,(n,o)=>new VdevMemberChange(n.Guid,Change(n.ReadErrors,o.ReadErrors),Change(n.WriteErrors,o.WriteErrors),Change(n.ChecksumErrors,o.ChecksumErrors))).ToArray();
        // Any decrease may mark a shared clear/import/reset boundary; suppress all differences.
        if(members.Any(m=>new[]{m.Read,m.Write,m.Checksum}.Any(c=>c.Status=="decreased")))return Stop("counter_decreased","At least one member counter decreased. A clear, reset or replacement is possible; differences are withheld and this does not establish repair.");
        return new("compared","Observed differences between adjacent saved readings with matching context and consistent uptime. Clears between polls can go undetected; differences are not guaranteed counts of new errors, drive failures or proof of recovery.",previous.FetchedUtc,members);
    }
}
