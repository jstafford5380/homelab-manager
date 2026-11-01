using Homelab.Api;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using System.Text.Json;
namespace Homelab.Tests;
public class ReviewedIdentityIntegrityTests {
 private sealed class Fixture:IAsyncDisposable {
  public SqliteConnection Connection {get;}=new("Data Source=:memory:");public HomelabDb Db {get;}
  public NasConnection Nas {get;}=new(){Enabled=false};public NasConnection Other {get;}=new(){Enabled=false};public Disk First {get;}=new();public Disk Second {get;}=new();public Disk Foreign {get;}=new();
  public Fixture(){Connection.Open();Db=new(new DbContextOptionsBuilder<HomelabDb>().UseSqlite(Connection).Options);First.ConnectionId=Second.ConnectionId=Nas.Id;Foreign.ConnectionId=Other.Id;}
  public async Task Init(bool legacy){if(legacy){var migrations=Db.Database.GetMigrations().ToArray();await Db.GetService<IMigrator>().MigrateAsync(migrations[Array.FindIndex(migrations,x=>x.EndsWith("_ReviewedIdentityIntegrity"))-1]);}else await Db.Database.MigrateAsync();Db.Connections.AddRange(Nas,Other);Db.Disks.AddRange(First,Second,Foreign);await Db.SaveChangesAsync();}
  public void Add(string kind,bool valid){if(kind=="assignment")Db.DiscoveryAssignments.Add(new(){ConnectionId=Nas.Id,DiskId=valid?First.Id:Foreign.Id,RequestId=Guid.NewGuid(),Evidence="original physical review"});else Db.IdentityMerges.Add(new(){ConnectionId=Nas.Id,FirstDiskId=!valid&&kind=="first"?Foreign.Id:First.Id,SecondDiskId=!valid&&kind=="second"?Foreign.Id:Second.Id,RequestId=Guid.NewGuid(),Evidence="original history review"});}
  public async Task<string> Read(string kind)=>kind=="assignment"?JsonSerializer.Serialize(await Db.DiscoveryAssignments.AsNoTracking().SingleAsync()):JsonSerializer.Serialize(await Db.IdentityMerges.AsNoTracking().SingleAsync());
  public async ValueTask DisposeAsync(){await Db.DisposeAsync();await Connection.DisposeAsync();}
 }
 [Theory][InlineData("assignment")][InlineData("first")][InlineData("second")]
 public async Task LegacyCrossNasReviewBlocksUpgradeWithoutReassignment(string kind){await using var f=new Fixture();await f.Init(true);f.Add(kind,false);await f.Db.SaveChangesAsync();var original=await f.Read(kind);f.Db.ChangeTracker.Clear();var error=await Assert.ThrowsAsync<SqliteException>(()=>f.Db.Database.MigrateAsync());Assert.Contains("ReviewedIdentityRequiresSameNas",error.Message);Assert.Equal(original,await f.Read(kind));Assert.DoesNotContain(await f.Db.Database.GetAppliedMigrationsAsync(),x=>x.EndsWith("_ReviewedIdentityIntegrity"));}
 [Theory][InlineData("assignment")][InlineData("first")][InlineData("second")]
 public async Task StorageRejectsCrossNasReview(string kind){await using var f=new Fixture();await f.Init(false);f.Add(kind,false);await Assert.ThrowsAsync<DbUpdateException>(()=>f.Db.SaveChangesAsync());}
 [Theory][InlineData("assignment")][InlineData("first")][InlineData("second")]
 public async Task ValidReviewSurvivesUpgradeAndBlocksParentDeletion(string kind){await using var f=new Fixture();await f.Init(true);f.Add(kind,true);await f.Db.SaveChangesAsync();var original=await f.Read(kind);f.Db.ChangeTracker.Clear();await f.Db.Database.MigrateAsync();Assert.Equal(original,await f.Read(kind));var disk=kind=="second"?f.Second.Id:f.First.Id;await Assert.ThrowsAsync<SqliteException>(()=>f.Db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM Disks WHERE Id={disk}"));}
}
