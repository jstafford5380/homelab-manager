using Microsoft.EntityFrameworkCore;
namespace Homelab.Api;
public record ActivityHistoryPage(long SnapshotId, AuditEvent[] Items, long? NextBefore);
public static class ActivityHistory {
 public static async Task<ActivityHistoryPage> ReadAsync(HomelabDb db,long? snapshotId=null,long? before=null,CancellationToken ct=default) {
  var ceiling=snapshotId ?? await db.Activity.Select(x=>(long?)x.Id).MaxAsync(ct) ?? 0;
  var rows=await db.Activity.AsNoTracking().Where(x=>x.Id<=ceiling && (before==null || x.Id<before)).OrderByDescending(x=>x.Id).Take(51).ToArrayAsync(ct);
  var items=rows.Take(50).ToArray();return new(ceiling,items,rows.Length>50?items[^1].Id:null);
 }
 public static void MapActivityHistory(this RouteGroupBuilder api)=>api.MapGet("/activity/history",async(long? snapshotId,long? before,HomelabDb db,CancellationToken ct)=>{
  if(snapshotId<0 || before<=0 || before is not null && (snapshotId is null || before>snapshotId))return Results.BadRequest(new{error="Use a valid activity snapshot and cursor."});
  return Results.Ok(await ReadAsync(db,snapshotId,before,ct));
 });
}
