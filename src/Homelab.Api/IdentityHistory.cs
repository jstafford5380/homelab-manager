using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
namespace Homelab.Api;
public sealed class IdentityHistoryMerge
{
    public Guid Id {get;set;}=Guid.NewGuid();
    public Guid ConnectionId {get;set;}
    public Guid FirstDiskId {get;set;}
    public Guid SecondDiskId {get;set;}
    public Guid RequestId {get;set;}
    public string SubmissionHash {get;set;}="";
    public string Evidence {get;set;}="";
    public string Author {get;set;}="";
    public DateTime RecordedUtc {get;set;}
    public DateTime? ReversedUtc {get;set;}
    public string? ReversalEvidence {get;set;}
    public string? ReversedBy {get;set;}
}
public record IdentityMergeInput(Guid OtherDiskId,Guid RequestId,string ReviewToken,string FirstIdentity,string SecondIdentity,string Evidence,bool ConfirmedSamePhysicalDrive);
public record IdentitySplitInput(string ReviewToken,string Evidence);
public record IdentityHistoryGraph(Disk[] Disks,IdentityHistoryMerge[] Links)
{
    public Guid[] Group(Guid id) {
        var seen=new HashSet<Guid>{id};var queue=new Queue<Guid>();queue.Enqueue(id);
        while(queue.TryDequeue(out var next))foreach(var link in Links.Where(x=>x.ReversedUtc==null && (x.FirstDiskId==next || x.SecondDiskId==next))) {
            var other=link.FirstDiskId==next?link.SecondDiskId:link.FirstDiskId;if(seen.Add(other))queue.Enqueue(other);
        }
        return seen.Order().ToArray();
    }
    public string Token(IEnumerable<Guid> members) {
        var ids=members.ToHashSet();
        var data=new {records=Disks.Where(x=>ids.Contains(x.Id)).OrderBy(x=>x.Id).Select(x=>new{x.Id,x.ConnectionId,x.Serial,x.Model,x.Wwn,x.IdentityKey,x.MappingConfidence,x.Present,x.Lifecycle,x.Bay,x.BayVerified}),links=Links.Where(x=>x.ReversedUtc==null && (ids.Contains(x.FirstDiskId)||ids.Contains(x.SecondDiskId))).OrderBy(x=>x.Id).Select(x=>new{x.Id,x.FirstDiskId,x.SecondDiskId})};
        return Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(data)));
    }
}
public static class IdentityHistory
{
    public static string SubmissionHash(IdentityMergeInput input)=>Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(input)));
    public static string Confirmation(Disk disk)=>DrivePaths.Identity(disk)??disk.Id.ToString();
    public static async Task<IdentityHistoryGraph> Graph(HomelabDb db,Guid nas)=>new(await db.Disks.AsNoTracking().Where(x=>x.ConnectionId==nas).ToArrayAsync(),await db.IdentityMerges.AsNoTracking().Where(x=>x.ConnectionId==nas).ToArrayAsync());
    public static void MapIdentityHistory(this RouteGroupBuilder api)
    {
        api.MapGet("/disks/{id:guid}/identity-history",async(Guid id,long? before,HomelabDb db)=>{
            var disk=await db.Disks.FindAsync(id);if(disk is null)return Results.NotFound();if(before is <=0)return Results.BadRequest();
            var graph=await Graph(db,disk.ConnectionId);var members=graph.Group(id);
            var observations=await db.Observations.AsNoTracking().Where(x=>x.DiskId!=null && members.Contains(x.DiskId.Value) && (before==null || x.Id<before)).OrderByDescending(x=>x.Id).Take(51).ToArrayAsync();
            return Results.Ok(new{reviewToken=graph.Token(members),members=graph.Disks.Where(x=>members.Contains(x.Id)).Select(x=>new{x.Id,x.Serial,x.Model,x.Wwn,x.MappingConfidence,x.Present,x.Bay,x.BayVerified,confirmation=Confirmation(x)}),
                candidates=graph.Disks.Where(x=>!members.Contains(x.Id)).Select(x=>new{x.Id,x.Serial,x.Model,x.Wwn,confirmation=Confirmation(x)}),
                links=graph.Links.Where(x=>members.Contains(x.FirstDiskId)||members.Contains(x.SecondDiskId)).OrderByDescending(x=>x.RecordedUtc),observations=observations.Take(50),nextBefore=observations.Length>50?(long?)observations[49].Id:null,
                findings=await db.Findings.AsNoTracking().Where(x=>x.DiskId!=null && members.Contains(x.DiskId.Value)).OrderByDescending(x=>x.LastSeenUtc).Take(51).ToArrayAsync()});
        });
        api.MapGet("/disks/{id:guid}/identity-history/review",async(Guid id,Guid otherId,HomelabDb db)=>{
            var disk=await db.Disks.FindAsync(id);var other=await db.Disks.FindAsync(otherId);if(disk is null || other is null)return Results.NotFound();
            if(id==otherId || disk.ConnectionId!=other.ConnectionId)return Results.BadRequest(new{error="Choose two distinct records from the same NAS."});
            var graph=await Graph(db,disk.ConnectionId);var first=graph.Group(id);var second=graph.Group(otherId);if(first.Contains(otherId))return Results.Conflict(new{error="These records already share an identity history group."});
            var ids=first.Concat(second).ToArray();if(ids.Length>64)return Results.BadRequest(new{error="An identity history group supports at most 64 records."});
            return Results.Ok(new{reviewToken=graph.Token(ids),firstIdentity=Confirmation(disk),secondIdentity=Confirmation(other),records=graph.Disks.Where(x=>ids.Contains(x.Id)).Select(x=>new{x.Id,x.Serial,x.Model,x.Wwn,x.MappingConfidence,x.Present,x.Bay,x.BayVerified}),
                explanation="Review all records before merging their histories. Original IDs and evidence remain intact. This does not resolve discovery ambiguity, combine counter rates, verify bays or authorize physical actions."});
        });
        api.MapPost("/disks/{id:guid}/identity-history/merge",async(Guid id,IdentityMergeInput input,HomelabDb db,HttpContext ctx)=>{
            var previous=await db.IdentityMerges.SingleOrDefaultAsync(x=>x.RequestId==input.RequestId);
            if(previous is not null)return previous.FirstDiskId==id && previous.SecondDiskId==input.OtherDiskId && previous.SubmissionHash==SubmissionHash(input)?Results.Ok(previous):Results.Conflict(new{error="This request ID was already used for different evidence."});
            var disk=await db.Disks.FindAsync(id);var other=await db.Disks.FindAsync(input.OtherDiskId);if(disk is null || other is null)return Results.NotFound();
            if(id==other.Id || disk.ConnectionId!=other.ConnectionId || input.RequestId==Guid.Empty || !input.ConfirmedSamePhysicalDrive || string.IsNullOrWhiteSpace(input.Evidence) || input.Evidence.Length>4000 || input.FirstIdentity!=Confirmation(disk) || input.SecondIdentity!=Confirmation(other))return Results.BadRequest(new{error="Confirm both exact identities, the same physical drive assertion, and supporting evidence in 1–4000 characters."});
            var graph=await Graph(db,disk.ConnectionId);var first=graph.Group(id);var second=graph.Group(other.Id);var ids=first.Concat(second).ToArray();
            if(first.Contains(other.Id) || ids.Length>64 || graph.Token(ids)!=input.ReviewToken)return Results.Conflict(new{error="The records or identity groups changed. Reload the review before merging."});
            var row=new IdentityHistoryMerge{ConnectionId=disk.ConnectionId,FirstDiskId=id,SecondDiskId=other.Id,RequestId=input.RequestId,SubmissionHash=SubmissionHash(input),Evidence=input.Evidence,Author=ctx.User.Identity!.Name!,RecordedUtc=DateTime.UtcNow};
            db.IdentityMerges.Add(row);db.Activity.Add(new(){Actor=row.Author,Action="Identity histories manually merged",Detail=JsonSerializer.Serialize(row)});await db.SaveChangesAsync();return Results.Ok(row);
        });
        api.MapPost("/disks/{id:guid}/identity-history/{mergeId:guid}/reverse",async(Guid id,Guid mergeId,IdentitySplitInput input,HomelabDb db,HttpContext ctx)=>{
            var row=await db.IdentityMerges.FindAsync(mergeId);if(row is null)return Results.NotFound();
            if(string.IsNullOrWhiteSpace(input.Evidence)||input.Evidence.Length>4000)return Results.BadRequest(new{error="Record why the histories should be split, in 1–4000 characters."});
            var graph=await Graph(db,row.ConnectionId);var members=graph.Group(id);
            if(row.ReversedUtc!=null)return (members.Contains(row.FirstDiskId) || members.Contains(row.SecondDiskId)) && row.ReversalEvidence==input.Evidence?Results.Ok(row):Results.Conflict(new{error="This merge was already reversed."});
            if(!members.Contains(row.FirstDiskId)||!members.Contains(row.SecondDiskId))return Results.NotFound();
            if(graph.Token(members)!=input.ReviewToken)return Results.Conflict(new{error="The identity group changed. Reload before splitting it."});
            row.ReversedUtc=DateTime.UtcNow;row.ReversalEvidence=input.Evidence;row.ReversedBy=ctx.User.Identity!.Name!;
            db.Activity.Add(new(){Actor=row.ReversedBy,Action="Identity history merge reversed",Detail=JsonSerializer.Serialize(row)});await db.SaveChangesAsync();return Results.Ok(row);
        });
    }
}
