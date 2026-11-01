using System.Text.Json;
using Microsoft.EntityFrameworkCore;
namespace Homelab.Api;
public sealed class DrivePathReview
{
    public long Id {get;set;}
    public Guid DiskId {get;set;}
    public Guid AssetId {get;set;}
    public Guid RequestId {get;set;}
    public DateTime AssetUpdatedUtc {get;set;}
    public string AssetName {get;set;}="";
    public string AssetType {get;set;}="";
    public string ConfirmedIdentity {get;set;}="";
    public string DiskModel {get;set;}="";
    public bool Attached {get;set;}
    public string Evidence {get;set;}="";
    public DateTime ValidUntilUtc {get;set;}
    public DateTime RecordedUtc {get;set;}
    public string Author {get;set;}="";
}
public record DrivePathInput(Guid RequestId,Guid AssetId,DateTime AssetUpdatedUtc,string ConfirmedIdentity,bool Attached,string Evidence,DateTime ValidUntilUtc,long? PreviousReviewId = null);
public static class DrivePaths
{
    public static string? Identity(Disk disk)=>!string.IsNullOrWhiteSpace(disk.Serial)?disk.Serial:IdentityCandidate.NormalizeWwn(disk.Wwn);
    public static string? Validate(Disk disk,InventoryAsset asset,DrivePathInput input,DateTime now)
    {
        if(input.RequestId==Guid.Empty || input.Evidence is null || string.IsNullOrWhiteSpace(input.Evidence) || input.Evidence.Length>4000)return "Record relationship evidence in 1–4000 characters.";
        if(Identity(disk) is not {} identity || identity!=input.ConfirmedIdentity || disk.MappingConfidence=="ambiguous")return "Confirm this drive's exact, unambiguous serial or WWN.";
        if(asset.Type is not ("controller" or "enclosure" or "power"))return "Choose a controller, enclosure or power asset.";
        if(input.Attached && (asset.Archived || !disk.Present || disk.Lifecycle=="retired"))return "An attached relationship requires an active asset and a currently present, non-retired drive.";
        if(input.ValidUntilUtc.Kind!=DateTimeKind.Utc || input.ValidUntilUtc<=now || input.ValidUntilUtc>now.AddYears(1))return "Set a UTC review expiry within the next year.";
        return null;
    }
    public static string Status(DrivePathReview row,Disk disk,InventoryAsset asset,bool latest,DateTime now)=>!latest?"superseded":!row.Attached?"ended":asset.Archived?"asset unavailable":asset.UpdatedUtc!=row.AssetUpdatedUtc?"asset changed":Identity(disk)!=row.ConfirmedIdentity || disk.Model!=row.DiskModel || disk.MappingConfidence=="ambiguous"?"identity needs review":!disk.Present || disk.Lifecycle=="retired"?"drive not current":row.ValidUntilUtc<=now?"review expired":"recorded relationship";
    public static bool Same(DrivePathReview row,DrivePathInput input)=>row.AssetId==input.AssetId && row.AssetUpdatedUtc==input.AssetUpdatedUtc && row.ConfirmedIdentity==input.ConfirmedIdentity && row.Attached==input.Attached && row.Evidence==input.Evidence && row.ValidUntilUtc==input.ValidUntilUtc;
    public static void MapDrivePaths(this RouteGroupBuilder api)
    {
        api.MapPost("/disks/{id:guid}/hardware-paths",async(Guid id,DrivePathInput input,HomelabDb db,HttpContext ctx)=>{
            var disk=await db.Disks.FindAsync(id);if(disk is null)return Results.NotFound();
            var existing=await db.DrivePaths.SingleOrDefaultAsync(x=>x.DiskId==id && x.RequestId==input.RequestId);
            if(existing is not null)return Same(existing,input)?Results.Ok(existing):Results.Conflict(new {error="This submission ID was already used with different evidence."});
            var asset=await db.Assets.FindAsync(input.AssetId);if(asset is null)return Results.NotFound();
            var now=DateTime.UtcNow;if(Validate(disk,asset,input,now) is {} error)return Results.BadRequest(new{error});
            if(asset.UpdatedUtc!=input.AssetUpdatedUtc)return Results.Conflict(new{error="This asset changed. Reload inventory before recording the relationship."});
            var head=await db.DrivePaths.Where(x=>x.DiskId==id && x.AssetId==input.AssetId).MaxAsync(x=>(long?)x.Id);
            if(head!=input.PreviousReviewId)return Results.Conflict(new{error="This relationship changed. Reload the latest evidence and review it before saving."});
            var row=new DrivePathReview{DiskId=id,AssetId=asset.Id,RequestId=input.RequestId,AssetUpdatedUtc=asset.UpdatedUtc,AssetName=asset.Name,AssetType=asset.Type,ConfirmedIdentity=input.ConfirmedIdentity,DiskModel=disk.Model,Attached=input.Attached,Evidence=input.Evidence,ValidUntilUtc=input.ValidUntilUtc,RecordedUtc=now,Author=ctx.User.Identity!.Name!};
            db.DrivePaths.Add(row);db.Activity.Add(new(){Actor=row.Author,Action="Drive hardware relationship recorded",Detail=JsonSerializer.Serialize(row)});await db.SaveChangesAsync();return Results.Ok(row);
        });
        api.MapGet("/disks/{id:guid}/hardware-paths",async(Guid id,long? before,HomelabDb db)=>{
            var disk=await db.Disks.AsNoTracking().SingleOrDefaultAsync(x=>x.Id==id);if(disk is null)return Results.NotFound();if(before is <=0)return Results.BadRequest();
            var now=DateTime.UtcNow;
            var latestIds=db.DrivePaths.GroupBy(x=>new{x.DiskId,x.AssetId}).Select(g=>g.Max(x=>x.Id));
            var ownAssetIds=db.DrivePaths.Where(x=>x.DiskId==id).Select(x=>x.AssetId).Distinct();
            var latest=await db.DrivePaths.AsNoTracking().Where(x=>latestIds.Contains(x.Id) && ownAssetIds.Contains(x.AssetId)).ToListAsync();
            var assets=await db.Assets.AsNoTracking().Where(x=>ownAssetIds.Contains(x.Id)).ToDictionaryAsync(x=>x.Id);
            var relatedDiskIds=latest.Select(x=>x.DiskId).Append(id).Distinct().ToArray();
            var disks=await db.Disks.AsNoTracking().Where(x=>relatedDiskIds.Contains(x.Id)).ToDictionaryAsync(x=>x.Id);
            var current=latest.Where(x=>disks.TryGetValue(x.DiskId,out var d) && assets.TryGetValue(x.AssetId,out var a) && Status(x,d,a,true,now)=="recorded relationship").ToArray();
            var sharedIds=current.Where(x=>x.DiskId==id).Select(x=>x.AssetId).ToHashSet();
            var peers=current.Where(x=>x.DiskId!=id && sharedIds.Contains(x.AssetId)).ToArray();
            var peerIds=peers.Select(x=>x.DiskId).Distinct().Order().Take(33).ToArray();
            var shown=peerIds.Take(32).ToArray();
            var findings=await db.Findings.AsNoTracking().Where(x=>x.DiskId!=null && shown.Contains(x.DiskId.Value) && x.State!="resolved").OrderByDescending(x=>x.LastSeenUtc).Take(161).ToListAsync();
            var rows=await db.DrivePaths.AsNoTracking().Where(x=>x.DiskId==id && (before==null || x.Id<before)).OrderByDescending(x=>x.Id).Take(26).ToListAsync();
            return Results.Ok(new{generatedUtc=now,currentHeads=latest.Where(x=>x.DiskId==id).Select(x=>new{x.AssetId,x.Id,x.Attached,x.Evidence,x.RecordedUtc}),items=rows.Take(25).Select(x=>new{record=x,status=Status(x,disk,assets[x.AssetId],latest.Any(l=>l.Id==x.Id),now)}),nextBefore=rows.Count>25?(long?)rows[24].Id:null,
                peersTruncated=peerIds.Length>32 || findings.Count>160,peers=shown.Select(peer=>new{diskId=peer,disks[peer].Serial,disks[peer].Model,components=peers.Where(x=>x.DiskId==peer).Select(x=>new{x.AssetId,x.AssetName,x.AssetType,x.ValidUntilUtc}),findings=findings.Take(160).Where(x=>x.DiskId==peer).Select(x=>new{x.Id,x.Title,x.Evidence,x.EvidenceObservedUtc,x.LastSeenUtc,x.State})})});
        });
    }
}
