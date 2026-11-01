using Homelab.Api;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Homelab.Tests;
public sealed class ReplacementTests
{
    private static readonly DateTime Now = new(2026, 10, 31, 12, 0, 0, DateTimeKind.Utc);
    private static (Disk Old, Disk New, ReplacementInput Input) Fixture()
    {
        var oldDisk = new Disk { Serial = "OLD", ConnectionId = Guid.NewGuid(), Present = false };
        var newDisk = new Disk { Serial = "NEW", ConnectionId = oldDisk.ConnectionId };
        return (oldDisk, newDisk, new(oldDisk.Id, newDisk.Id, Now.AddDays(-1), "documented", "pool-guid", "Maintenance log identifies data vdev member", "Media issue", "Private operator log", "OLD", "NEW"));
    }
    [Fact] public void RecordingReplacementDoesNotRetireDrivesTransferObservationsOrAssertHealth()
    {
        var (oldDisk, newDisk, input) = Fixture();
        var record = Replacements.Create(oldDisk, newDisk, input, "owner", Now);
        Assert.Equal(oldDisk.Id, record.OldDiskId); Assert.Equal(newDisk.Id, record.NewDiskId);
        Assert.Equal("installed", oldDisk.Lifecycle); Assert.False(oldDisk.Present); Assert.True(newDisk.Present);
        Assert.Equal("OLD", record.OldIdentity); Assert.Equal("NEW", record.NewIdentity);
        Assert.Equal("owner", record.Author); Assert.Equal(Now, record.RecordedUtc);
    }
    [Fact] public void SameDiskAndDifferentNasAreRejected()
    {
        var (oldDisk, newDisk, input) = Fixture();
        Assert.NotNull(Replacements.Validate(oldDisk, oldDisk, input with { NewDiskId = oldDisk.Id }, Now));
        newDisk.ConnectionId = Guid.NewGuid(); Assert.NotNull(Replacements.Validate(oldDisk, newDisk, input, Now));
    }
    [Fact] public void AmbiguousMissingAndUnconfirmedIdentitiesAreRejected()
    {
        var (oldDisk, newDisk, input) = Fixture();
        Assert.NotNull(Replacements.Validate(oldDisk, newDisk, input with { ConfirmedNewIdentity = "other" }, Now));
        newDisk.MappingConfidence = "ambiguous"; Assert.NotNull(Replacements.Validate(oldDisk, newDisk, input, Now));
        newDisk.MappingConfidence = "inferred"; newDisk.Serial = "";
        Assert.NotNull(Replacements.Validate(oldDisk, newDisk, input with { ConfirmedNewIdentity = "" }, Now));
        newDisk.Wwn = "5000c50012345678";
        Assert.Null(Replacements.Validate(oldDisk, newDisk, input with { ConfirmedNewIdentity = newDisk.Wwn }, Now));
    }
    [Fact] public void FutureTimeMissingOffsetOrMissingEvidenceAreRejected()
    {
        var (oldDisk, newDisk, input) = Fixture();
        Assert.NotNull(Replacements.Validate(oldDisk, newDisk, input with { OccurredUtc = Now.AddDays(1) }, Now));
        Assert.NotNull(Replacements.Validate(oldDisk, newDisk, input with { OccurredUtc = DateTime.SpecifyKind(Now.AddDays(-1), DateTimeKind.Unspecified) }, Now));
        Assert.NotNull(Replacements.Validate(oldDisk, newDisk, input with { Evidence = null! }, Now));
        Assert.NotNull(Replacements.Validate(oldDisk, newDisk, input with { MembershipEvidence = "" }, Now));
        Assert.Null(Replacements.Validate(oldDisk, newDisk, input with { PoolGuid = "", MembershipEvidence = "Unknown: original maintenance notes did not identify the vdev", DateConfidence = "estimated" }, Now));
    }
    [Fact] public async Task HistorySurvivesRestartKeepsBothDrivesAndRedactsPrivateEvidence()
    {
        using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<HomelabDb>().UseSqlite(connection).Options;
        var (oldDisk, newDisk, input) = Fixture();
        await using (var db = new HomelabDb(options)) {
            await db.Database.MigrateAsync();
            db.Connections.Add(new(){Id=oldDisk.ConnectionId,Enabled=false});db.Disks.AddRange(oldDisk, newDisk);
            db.Observations.Add(new() { DiskId = oldDisk.Id, ConnectionId=oldDisk.ConnectionId, SourceKey = "old-evidence", Metric = "ata.pending_sectors", Value = 2 });
            db.Findings.Add(new() { DiskId = oldDisk.Id, ConnectionId=oldDisk.ConnectionId, State = "open", Title = "Old fault" });
            db.Replacements.Add(Replacements.Create(oldDisk, newDisk, input, "private-owner", Now));
            await db.SaveChangesAsync();
        }
        await using var reopened = new HomelabDb(options);
        var saved = await reopened.Replacements.SingleAsync();
        Assert.Equal(oldDisk.Id, (await reopened.Observations.SingleAsync()).DiskId);
        Assert.Equal("open", (await reopened.Findings.SingleAsync()).State);
        Assert.Equal(2, await reopened.Disks.CountAsync());
        saved.VoidedUtc = Now; saved.VoidReason = "Incorrect membership"; saved.VoidedBy = "private-owner"; await reopened.SaveChangesAsync();
        var exported = System.Text.Encoding.UTF8.GetString(await InventoryExport.CreateAsync(reopened, false));
        Assert.Contains("Private operator log", exported); Assert.Contains("Incorrect membership", exported);
        var redacted = System.Text.Encoding.UTF8.GetString(await InventoryExport.CreateAsync(reopened, true));
        Assert.DoesNotContain("Private operator log", redacted); Assert.DoesNotContain("private-owner", redacted); Assert.DoesNotContain("Incorrect membership", redacted);
        reopened.ChangeTracker.Clear();
        reopened.Disks.Remove(await reopened.Disks.SingleAsync(d => d.Id == oldDisk.Id));
        await Assert.ThrowsAsync<DbUpdateException>(() => reopened.SaveChangesAsync());
    }
}
