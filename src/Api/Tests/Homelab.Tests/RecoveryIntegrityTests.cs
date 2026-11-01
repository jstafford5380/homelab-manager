using Homelab.Api;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
namespace Homelab.Tests;

public sealed class RecoveryIntegrityTests
{
    private sealed class Fixture : IAsyncDisposable {
        public SqliteConnection Connection {get;} = new("Data Source=:memory:");
        public HomelabDb Db {get;}
        public Fixture() {Connection.Open();Db=new(new DbContextOptionsBuilder<HomelabDb>().UseSqlite(Connection).Options);}
        public async Task Legacy() {var migrations=Db.Database.GetMigrations().ToArray();await Db.GetService<IMigrator>().MigrateAsync(migrations[Array.FindIndex(migrations,x=>x.EndsWith("_RecoveryEvidenceIntegrity"))-1]);}
        public Task InsertLegacyPolicy(RecoveryPolicy policy) => Db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO Recovery (Id, Asset, Destination, Mechanism, RpoHours, RestoreResult, Evidence, Scope, Revision) VALUES ({policy.Id}, {policy.Asset}, {policy.Destination}, {policy.Mechanism}, {policy.RpoHours}, {policy.RestoreResult}, {policy.Evidence}, {policy.Scope}, {policy.Revision})");
        public async ValueTask DisposeAsync() {await Db.DisposeAsync();await Connection.DisposeAsync();}
    }
    [Fact] public async Task ValidLegacyRecoveryEvidenceSurvivesUpgradeAndPolicyDeletionIsRestricted() {
        await using var f=new Fixture();await f.Legacy();var policy=new RecoveryPolicy {Asset="Important photos",Scope="photos"};
        var run=new BackupEvidence {PolicyId=policy.Id,PolicyRevision=policy.Revision,Evidence="Destination manifest",Author="owner"};
        var exercise=new RestoreExercise {PolicyId=policy.Id,PolicyRevision=policy.Revision,Evidence="Offline restore record",Scope="photos",Author="owner"};
        await f.InsertLegacyPolicy(policy);f.Db.BackupEvidence.Add(run);f.Db.RestoreExercises.Add(exercise);await f.Db.SaveChangesAsync();f.Db.ChangeTracker.Clear();
        await f.Db.Database.MigrateAsync();
        Assert.Equal("", (await f.Db.Recovery.SingleAsync()).DestinationCapacity);
        Assert.Equal("", (await f.Db.Recovery.SingleAsync()).RestoreRequirements);
        Assert.Equal(run.Evidence,(await f.Db.BackupEvidence.SingleAsync()).Evidence);Assert.Equal(exercise.PolicyRevision,(await f.Db.RestoreExercises.SingleAsync()).PolicyRevision);
        Assert.Equal(exercise.Evidence,(await f.Db.RestoreExercises.SingleAsync()).Evidence);
        await Assert.ThrowsAsync<SqliteException>(()=>f.Db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM Recovery WHERE Id={policy.Id}"));
        Assert.Single(await f.Db.Recovery.ToListAsync());Assert.Single(await f.Db.BackupEvidence.ToListAsync());Assert.Single(await f.Db.RestoreExercises.ToListAsync());
    }
    [Theory] [InlineData(true)] [InlineData(false)]
    public async Task ExistingOrphanBlocksMigrationWithoutDiscardingEvidence(bool backup) {
        await using var f=new Fixture();await f.Legacy();var missing=Guid.NewGuid();
        if(backup) f.Db.BackupEvidence.Add(new() {PolicyId=missing,Evidence="Retain orphan for review"});
        else f.Db.RestoreExercises.Add(new() {PolicyId=missing,Evidence="Retain orphan for review"});
        await f.Db.SaveChangesAsync();f.Db.ChangeTracker.Clear();
        var error=await Assert.ThrowsAsync<SqliteException>(()=>f.Db.Database.MigrateAsync());Assert.Contains("RecoveryEvidenceRequiresExistingPolicy",error.Message);
        Assert.DoesNotContain(await f.Db.Database.GetAppliedMigrationsAsync(),x=>x.EndsWith("_RecoveryEvidenceIntegrity"));
        if(backup)Assert.Equal("Retain orphan for review",(await f.Db.BackupEvidence.SingleAsync()).Evidence);
        else Assert.Equal("Retain orphan for review",(await f.Db.RestoreExercises.SingleAsync()).Evidence);
        Assert.Equal(0, await f.Db.Recovery.CountAsync());
        // A deliberately restored original parent permits retry; the migration never guesses one.
        await f.InsertLegacyPolicy(new() {Id=missing,Asset="Restored original policy"});await f.Db.Database.MigrateAsync();
        Assert.Contains(await f.Db.Database.GetAppliedMigrationsAsync(),x=>x.EndsWith("_RecoveryEvidenceIntegrity"));
    }
    [Theory] [InlineData(true)] [InlineData(false)]
    public async Task NewOrphanIsRejectedByDatabaseEvenWithoutApiValidation(bool backup) {
        await using var f=new Fixture();await f.Db.Database.MigrateAsync();
        if(backup)f.Db.BackupEvidence.Add(new() {PolicyId=Guid.NewGuid(),Evidence="Cannot attach to absent policy"});
        else f.Db.RestoreExercises.Add(new() {PolicyId=Guid.NewGuid(),Evidence="Cannot attach to absent policy"});
        await Assert.ThrowsAsync<DbUpdateException>(()=>f.Db.SaveChangesAsync());f.Db.ChangeTracker.Clear();
        Assert.Empty(await f.Db.BackupEvidence.ToListAsync());Assert.Empty(await f.Db.RestoreExercises.ToListAsync());
    }
}
