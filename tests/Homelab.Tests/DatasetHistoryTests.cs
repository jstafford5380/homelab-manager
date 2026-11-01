using Homelab.Api;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using System.Text.Json;
namespace Homelab.Tests;
public class DatasetHistoryTests {
 private sealed class Fixture:IAsyncDisposable {
  public SqliteConnection Connection {get;}=new("Data Source=:memory:");public HomelabDb Db {get;}public NasConnection Nas {get;}=new(){Enabled=false,ObservedSystemSerial="source",Version="24.10.2"};
  public Fixture(){Connection.Open();Db=new(new DbContextOptionsBuilder<HomelabDb>().UseSqlite(Connection).Options);Db.Database.EnsureCreated();Db.Connections.Add(Nas);Db.SaveChanges();}
  public async ValueTask DisposeAsync(){await Db.DisposeAsync();await Connection.DisposeAsync();}
 }
 private static DatasetSnapshot Sample(DateTime at,string name="tank/photos")=>new(at,[new(name,"tank","FILESYSTEM",42,null,0,0,null)]);
 [Fact]public async Task FirstSamplePerHourAndRevisionIsImmutableAndReplaySafe(){
  await using var f=new Fixture();var at=new DateTime(2026,11,1,12,1,0,DateTimeKind.Utc);
  await DatasetHistory.StageAsync(f.Db,f.Nas,Sample(at));await DatasetHistory.StageAsync(f.Db,f.Nas,Sample(at.AddMinutes(2),"tank/renamed"));await f.Db.SaveChangesAsync();
  await DatasetHistory.StageAsync(f.Db,f.Nas,Sample(at.AddMinutes(3)));await f.Db.SaveChangesAsync();Assert.Single(await f.Db.DatasetSamples.ToListAsync());
  var row=await f.Db.DatasetSamples.SingleAsync();var snapshot=JsonSerializer.Deserialize<DatasetSnapshot>(row.SnapshotJson)!;Assert.Equal(at,snapshot.FetchedUtc);Assert.Equal("tank/photos",snapshot.Items[0].Name);Assert.Null(snapshot.Items[0].AvailableBytes);Assert.Equal(0,snapshot.Items[0].QuotaBytes);Assert.Equal("source",row.SystemSerial);
  await DatasetHistory.StageAsync(f.Db,f.Nas,Sample(at.AddHours(1),"tank/renamed"));f.Nas.ConfigurationRevision=Guid.NewGuid();await DatasetHistory.StageAsync(f.Db,f.Nas,Sample(at.AddHours(1),"tank/new"));await f.Db.SaveChangesAsync();Assert.Equal(3,await f.Db.DatasetSamples.CountAsync());
 }
 [Fact]public async Task EmptySuccessfulInventoryRemainsEvidenceAndNasScopeIsSeparate(){await using var f=new Fixture();var other=new NasConnection{Enabled=false};f.Db.Connections.Add(other);var at=DateTime.UtcNow;await DatasetHistory.StageAsync(f.Db,f.Nas,new(at,[]));await DatasetHistory.StageAsync(f.Db,other,Sample(at));await f.Db.SaveChangesAsync();Assert.Equal(2,await f.Db.DatasetSamples.CountAsync());Assert.Empty(JsonSerializer.Deserialize<DatasetSnapshot>((await f.Db.DatasetSamples.SingleAsync(x=>x.ConnectionId==f.Nas.Id)).SnapshotJson)!.Items);}
 [Fact]public async Task RetentionRequiresSuccessfulBackupAndKeepsExactBoundary(){
  await using var f=new Fixture();var now=DateTime.UtcNow;var cutoff=PoolRetention.DetailCutoff(now,90);await DatasetHistory.StageAsync(f.Db,f.Nas,Sample(cutoff.AddHours(-1)));await DatasetHistory.StageAsync(f.Db,f.Nas,Sample(cutoff));await f.Db.SaveChangesAsync();
  var root=Path.Combine(Path.GetTempPath(),"dataset-history-"+Guid.NewGuid());Directory.CreateDirectory(root);
  try{BackupService Backup(string? path)=>new(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{{"Homelab:DataPath",Path.Combine(root,"data")},{"Homelab:BackupPath",path}}).Build());
   Assert.Equal(0,await HistoryRetention.MaintainAsync(f.Db,Backup(null),now));Assert.Equal(2,await f.Db.DatasetSamples.CountAsync());
   var bad=Path.Combine(root,"not-directory");await File.WriteAllTextAsync(bad,"fixture");await Assert.ThrowsAsync<IOException>(()=>HistoryRetention.MaintainAsync(f.Db,Backup(bad),now));Assert.Equal(2,await f.Db.DatasetSamples.CountAsync());
   Assert.Equal(1,await HistoryRetention.MaintainAsync(f.Db,Backup(Path.Combine(root,"backups")),now));Assert.Equal(cutoff,(await f.Db.DatasetSamples.SingleAsync()).FetchedUtc);Assert.Single(Directory.GetFiles(Path.Combine(root,"backups"),"*.zip"));
  }finally{Directory.Delete(root,true);}
 }
}
