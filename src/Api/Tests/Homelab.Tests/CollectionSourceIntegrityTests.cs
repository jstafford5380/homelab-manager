using Homelab.Api;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using System.Text.Json;
namespace Homelab.Tests;
public class CollectionSourceIntegrityTests {
 private sealed class Fixture:IAsyncDisposable {
  public SqliteConnection Connection {get;}=new("Data Source=:memory:");public HomelabDb Db {get;}
  public Fixture(){Connection.Open();Db=new(new DbContextOptionsBuilder<HomelabDb>().UseSqlite(Connection).Options);}
  public Task Legacy(){var migrations=Db.Database.GetMigrations().ToArray();return Db.GetService<IMigrator>().MigrateAsync(migrations[Array.FindIndex(migrations,x=>x.EndsWith("_CollectionSourceIntegrity"))-1]);}
  public async ValueTask DisposeAsync(){await Db.DisposeAsync();await Connection.DisposeAsync();}
 }
 private static object Evidence(string kind,Guid id)=>kind switch {
  "capability"=>new Capability{ConnectionId=id,Name="pools",Status="denied",Detail="preserved permission evidence"},
  "alert"=>new NativeAlert{ConnectionId=id,SourceId="fixture",Message="preserved alert",DismissedOnNas=true},
  "run"=>new CollectionRun{ConnectionId=id,Result="partial",CapabilitySummaryJson="{\"fixture\":true}"},
  _=>new CollectionDailySummary{ConnectionId=id,GroupKey="fixture",AttemptCount=12,CapabilitiesKnown=false}
 };
 private static async Task<string> Read(HomelabDb db,string kind)=>kind switch {
  "capability"=>JsonSerializer.Serialize(await db.Capabilities.AsNoTracking().SingleAsync()),
  "alert"=>JsonSerializer.Serialize(await db.NativeAlerts.AsNoTracking().SingleAsync()),
  "run"=>JsonSerializer.Serialize(await db.CollectionRuns.AsNoTracking().SingleAsync()),
  _=>JsonSerializer.Serialize(await db.CollectionDailySummaries.AsNoTracking().SingleAsync())
 };
 [Theory][InlineData("capability")][InlineData("alert")][InlineData("run")][InlineData("summary")]
 public async Task ValidUpgradePreservesExactEvidenceAndRestrictsSourceDeletion(string kind){
  await using var f=new Fixture();await f.Legacy();var nas=new NasConnection{Enabled=false};f.Db.Connections.Add(nas);f.Db.Add(Evidence(kind,nas.Id));await f.Db.SaveChangesAsync();var original=await Read(f.Db,kind);f.Db.ChangeTracker.Clear();await f.Db.Database.MigrateAsync();Assert.Equal(original,await Read(f.Db,kind));
  await Assert.ThrowsAsync<SqliteException>(()=>f.Db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM Connections WHERE Id={nas.Id}"));Assert.Single(await f.Db.Connections.ToListAsync());
 }
 [Theory][InlineData("capability")][InlineData("alert")][InlineData("run")][InlineData("summary")]
 public async Task LegacyOrphanBlocksUpgradeWithoutLosingEvidenceAndOriginalParentAllowsRetry(string kind){
  await using var f=new Fixture();await f.Legacy();var id=Guid.NewGuid();f.Db.Add(Evidence(kind,id));await f.Db.SaveChangesAsync();var original=await Read(f.Db,kind);f.Db.ChangeTracker.Clear();
  var error=await Assert.ThrowsAsync<SqliteException>(()=>f.Db.Database.MigrateAsync());Assert.Contains("CollectionEvidenceRequiresNas",error.Message);Assert.DoesNotContain(await f.Db.Database.GetAppliedMigrationsAsync(),x=>x.EndsWith("_CollectionSourceIntegrity"));Assert.Equal(original,await Read(f.Db,kind));Assert.Empty(await f.Db.Connections.ToListAsync());
  f.Db.Connections.Add(new(){Id=id,Enabled=false});await f.Db.SaveChangesAsync();await f.Db.Database.MigrateAsync();Assert.Equal(original,await Read(f.Db,kind));
 }
 [Theory][InlineData("capability")][InlineData("alert")][InlineData("run")][InlineData("summary")]
 public async Task NewOrphanIsRejectedByStorage(string kind){
  await using var f=new Fixture();await f.Db.Database.MigrateAsync();f.Db.Add(Evidence(kind,Guid.NewGuid()));await Assert.ThrowsAsync<DbUpdateException>(()=>f.Db.SaveChangesAsync());
 }
}
