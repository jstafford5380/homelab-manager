using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
namespace Homelab.Api;
public sealed class DiscoveryAssignment
{
    public Guid Id {get;set;}=Guid.NewGuid();
    public Guid ConnectionId {get;set;}
    public Guid DiskId {get;set;}
    public Guid ConfigurationRevision {get;set;}
    public Guid RequestId {get;set;}
    public string SubmissionHash {get;set;}="";
    public string Wwn {get;set;}="";
    public string Serial {get;set;}="";
    public string Model {get;set;}="";
    public string Evidence {get;set;}="";
    public string Author {get;set;}="";
    public DateTime RecordedUtc {get;set;}
    public DateTime ValidUntilUtc {get;set;}
    public DateTime? RevokedUtc {get;set;}
    public string? RevocationEvidence {get;set;}
    public string? RevokedBy {get;set;}
}
public record DiscoverySnapshot(Guid Revision,DateTime FetchedUtc,IdentityCandidate[] Rows);
public record DiscoveryAssignmentInput(Guid RequestId,string ReviewToken,string Wwn,string ConfirmedWwn,Guid? DiskId,string Evidence,DateTime ValidUntilUtc,bool ConfirmedPhysicalIdentity);
public record DiscoveryRevokeInput(string Evidence);
public static class DiscoveryAssignments
{
    private static readonly JsonSerializerOptions Options=new(JsonSerializerDefaults.Web);
    public static string? Capture(Guid revision,DateTime at,IdentityCandidate[] rows) {var json=JsonSerializer.Serialize(new DiscoverySnapshot(revision,at,rows),Options);return rows.Length<=4096 && json.Length<=256*1024?json:null;}
    public static DiscoverySnapshot? Read(NasConnection nas) {try{if(nas.DiscoveryIdentityJson is not {} json || json.Length>256*1024)return null;var value=JsonSerializer.Deserialize<DiscoverySnapshot>(json,Options);return value is {Rows:not null} && value.Rows.Length<=4096 && value.Rows.All(x=>x is not null && x.Serial is not null && x.Model is not null && x.Alias is not null)?value:null;}catch(JsonException){return null;}}
    public static bool Usable(NasConnection nas,DiscoverySnapshot? snapshot,DateTime now)=>nas.Enabled && snapshot is not null && snapshot.Revision==nas.ConfigurationRevision && snapshot.FetchedUtc<=now && snapshot.FetchedUtc>=now.AddDays(-1);
    public static bool Compatible(IEnumerable<IdentityCandidate> rows)=>rows.Any() && rows.All(x=>IdentityCandidate.NormalizeWwn(x.Wwn)!=null && !string.IsNullOrWhiteSpace(x.Model)) && rows.Select(x=>(IdentityCandidate.NormalizeWwn(x.Wwn),x.Serial.Trim(),x.Model.Trim())).Distinct().Count()==1;
    public static bool AliasConsistent(IEnumerable<IdentityCandidate> group,IEnumerable<IdentityCandidate> all)=>!group.Any(x=>all.Any(other=>other.Alias==x.Alias && other.Key!=x.Key));
    public static Disk? Target(DiscoveryAssignment decision,IEnumerable<IdentityCandidate> rows,IReadOnlyList<Disk> disks,Guid revision,DateTime at)
    {
        if(decision.RevokedUtc!=null || decision.ValidUntilUtc<=at || decision.ConfigurationRevision!=revision || !Compatible(rows))return null;
        var row=rows.First();if(IdentityCandidate.NormalizeWwn(row.Wwn)!=decision.Wwn || row.Serial.Trim()!=decision.Serial || row.Model.Trim()!=decision.Model)return null;
        return disks.SingleOrDefault(x=>x.Id==decision.DiskId && IdentityCandidate.NormalizeWwn(x.Wwn)==decision.Wwn && x.Serial.Trim()==decision.Serial && x.Model.Trim()==decision.Model && x.Lifecycle!="retired");
    }
    private static string Hash<T>(T value)=>Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(value)));
    public static string Token(NasConnection nas,DiscoverySnapshot snapshot,IEnumerable<Disk> disks,IEnumerable<DiscoveryAssignment> assignments)=>Hash(new{nas.ConfigurationRevision,rows=snapshot.Rows.OrderBy(x=>x.Key).ThenBy(x=>x.Alias).ThenBy(x=>x.Serial).ThenBy(x=>x.Model),records=disks.OrderBy(x=>x.Id).Select(x=>new{x.Id,x.Serial,x.Model,x.Wwn,x.MappingConfidence,x.Lifecycle}),assignments=assignments.Where(x=>x.RevokedUtc==null).OrderBy(x=>x.Id).Select(x=>new{x.Id,x.ValidUntilUtc})});
    public static void MapDiscoveryAssignments(this RouteGroupBuilder api)
    {
        api.MapGet("/connections/{id:guid}/identity-review",async(Guid id,HomelabDb db)=>{
            var nas=await db.Connections.FindAsync(id);if(nas is null)return Results.NotFound();var snapshot=Read(nas);
            var disks=await db.Disks.AsNoTracking().Where(x=>x.ConnectionId==id).ToArrayAsync();var assignments=await db.DiscoveryAssignments.AsNoTracking().Where(x=>x.ConnectionId==id).OrderByDescending(x=>x.RecordedUtc).ToArrayAsync();
            var now=DateTime.UtcNow;
            return Results.Ok(new{usable=Usable(nas,snapshot,now),fetchedUtc=snapshot?.FetchedUtc,reviewToken=snapshot is null?null:Token(nas,snapshot,disks,assignments),
                groups=(snapshot?.Rows??[]).GroupBy(x=>x.Key).Select(g=>new{wwn=IdentityCandidate.NormalizeWwn(g.First().Wwn),serial=g.First().Serial,model=g.First().Model,aliases=g.Select(x=>x.Alias).Distinct(),eligible=Compatible(g) && AliasConsistent(g,snapshot!.Rows),records=disks.Where(d=>IdentityCandidate.NormalizeWwn(d.Wwn)==IdentityCandidate.NormalizeWwn(g.First().Wwn) && IdentityCandidate.NormalizeWwn(d.Wwn)!=null && d.Serial.Trim()==g.First().Serial.Trim() && d.Model.Trim()==g.First().Model.Trim()).Select(d=>new{d.Id,d.Serial,d.Model,d.MappingConfidence,d.Lifecycle})}),
                assignments=assignments.Where(x=>x.RevokedUtc==null && x.ValidUntilUtc>now).Concat(assignments.Where(x=>x.RevokedUtc!=null || x.ValidUntilUtc<=now).Take(50)),historyTruncated=assignments.Count(x=>x.RevokedUtc!=null || x.ValidUntilUtc<=now)>50});
        });
        api.MapPost("/connections/{id:guid}/identity-review",async(Guid id,DiscoveryAssignmentInput input,HomelabDb db,HttpContext ctx)=>{
            var old=await db.DiscoveryAssignments.SingleOrDefaultAsync(x=>x.RequestId==input.RequestId);if(old is not null)return old.ConnectionId==id && old.SubmissionHash==Hash(input)?Results.Ok(old):Results.Conflict(new{error="This request ID was used for different evidence."});
            var nas=await db.Connections.FindAsync(id);if(nas is null)return Results.NotFound();var now=DateTime.UtcNow;var snapshot=Read(nas);
            if(!Usable(nas,snapshot,now))return Results.Conflict(new{error="A running connection and current-configuration inventory from the last 24 hours are required. Refresh inventory before review."});
            var disks=await db.Disks.Where(x=>x.ConnectionId==id).ToArrayAsync();var assignments=await db.DiscoveryAssignments.Where(x=>x.ConnectionId==id).ToArrayAsync();
            if(Token(nas,snapshot!,disks,assignments)!=input.ReviewToken)return Results.Conflict(new{error="Inventory, identity records or assignments changed. Reload the review."});
            var rows=snapshot!.Rows.Where(x=>IdentityCandidate.NormalizeWwn(x.Wwn)==input.Wwn).ToArray();
            if(!Compatible(rows) || !AliasConsistent(rows,snapshot.Rows) || input.Wwn!=input.ConfirmedWwn || !input.ConfirmedPhysicalIdentity || input.RequestId==Guid.Empty || string.IsNullOrWhiteSpace(input.Evidence) || input.Evidence.Length>4000 || input.ValidUntilUtc.Kind!=DateTimeKind.Utc || input.ValidUntilUtc<=now || input.ValidUntilUtc>now.AddYears(1))return Results.BadRequest(new{error="Confirm the exact stable WWN and physical identity, provide evidence, and set a review expiry within one year. Missing or conflicting source identities cannot be overridden."});
            var row=rows[0];var target=input.DiskId is {} targetId?disks.SingleOrDefault(x=>x.Id==targetId):null;
            if(input.DiskId!=null && (target is null || IdentityCandidate.NormalizeWwn(target.Wwn)!=input.Wwn || target.Serial.Trim()!=row.Serial.Trim() || target.Model.Trim()!=row.Model.Trim() || target.Lifecycle=="retired"))return Results.BadRequest(new{error="The selected record must match the reported WWN, serial and model and must not be retired."});
            if(target is null && disks.Any(x=>IdentityCandidate.NormalizeWwn(x.Wwn)==input.Wwn && x.Serial.Trim()==row.Serial.Trim() && x.Model.Trim()==row.Model.Trim() && x.Lifecycle!="retired"))return Results.BadRequest(new{error="A matching active identity record exists. Review and select it instead of creating another duplicate."});
            if(target is null){target=new Disk{ConnectionId=id,IdentityKey="wwn:"+input.Wwn,Wwn=input.Wwn,Serial=row.Serial.Trim(),Model=row.Model.Trim(),Present=false,MappingConfidence="review pending",FirstSeenUtc=snapshot.FetchedUtc,LastSeenUtc=snapshot.FetchedUtc};db.Disks.Add(target);}
            foreach(var prior in assignments.Where(x=>x.Wwn==input.Wwn && x.RevokedUtc==null)){prior.RevokedUtc=now;prior.RevokedBy=ctx.User.Identity!.Name!;prior.RevocationEvidence="Superseded by a newly reviewed assignment.";}
            var decision=new DiscoveryAssignment{ConnectionId=id,DiskId=target.Id,ConfigurationRevision=nas.ConfigurationRevision,RequestId=input.RequestId,SubmissionHash=Hash(input),Wwn=input.Wwn,Serial=row.Serial.Trim(),Model=row.Model.Trim(),Evidence=input.Evidence,Author=ctx.User.Identity!.Name!,RecordedUtc=now,ValidUntilUtc=input.ValidUntilUtc};
            db.DiscoveryAssignments.Add(decision);db.Activity.Add(new(){Actor=decision.Author,Action="Stable WWN discovery assignment reviewed",Detail=JsonSerializer.Serialize(decision)});await db.SaveChangesAsync();return Results.Ok(decision);
        });
        api.MapPost("/connections/{id:guid}/identity-review/{assignmentId:guid}/revoke",async(Guid id,Guid assignmentId,DiscoveryRevokeInput input,HomelabDb db,HttpContext ctx)=>{
            var row=await db.DiscoveryAssignments.SingleOrDefaultAsync(x=>x.Id==assignmentId && x.ConnectionId==id);if(row is null)return Results.NotFound();if(string.IsNullOrWhiteSpace(input.Evidence)||input.Evidence.Length>4000)return Results.BadRequest(new{error="Record a reason in 1–4000 characters."});
            if(row.RevokedUtc!=null)return row.RevocationEvidence==input.Evidence?Results.Ok(row):Results.Conflict(new{error="This assignment was already revoked."});
            row.RevokedUtc=DateTime.UtcNow;row.RevokedBy=ctx.User.Identity!.Name!;row.RevocationEvidence=input.Evidence;
            var disk=await db.Disks.FindAsync(row.DiskId);if(disk is not null)disk.MappingConfidence="ambiguous";
            db.Activity.Add(new(){Actor=row.RevokedBy,Action="Discovery assignment revoked",Detail=JsonSerializer.Serialize(row)});await db.SaveChangesAsync();return Results.Ok(row);
        });
    }
}
