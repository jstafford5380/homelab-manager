using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
namespace Homelab.Api;
public sealed class DatasetRenameReview {
 public long Id {get;set;}
 public Guid ConnectionId {get;set;}
 public long FromSampleId {get;set;}
 public long ToSampleId {get;set;}
 public string FromName {get;set;}="";
 public string ToName {get;set;}="";
 public Guid RequestId {get;set;}
 public string SubmissionHash {get;set;}="";
 public string Evidence {get;set;}="";
 public string Author {get;set;}="";
 public DateTime RecordedUtc {get;set;}
 public DateTime? ReversedUtc {get;set;}
 public string? ReversalEvidence {get;set;}
 public string? ReversedBy {get;set;}
}
public record DatasetRenameInput(long FromSampleId,long ToSampleId,string FromName,string ToName,string ConfirmedFromName,string ConfirmedToName,string ReviewToken,string Evidence,bool ConfirmedSameDataset,Guid RequestId);
public record DatasetRenameReversal(string Evidence);
public static class DatasetRenames {
 public static string Token(DatasetInventorySample from,DatasetInventorySample to)=>Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new{from,to})));
 public static string? Validate(DatasetInventorySample from,DatasetInventorySample to,DatasetRenameInput input){
  if(from.ConnectionId!=to.ConnectionId || from.Id==to.Id || from.FetchedUtc>=to.FetchedUtc)return "Choose an earlier and a later inventory from the same NAS.";
  if(string.IsNullOrWhiteSpace(from.SystemSerial)||from.SystemSerial!=to.SystemSerial)return "Both inventories need the same known NAS system identity; do not infer a rename across unknown or changed sources.";
  if(input.RequestId==Guid.Empty || !input.ConfirmedSameDataset || string.IsNullOrWhiteSpace(input.Evidence)||input.Evidence.Length>4000 || string.IsNullOrWhiteSpace(input.FromName)||input.FromName.Length>256 || string.IsNullOrWhiteSpace(input.ToName)||input.ToName.Length>256 || input.FromName==input.ToName || input.ConfirmedFromName!=input.FromName || input.ConfirmedToName!=input.ToName)return "Confirm both exact names, the same-dataset assertion and supporting evidence in 1–4000 characters.";
  DatasetSnapshot? oldSnapshot,newSnapshot;
  try{oldSnapshot=JsonSerializer.Deserialize<DatasetSnapshot>(from.SnapshotJson);newSnapshot=JsonSerializer.Deserialize<DatasetSnapshot>(to.SnapshotJson);}catch(JsonException){return "The saved inventory cannot be interpreted.";}
  if(oldSnapshot?.Items is null||newSnapshot?.Items is null||oldSnapshot.Items.Length>256||newSnapshot.Items.Length>256)return "Both saved inventories are required.";
  var oldRows=oldSnapshot.Items.Where(x=>x.Name==input.FromName).ToArray();var newRows=newSnapshot.Items.Where(x=>x.Name==input.ToName).ToArray();
  if(oldRows.Length!=1||newRows.Length!=1 || oldRows[0].Type!=newRows[0].Type || oldRows[0].Pool!=newRows[0].Pool || oldSnapshot.Items.Any(x=>x.Name==input.ToName)||newSnapshot.Items.Any(x=>x.Name==input.FromName))return "Choose distinct non-coexisting names of the same type and pool. Conflicting inventory cannot establish this rename.";
  return null;
 }
 static object Preview(DatasetInventorySample row)=>new{row.Id,row.FetchedUtc,row.SystemSerial,row.Version,row.ConfigurationRevision,snapshot=JsonSerializer.Deserialize<DatasetSnapshot>(row.SnapshotJson)};
 public static void MapDatasetRenames(this RouteGroupBuilder api){
  api.MapGet("/connections/{id:guid}/dataset-renames",async(Guid id,long? before,HomelabDb db)=>{
   if(before<=0)return Results.BadRequest();if(!await db.Connections.AnyAsync(x=>x.Id==id))return Results.NotFound();
   var rows=await db.DatasetRenames.AsNoTracking().Where(x=>x.ConnectionId==id&&(before==null||x.Id<before)).OrderByDescending(x=>x.Id).Take(26).ToArrayAsync();return Results.Ok(new{items=rows.Take(25),nextBefore=rows.Length>25?(long?)rows[24].Id:null});
  });
  api.MapGet("/connections/{id:guid}/dataset-renames/review",async(Guid id,long fromSampleId,long toSampleId,HomelabDb db)=>{
   var from=await db.DatasetSamples.AsNoTracking().SingleOrDefaultAsync(x=>x.Id==fromSampleId&&x.ConnectionId==id);var to=await db.DatasetSamples.AsNoTracking().SingleOrDefaultAsync(x=>x.Id==toSampleId&&x.ConnectionId==id);if(from is null||to is null)return Results.NotFound();
   if(from.Id==to.Id||from.FetchedUtc>=to.FetchedUtc)return Results.BadRequest(new{error="Choose an earlier and a later saved inventory."});
   return Results.Ok(new{reviewToken=Token(from,to),from=Preview(from),to=Preview(to),warning="Manual rename evidence only. This does not establish a GUID or authorize changes to the NAS. Saved inventories remain separate and are retained while referenced by this review."});
  });
  api.MapPost("/connections/{id:guid}/dataset-renames",async(Guid id,DatasetRenameInput input,HomelabDb db,HttpContext ctx)=>{
   var hash=Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(input)));
   var prior=await db.DatasetRenames.SingleOrDefaultAsync(x=>x.RequestId==input.RequestId);if(prior is not null)return prior.ConnectionId==id&&prior.SubmissionHash==hash?Results.Ok(prior):Results.Conflict(new{error="This request ID was already used for different evidence."});
   var from=await db.DatasetSamples.SingleOrDefaultAsync(x=>x.Id==input.FromSampleId&&x.ConnectionId==id);var to=await db.DatasetSamples.SingleOrDefaultAsync(x=>x.Id==input.ToSampleId&&x.ConnectionId==id);if(from is null||to is null)return Results.NotFound(new{error="A selected inventory is no longer retained. Reload history before reviewing."});
   if(Token(from,to)!=input.ReviewToken)return Results.Conflict(new{error="The reviewed inventories changed. Reload both before submitting."});
   if(Validate(from,to,input) is {} error)return Results.BadRequest(new{error});
   if(await db.DatasetRenames.AnyAsync(x=>x.ConnectionId==id&&x.ReversedUtc==null&&((x.FromSampleId==input.FromSampleId&&x.FromName==input.FromName)||(x.ToSampleId==input.ToSampleId&&x.ToName==input.ToName))))return Results.Conflict(new{error="This inventory entry already has an active rename review. Review or reverse that record first."});
   var row=new DatasetRenameReview{ConnectionId=id,FromSampleId=from.Id,ToSampleId=to.Id,FromName=input.FromName,ToName=input.ToName,RequestId=input.RequestId,SubmissionHash=hash,Evidence=input.Evidence,Author=ctx.User.Identity!.Name!,RecordedUtc=DateTime.UtcNow};db.DatasetRenames.Add(row);db.Activity.Add(new(){Actor=row.Author,Action="Dataset rename reviewed",Detail=JsonSerializer.Serialize(new{id,input.FromSampleId,input.ToSampleId,input.FromName,input.ToName})});await db.SaveChangesAsync();return Results.Ok(row);
  });
  api.MapPost("/connections/{id:guid}/dataset-renames/{reviewId:long}/reverse",async(Guid id,long reviewId,DatasetRenameReversal input,HomelabDb db,HttpContext ctx)=>{
   if(string.IsNullOrWhiteSpace(input.Evidence)||input.Evidence.Length>4000)return Results.BadRequest(new{error="Record the reversal reason in 1–4000 characters."});var row=await db.DatasetRenames.SingleOrDefaultAsync(x=>x.Id==reviewId&&x.ConnectionId==id);if(row is null)return Results.NotFound();
   if(row.ReversedUtc is not null)return row.ReversalEvidence==input.Evidence?Results.Ok(row):Results.Conflict(new{error="This review was already reversed with different evidence."});
   row.ReversedUtc=DateTime.UtcNow;row.ReversalEvidence=input.Evidence;row.ReversedBy=ctx.User.Identity!.Name!;db.Activity.Add(new(){Actor=row.ReversedBy,Action="Dataset rename review reversed",Detail=JsonSerializer.Serialize(new{row.Id,input.Evidence})});await db.SaveChangesAsync();return Results.Ok(row);
  });
 }
}
