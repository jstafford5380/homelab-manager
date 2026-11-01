using System.Text.Json;
using Microsoft.EntityFrameworkCore;
namespace Homelab.Api;
public sealed class DatasetInventorySample {
 public long Id {get;set;}
 public Guid ConnectionId {get;set;}
 public Guid ConfigurationRevision {get;set;}
 public DateTime HourUtc {get;set;}
 public DateTime FetchedUtc {get;set;}
 public string? SystemSerial {get;set;}
 public string? Version {get;set;}
 public string SnapshotJson {get;set;}="";
}
public static class DatasetHistory {
 public static async Task StageAsync(HomelabDb db,NasConnection nas,DatasetSnapshot snapshot,CancellationToken ct=default){
  if(snapshot.FetchedUtc.Kind!=DateTimeKind.Utc || snapshot.Items.Length>256)throw new InvalidDataException("Invalid dataset history sample.");
  var hour=new DateTime(snapshot.FetchedUtc.Ticks-snapshot.FetchedUtc.Ticks%TimeSpan.TicksPerHour,DateTimeKind.Utc);
  if(db.DatasetSamples.Local.Any(x=>x.ConnectionId==nas.Id&&x.ConfigurationRevision==nas.ConfigurationRevision&&x.HourUtc==hour) || await db.DatasetSamples.AnyAsync(x=>x.ConnectionId==nas.Id&&x.ConfigurationRevision==nas.ConfigurationRevision&&x.HourUtc==hour,ct))return;
  db.DatasetSamples.Add(new(){ConnectionId=nas.Id,ConfigurationRevision=nas.ConfigurationRevision,HourUtc=hour,FetchedUtc=snapshot.FetchedUtc,SystemSerial=nas.ObservedSystemSerial,Version=nas.Version,SnapshotJson=JsonSerializer.Serialize(snapshot)});
 }
 public static async Task<int> ExpireAsync(HomelabDb db,DateTime cutoff,CancellationToken ct=default){
  var ids=await db.DatasetSamples.Where(x=>x.FetchedUtc<cutoff&&!db.DatasetRenames.Any(r=>r.FromSampleId==x.Id||r.ToSampleId==x.Id)).OrderBy(x=>x.Id).Take(PoolRetention.BatchSize).Select(x=>x.Id).ToArrayAsync(ct);
  return await db.DatasetSamples.Where(x=>ids.Contains(x.Id)).ExecuteDeleteAsync(ct);
 }
 public static void MapDatasetHistory(this RouteGroupBuilder api)=>api.MapGet("/connections/{id:guid}/dataset-history",async(Guid id,long? snapshotId,long? before,HomelabDb db,CancellationToken ct)=>{
  if(snapshotId<0||before<=0||before is not null&&(snapshotId is null||before>snapshotId))return Results.BadRequest(new{error="Use a valid dataset history cursor."});
  if(!await db.Connections.AnyAsync(x=>x.Id==id,ct))return Results.NotFound();
  var ceiling=snapshotId??await db.DatasetSamples.Where(x=>x.ConnectionId==id).MaxAsync(x=>(long?)x.Id,ct)??0;
  var rows=await db.DatasetSamples.AsNoTracking().Where(x=>x.ConnectionId==id&&x.Id<=ceiling&&(before==null||x.Id<before)).OrderByDescending(x=>x.Id).Take(11).ToArrayAsync(ct);
  var settings=await db.Settings.AsNoTracking().SingleOrDefaultAsync(ct)??new OperatingSettings();
  return Results.Ok(new{snapshotId=ceiling,items=rows.Take(10).Select(x=>new{x.Id,x.ConfigurationRevision,x.FetchedUtc,x.SystemSerial,x.Version,snapshot=JsonSerializer.Deserialize<DatasetSnapshot>(x.SnapshotJson)}),nextBefore=rows.Length>10?(long?)rows[9].Id:null,retentionDays=settings.SampleRetentionDays,
   sampling="First successful inventory per UTC hour and connection revision. Dataset names do not establish identity or rename continuity. Missing samples are gaps, not evidence of deletion."});
 });
}
