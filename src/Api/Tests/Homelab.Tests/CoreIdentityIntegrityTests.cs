using Homelab.Api;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
namespace Homelab.Tests;
public class CoreIdentityIntegrityTests {
 private sealed class Fixture:IAsyncDisposable {
  public SqliteConnection Connection {get;}=new("Data Source=:memory:");public HomelabDb Db {get;}
  public NasConnection Nas {get;}=new(){Enabled=false};public NasConnection Other {get;}=new(){Enabled=false};public Disk Disk {get;}=new();
  public Fixture(){Connection.Open();Db=new(new DbContextOptionsBuilder<HomelabDb>().UseSqlite(Connection).Options);Disk.ConnectionId=Nas.Id;}
  public async Task Init(bool legacy){if(legacy){var migrations=Db.Database.GetMigrations().ToArray();await Db.GetService<IMigrator>().MigrateAsync(migrations[Array.FindIndex(migrations,x=>x.EndsWith("_CoreIdentityIntegrity"))-1]);}else await Db.Database.MigrateAsync();Db.Connections.AddRange(Nas,Other);Db.Disks.Add(Disk);await Db.SaveChangesAsync();}
  public void Invalid(string scenario){var missing=Guid.NewGuid();if(scenario=="disk")Db.Disks.Add(new(){ConnectionId=missing});else{
   var nas=scenario.EndsWith("nas")?missing:scenario.EndsWith("cross")?Other.Id:Nas.Id;var disk=scenario.EndsWith("disk")?missing:Disk.Id;
   if(scenario.StartsWith("observation"))Db.Observations.Add(new(){ConnectionId=nas,DiskId=disk,SourceKey="preserve",Metric="temperature",Value=42});else Db.Findings.Add(new(){ConnectionId=nas,DiskId=disk,Evidence="preserve"});}}
  public async Task<int> Count()=>await Db.Disks.CountAsync()+await Db.Observations.CountAsync()+await Db.Findings.CountAsync();
  public async ValueTask DisposeAsync(){await Db.DisposeAsync();await Connection.DisposeAsync();}
 }
 [Theory][InlineData("disk")][InlineData("observation-nas")][InlineData("finding-nas")][InlineData("observation-disk")][InlineData("finding-disk")][InlineData("observation-cross")][InlineData("finding-cross")]
 public async Task LegacyInvalidIdentityBlocksMigrationWithoutDiscardingOrReassigning(string scenario){
  await using var f=new Fixture();await f.Init(true);f.Invalid(scenario);await f.Db.SaveChangesAsync();f.Db.ChangeTracker.Clear();var count=await f.Count();
  var error=await Assert.ThrowsAsync<SqliteException>(()=>f.Db.Database.MigrateAsync());Assert.Contains("CoreEvidenceRequiresMatchingIdentity",error.Message);Assert.Equal(count,await f.Count());Assert.DoesNotContain(await f.Db.Database.GetAppliedMigrationsAsync(),x=>x.EndsWith("_CoreIdentityIntegrity"));
  if(scenario.StartsWith("observation")){var row=await f.Db.Observations.SingleAsync();Assert.Equal(42,row.Value);if(scenario.EndsWith("cross"))Assert.Equal(f.Other.Id,row.ConnectionId);}
  if(scenario.StartsWith("finding")){var row=await f.Db.Findings.SingleAsync();Assert.Equal("preserve",row.Evidence);if(scenario.EndsWith("cross"))Assert.Equal(f.Other.Id,row.ConnectionId);}
 }
 [Theory][InlineData("disk")][InlineData("observation-nas")][InlineData("finding-nas")][InlineData("observation-disk")][InlineData("finding-disk")][InlineData("observation-cross")][InlineData("finding-cross")]
 public async Task StorageRejectsMissingAndCrossNasIdentity(string scenario){await using var f=new Fixture();await f.Init(false);f.Invalid(scenario);await Assert.ThrowsAsync<DbUpdateException>(()=>f.Db.SaveChangesAsync());}
 [Fact]public async Task ValidLegacyUpgradePreservesScopedAndNasLevelEvidenceAndRestrictsDeletion(){
  await using var f=new Fixture();await f.Init(true);
  f.Db.Observations.AddRange(new(){ConnectionId=f.Nas.Id,DiskId=f.Disk.Id,SourceKey="drive",Value=42},new(){ConnectionId=f.Nas.Id,SourceKey="nas",Value=7});
  f.Db.Findings.AddRange(new(){ConnectionId=f.Nas.Id,DiskId=f.Disk.Id,Evidence="drive evidence"},new(){ConnectionId=f.Nas.Id,Evidence="pool evidence"});await f.Db.SaveChangesAsync();f.Db.ChangeTracker.Clear();await f.Db.Database.MigrateAsync();
  Assert.Equal(2,await f.Db.Observations.CountAsync());Assert.Equal(2,await f.Db.Findings.CountAsync());Assert.Equal(42,(await f.Db.Observations.SingleAsync(x=>x.DiskId!=null)).Value);Assert.Equal("drive evidence",(await f.Db.Findings.SingleAsync(x=>x.DiskId!=null)).Evidence);
  await Assert.ThrowsAsync<SqliteException>(()=>f.Db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM Disks WHERE Id={f.Disk.Id}"));
  await Assert.ThrowsAsync<SqliteException>(()=>f.Db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM Connections WHERE Id={f.Nas.Id}"));
  await Assert.ThrowsAsync<SqliteException>(()=>f.Db.Database.ExecuteSqlInterpolatedAsync($"UPDATE Disks SET ConnectionId={f.Other.Id} WHERE Id={f.Disk.Id}"));
 }
}
