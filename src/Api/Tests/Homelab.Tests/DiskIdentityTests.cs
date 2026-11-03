using Homelab.Api;
using Microsoft.EntityFrameworkCore;
using Microsoft.Data.Sqlite;
namespace Homelab.Tests;
public class DiskIdentityTests
{
    [Fact] public async Task PathReorderPreservesPhysicalIdentityAndClosesOldAliases()
    {
        using var connection = new SqliteConnection("Data Source=:memory:"); connection.Open();
        await using var db = new HomelabDb(new DbContextOptionsBuilder<HomelabDb>().UseSqlite(connection).Options); db.Database.EnsureCreated();
        var nas = Guid.NewGuid(); db.Connections.Add(new(){Id=nas,Enabled=false}); var at = DateTime.UtcNow;
        await DiskIdentity.ReconcileAsync(db, nas, [new("A", "model", null, "sda"), new("B", "model", null, "sdb")], at, default); await db.SaveChangesAsync();
        var a = (await db.Disks.SingleAsync(d => d.Serial == "A")).Id;
        await DiskIdentity.ReconcileAsync(db, nas, [new("A", "model", null, "sdb"), new("B", "model", null, "sda")], at.AddMinutes(1), default); await db.SaveChangesAsync();
        Assert.Equal(2, await db.Disks.CountAsync()); Assert.Equal(a, (await db.Disks.SingleAsync(d => d.Serial == "A")).Id);
        Assert.Equal("sdb", (await db.Disks.FindAsync(a))!.Alias);
        Assert.NotNull((await db.DiskAliases.SingleAsync(x => x.DiskId == a && x.Path == "sda")).EndedUtc);
    }
    [Fact] public async Task MultipathWwnIsOneDriveButConflictingSerialCreatesReview()
    {
        using var connection = new SqliteConnection("Data Source=:memory:"); connection.Open();
        await using var db = new HomelabDb(new DbContextOptionsBuilder<HomelabDb>().UseSqlite(connection).Options); db.Database.EnsureCreated();
        var nas = Guid.NewGuid(); db.Connections.Add(new(){Id=nas,Enabled=false}); var at = DateTime.UtcNow;
        await DiskIdentity.ReconcileAsync(db, nas, [new("A", "model", "5000c50012345678", "sda"), new("A", "model", "5000c50012345678", "sdb")], at, default); await db.SaveChangesAsync();
        Assert.Single(await db.Disks.ToListAsync()); Assert.Equal(2, await db.DiskAliases.CountAsync());
        await DiskIdentity.ReconcileAsync(db, nas, [new("A", "model", "5000c50087654321", "sdc")], at.AddMinutes(1), default); await db.SaveChangesAsync();
        Assert.Single(await db.Disks.ToListAsync()); Assert.Single(await db.Findings.ToListAsync());
        Assert.Equal("ambiguous", (await db.Disks.SingleAsync()).MappingConfidence);
    }
    [Fact] public async Task DuplicateSerialWithoutWwnDoesNotCreateMergedDrive()
    {
        using var connection = new SqliteConnection("Data Source=:memory:"); connection.Open();
        await using var db = new HomelabDb(new DbContextOptionsBuilder<HomelabDb>().UseSqlite(connection).Options); db.Database.EnsureCreated();
        var nas=Guid.NewGuid();db.Connections.Add(new(){Id=nas,Enabled=false});
        await DiskIdentity.ReconcileAsync(db, nas, [new("A", "model", null, "sda"), new("A", "model", null, "sdb")], DateTime.UtcNow, default); await db.SaveChangesAsync();
        Assert.Empty(await db.Disks.ToListAsync()); Assert.Single(await db.Findings.ToListAsync());
    }
    [Fact] public void BayCannotBeVerifiedWithAmbiguousIdentityOrWrongSerial()
    {
        var disk = new Disk { Serial = "A" };
        var input = new DiskMetadata("Bay 1", true, "B", "Physical label checked", null, "unknown", null, "", "installed");
        Assert.NotNull(DiskMetadataRules.ValidateMetadata(disk, input));
        Assert.Null(DiskMetadataRules.ValidateMetadata(disk, input with { ConfirmedSerial = "A" }));
        disk.MappingConfidence = "ambiguous";
        Assert.NotNull(DiskMetadataRules.ValidateMetadata(disk, input with { ConfirmedSerial = "A" }));
    }
    [Theory] [InlineData(null)] [InlineData("")] [InlineData("0000000000000000")] [InlineData("not-a-wwn")]
    public void InvalidWwnCannotBecomeDurableKey(string? value) => Assert.Null(IdentityCandidate.NormalizeWwn(value));
}
