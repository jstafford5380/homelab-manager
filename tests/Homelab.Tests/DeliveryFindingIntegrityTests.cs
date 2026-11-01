using Homelab.Api;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
namespace Homelab.Tests;
public class DeliveryFindingIntegrityTests {
 private sealed class Fixture:IAsyncDisposable {
  public SqliteConnection Connection {get;}=new("Data Source=:memory:");public HomelabDb Db {get;}
  public Fixture(){Connection.Open();Db=new(new DbContextOptionsBuilder<HomelabDb>().UseSqlite(Connection).Options);}
  public Task Legacy(){var migrations=Db.Database.GetMigrations().ToArray();return Db.GetService<IMigrator>().MigrateAsync(migrations[Array.FindIndex(migrations,x=>x.EndsWith("_DeliveryFindingIntegrity"))-1]);}
  public async ValueTask DisposeAsync(){await Db.DisposeAsync();await Connection.DisposeAsync();}
 }
 [Fact]public async Task ValidDeliveryAndAttemptsSurviveUpgradeAndFindingCannotBeDeleted(){
  await using var f=new Fixture();await f.Legacy();var nas=new NasConnection{Enabled=false};var finding=new Finding{ConnectionId=nas.Id};var delivery=new NotificationDelivery{FindingId=finding.Id,EventKey="fixture",PayloadJson="{\"evidence\":true}",Status="accepted"};f.Db.Connections.Add(nas);f.Db.Findings.Add(finding);f.Db.Deliveries.Add(delivery);f.Db.DeliveryAttempts.Add(new(){DeliveryId=delivery.Id,Result="accepted",HttpStatus=202});await f.Db.SaveChangesAsync();f.Db.ChangeTracker.Clear();await f.Db.Database.MigrateAsync();
  var retained=await f.Db.Deliveries.SingleAsync();Assert.Equal(finding.Id,retained.FindingId);Assert.Equal(delivery.PayloadJson,retained.PayloadJson);Assert.Equal("accepted",retained.Status);Assert.Equal(202,(await f.Db.DeliveryAttempts.SingleAsync()).HttpStatus);
  await Assert.ThrowsAsync<SqliteException>(()=>f.Db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM Findings WHERE Id={finding.Id}"));
 }
 [Fact]public async Task LegacyOrphanBlocksUpgradeAndOriginalFindingPermitsRetry(){
  await using var f=new Fixture();await f.Legacy();var id=Guid.NewGuid();f.Db.Deliveries.Add(new(){FindingId=id,EventKey="preserve",PayloadJson="{\"original\":true}"});await f.Db.SaveChangesAsync();f.Db.ChangeTracker.Clear();var error=await Assert.ThrowsAsync<SqliteException>(()=>f.Db.Database.MigrateAsync());Assert.Contains("DeliveryRequiresFinding",error.Message);var retained=await f.Db.Deliveries.SingleAsync();Assert.Equal(id,retained.FindingId);Assert.Equal("{\"original\":true}",retained.PayloadJson);Assert.DoesNotContain(await f.Db.Database.GetAppliedMigrationsAsync(),x=>x.EndsWith("_DeliveryFindingIntegrity"));
  var nas=new NasConnection{Enabled=false};f.Db.Connections.Add(nas);f.Db.Findings.Add(new(){Id=id,ConnectionId=nas.Id});await f.Db.SaveChangesAsync();await f.Db.Database.MigrateAsync();Assert.Single(await f.Db.Deliveries.ToListAsync());
 }
 [Fact]public async Task NewOrphanDeliveryIsRejected(){await using var f=new Fixture();await f.Db.Database.MigrateAsync();f.Db.Deliveries.Add(new(){FindingId=Guid.NewGuid(),EventKey="orphan"});await Assert.ThrowsAsync<DbUpdateException>(()=>f.Db.SaveChangesAsync());}
}
