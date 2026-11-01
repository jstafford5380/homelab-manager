using Homelab.Api;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using System.Text.Json;
namespace Homelab.Tests;
public class DatasetRenameTests {
 static DatasetInventorySample Sample(long id,Guid nas,DateTime at,string name)=>new(){Id=id,ConnectionId=nas,ConfigurationRevision=Guid.NewGuid(),SystemSerial="same-nas",FetchedUtc=at,HourUtc=at,SnapshotJson=JsonSerializer.Serialize(new DatasetSnapshot(at,[new(name,"tank","FILESYSTEM",1,null,0,0,null)]))};
 static DatasetRenameInput Input()=>new(1,2,"tank/old","tank/new","tank/old","tank/new","token","Reviewed independent rename log",true,Guid.NewGuid());
 [Theory][InlineData("valid")][InlineData("nas")][InlineData("time")][InlineData("unknown")][InlineData("identity")][InlineData("confirmation")][InlineData("assertion")][InlineData("evidence")][InlineData("missing")][InlineData("coexisting")][InlineData("type")][InlineData("pool")][InlineData("malformed")]
 public void ReviewRequiresSpecificNonConflictingEvidence(string scenario){
  var nas=Guid.NewGuid();var at=DateTime.UtcNow;var from=Sample(1,nas,at.AddHours(-1),"tank/old");var to=Sample(2,nas,at,"tank/new");var input=Input();
  switch(scenario){case "nas":to.ConnectionId=Guid.NewGuid();break;case "time":from.FetchedUtc=to.FetchedUtc;break;case "unknown":from.SystemSerial=null;break;case "identity":to.SystemSerial="different";break;case "confirmation":input=input with{ConfirmedFromName="wrong"};break;case "assertion":input=input with{ConfirmedSameDataset=false};break;case "evidence":input=input with{Evidence=""};break;case "missing":input=input with{FromName="absent",ConfirmedFromName="absent"};break;case "coexisting":to.SnapshotJson=JsonSerializer.Serialize(new DatasetSnapshot(at,[new("tank/new","tank","FILESYSTEM",1,null,0,0,null),new("tank/old","tank","FILESYSTEM",1,null,0,0,null)]));break;case "type":to.SnapshotJson=to.SnapshotJson.Replace("FILESYSTEM","VOLUME");break;case "pool":to.SnapshotJson=to.SnapshotJson.Replace("\"Pool\":\"tank\"","\"Pool\":\"other\"");break;case "malformed":to.SnapshotJson="null";break;}
  if(scenario=="valid")Assert.Null(DatasetRenames.Validate(from,to,input));else Assert.NotNull(DatasetRenames.Validate(from,to,input));
 }
 [Fact]public void ReviewTokenBindsExactSnapshotsAndSourceContext(){var nas=Guid.NewGuid();var at=DateTime.UtcNow;var from=Sample(1,nas,at.AddHours(-1),"tank/old");var to=Sample(2,nas,at,"tank/new");var token=DatasetRenames.Token(from,to);to.SnapshotJson=to.SnapshotJson.Replace("tank/new","tank/other");Assert.NotEqual(token,DatasetRenames.Token(from,to));}
 [Fact]public async Task ReviewsPinOriginalSamplesIncludingAfterReversalAndRejectCrossNasLinks(){
  await using var connection=new SqliteConnection("Data Source=:memory:");await connection.OpenAsync();await using var db=new HomelabDb(new DbContextOptionsBuilder<HomelabDb>().UseSqlite(connection).Options);await db.Database.MigrateAsync();var nas=new NasConnection{Enabled=false};var other=new NasConnection{Enabled=false};db.Connections.AddRange(nas,other);var at=DateTime.UtcNow.AddDays(-100);var from=Sample(1,nas.Id,at,"tank/old");var to=Sample(2,nas.Id,at.AddHours(1),"tank/new");var unused=Sample(3,nas.Id,at.AddHours(2),"tank/unrelated");db.DatasetSamples.AddRange(from,to,unused);await db.SaveChangesAsync();
  var review=new DatasetRenameReview{ConnectionId=nas.Id,FromSampleId=1,ToSampleId=2,RequestId=Guid.NewGuid(),FromName="tank/old",ToName="tank/new",Evidence="Retain proof",RecordedUtc=DateTime.UtcNow};db.DatasetRenames.Add(review);await db.SaveChangesAsync();Assert.Equal(1,await DatasetHistory.ExpireAsync(db,DateTime.UtcNow));review.ReversedUtc=DateTime.UtcNow;review.ReversalEvidence="Corrected review";await db.SaveChangesAsync();Assert.Equal(0,await DatasetHistory.ExpireAsync(db,DateTime.UtcNow));Assert.Equal(2,await db.DatasetSamples.CountAsync());
  db.DatasetRenames.Add(new(){ConnectionId=other.Id,FromSampleId=1,ToSampleId=2,RequestId=Guid.NewGuid()});await Assert.ThrowsAsync<DbUpdateException>(()=>db.SaveChangesAsync());
 }
 [Fact]public async Task UpgradePreservesExistingInventoryAndAddsCompositeParentKey(){
  await using var connection=new SqliteConnection("Data Source=:memory:");await connection.OpenAsync();await using var db=new HomelabDb(new DbContextOptionsBuilder<HomelabDb>().UseSqlite(connection).Options);var migrations=db.Database.GetMigrations().ToArray();await db.GetService<IMigrator>().MigrateAsync(migrations[Array.FindIndex(migrations,x=>x.EndsWith("_DatasetRenameReviews"))-1]);var nas=new NasConnection{Enabled=false};db.Connections.Add(nas);var sample=Sample(1,nas.Id,DateTime.UtcNow,"tank/original");db.DatasetSamples.Add(sample);await db.SaveChangesAsync();db.ChangeTracker.Clear();await db.Database.MigrateAsync();var retained=await db.DatasetSamples.SingleAsync();Assert.Equal(sample.SnapshotJson,retained.SnapshotJson);Assert.Equal(sample.ConfigurationRevision,retained.ConfigurationRevision);Assert.Empty(await db.DatasetRenames.ToListAsync());
 }
}
