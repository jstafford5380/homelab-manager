using Homelab.Api;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using System.Text.Json;
namespace Homelab.Tests;
public class DriveRecordIntegrityTests {
 private sealed class Fixture:IAsyncDisposable {
  public SqliteConnection Connection {get;}=new("Data Source=:memory:");public HomelabDb Db {get;} public Guid NasId {get;}=Guid.NewGuid();
  public Fixture(){Connection.Open();Db=new(new DbContextOptionsBuilder<HomelabDb>().UseSqlite(Connection).Options);}
  public async Task Legacy(){var migrations=Db.Database.GetMigrations().ToArray();await Db.GetService<IMigrator>().MigrateAsync(migrations[Array.FindIndex(migrations,x=>x.EndsWith("_DriveRecordIntegrity"))-1]);Db.Connections.Add(new(){Id=NasId,Enabled=false});await LegacyDiskFixture.SaveAsync(Db);}
  public async ValueTask DisposeAsync(){await Db.DisposeAsync();await Connection.DisposeAsync();}
 }
 private static object Evidence(string kind,Guid id)=>kind switch {
  "plan"=>new PlanningItem{DiskId=id,Title="replacement budget",Budget=250,Reason="retained evidence"},
  "alias"=>new DiskAlias{DiskId=id,Path="/dev/old-path",EndedUtc=DateTime.UtcNow},
  _=>new DiskAnnotation{DiskId=id,Actor="owner",BeforeJson="{\"old\":true}",AfterJson="{\"new\":true}"}
 };
 private static async Task<string> Read(HomelabDb db,string kind)=>kind switch {
  "plan"=>JsonSerializer.Serialize(await db.Plans.AsNoTracking().SingleAsync()),
  "alias"=>JsonSerializer.Serialize(await db.DiskAliases.AsNoTracking().SingleAsync()),
  _=>JsonSerializer.Serialize(await db.DiskAnnotations.AsNoTracking().SingleAsync())
 };
 [Theory][InlineData("plan")][InlineData("alias")][InlineData("annotation")]
 public async Task ValidUpgradePreservesExactEvidenceAndRestrictsDriveDeletion(string kind){
  await using var f=new Fixture();await f.Legacy();var nas=new Disk{ConnectionId=f.NasId};f.Db.Disks.Add(nas);f.Db.Add(Evidence(kind,nas.Id));await LegacyDiskFixture.SaveAsync(f.Db);var original=await Read(f.Db,kind);f.Db.ChangeTracker.Clear();await f.Db.Database.MigrateAsync();Assert.Equal(original,await Read(f.Db,kind));
  await Assert.ThrowsAsync<SqliteException>(()=>f.Db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM Disks WHERE Id={nas.Id}"));Assert.Single(await f.Db.Disks.ToListAsync());
 }
 [Theory][InlineData("plan")][InlineData("alias")][InlineData("annotation")]
 public async Task LegacyOrphanBlocksUpgradeWithoutLosingEvidenceAndOriginalParentAllowsRetry(string kind){
  await using var f=new Fixture();await f.Legacy();var id=Guid.NewGuid();f.Db.Add(Evidence(kind,id));await LegacyDiskFixture.SaveAsync(f.Db);var original=await Read(f.Db,kind);f.Db.ChangeTracker.Clear();
  var error=await Assert.ThrowsAsync<SqliteException>(()=>f.Db.Database.MigrateAsync());Assert.Contains("DriveRecordRequiresDisk",error.Message);Assert.DoesNotContain(await f.Db.Database.GetAppliedMigrationsAsync(),x=>x.EndsWith("_DriveRecordIntegrity"));Assert.Equal(original,await Read(f.Db,kind));Assert.Empty(await f.Db.Disks.Select(x=>x.Id).ToListAsync());
  f.Db.Disks.Add(new(){Id=id,ConnectionId=f.NasId});await LegacyDiskFixture.SaveAsync(f.Db);await f.Db.Database.MigrateAsync();Assert.Equal(original,await Read(f.Db,kind));
 }
 [Theory][InlineData("plan")][InlineData("alias")][InlineData("annotation")]
 public async Task NewOrphanIsRejectedByStorage(string kind){
  await using var f=new Fixture();await f.Db.Database.MigrateAsync();f.Db.Add(Evidence(kind,Guid.NewGuid()));await Assert.ThrowsAsync<DbUpdateException>(()=>f.Db.SaveChangesAsync());
 }
 [Fact]public async Task GeneralPlanCanRemainUnlinked(){
  await using var f=new Fixture();await f.Legacy();f.Db.Plans.Add(new(){Title="General budget"});await LegacyDiskFixture.SaveAsync(f.Db);await f.Db.Database.MigrateAsync();Assert.Null((await f.Db.Plans.SingleAsync()).DiskId);
  f.Db.Plans.Add(new(){Title="Another general plan"});await LegacyDiskFixture.SaveAsync(f.Db);Assert.Equal(2,await f.Db.Plans.CountAsync());
 }
}
