using Homelab.Api;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
namespace Homelab.Tests;
public class NotificationIntegrityTests {
 private sealed class Fixture:IAsyncDisposable {
  public SqliteConnection Connection {get;}=new("Data Source=:memory:");public HomelabDb Db {get;} public Guid FindingId {get;}=Guid.NewGuid();
  public Fixture(){Connection.Open();Db=new(new DbContextOptionsBuilder<HomelabDb>().UseSqlite(Connection).Options);}
  public async Task Legacy(){var migrations=Db.Database.GetMigrations().ToArray();await Db.GetService<IMigrator>().MigrateAsync(migrations[Array.FindIndex(migrations,x=>x.EndsWith("_NotificationAttemptIntegrity"))-1]);var nas=new NasConnection{Enabled=false};Db.Connections.Add(nas);Db.Findings.Add(new(){Id=FindingId,ConnectionId=nas.Id});await Db.SaveChangesAsync();}
  public async ValueTask DisposeAsync(){await Db.DisposeAsync();await Connection.DisposeAsync();}
 }
 [Fact]public async Task UpgradePreservesAttemptsAndRestrictsParentDeletion(){
  await using var f=new Fixture();await f.Legacy();var delivery=new NotificationDelivery{FindingId=f.FindingId,EventKey="fixture"};f.Db.Deliveries.Add(delivery);f.Db.DeliveryAttempts.Add(new(){DeliveryId=delivery.Id,Result="outcome-unknown",HttpStatus=null});await f.Db.SaveChangesAsync();f.Db.ChangeTracker.Clear();
  await f.Db.Database.MigrateAsync();var attempt=await f.Db.DeliveryAttempts.SingleAsync();Assert.Equal(delivery.Id,attempt.DeliveryId);Assert.Equal("outcome-unknown",attempt.Result);Assert.Null(attempt.HttpStatus);
  await Assert.ThrowsAsync<SqliteException>(()=>f.Db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM Deliveries WHERE Id={delivery.Id}"));Assert.Single(await f.Db.Deliveries.ToListAsync());
 }
 [Fact]public async Task LegacyOrphanBlocksUpgradeWithoutDiscardingEvidenceAndOriginalParentAllowsRetry(){
  await using var f=new Fixture();await f.Legacy();var id=Guid.NewGuid();f.Db.DeliveryAttempts.Add(new(){DeliveryId=id,Result="accepted",HttpStatus=202});await f.Db.SaveChangesAsync();f.Db.ChangeTracker.Clear();
  var error=await Assert.ThrowsAsync<SqliteException>(()=>f.Db.Database.MigrateAsync());Assert.Contains("NotificationAttemptRequiresDelivery",error.Message);Assert.DoesNotContain(await f.Db.Database.GetAppliedMigrationsAsync(),x=>x.EndsWith("_NotificationAttemptIntegrity"));
  var attempt=await f.Db.DeliveryAttempts.SingleAsync();Assert.Equal(id,attempt.DeliveryId);Assert.Equal(202,attempt.HttpStatus);Assert.Empty(await f.Db.Deliveries.ToListAsync());
  f.Db.Deliveries.Add(new(){Id=id,FindingId=f.FindingId,EventKey="restored original delivery"});await f.Db.SaveChangesAsync();await f.Db.Database.MigrateAsync();Assert.Single(await f.Db.DeliveryAttempts.ToListAsync());
 }
 [Fact]public async Task NewOrphanIsRejectedWithoutApiValidation(){
  await using var f=new Fixture();await f.Db.Database.MigrateAsync();f.Db.DeliveryAttempts.Add(new(){DeliveryId=Guid.NewGuid(),Result="accepted"});await Assert.ThrowsAsync<DbUpdateException>(()=>f.Db.SaveChangesAsync());f.Db.ChangeTracker.Clear();Assert.Empty(await f.Db.DeliveryAttempts.ToListAsync());
 }
}
