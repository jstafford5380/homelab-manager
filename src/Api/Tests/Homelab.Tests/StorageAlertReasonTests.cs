using Homelab.Api;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Homelab.Tests;

public class StorageAlertReasonTests
{
    [Fact]
    public async Task WatchExplainsExactActiveAlertsWithoutInventingSmartCounters()
    {
        using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        await using var db = new HomelabDb(new DbContextOptionsBuilder<HomelabDb>().UseSqlite(connection).Options); await db.Database.MigrateAsync();
        var now = DateTime.UtcNow;
        var nas = new NasConnection { LastSuccessUtc = now };
        var disk = new Disk { ConnectionId = nas.Id, Alias = "sdk", Serial = "WD-SERIAL", Model = "WD" };
        db.Connections.Add(nas); db.Disks.Add(disk);
        foreach (var name in new[] { "inventory", "alerts" }) db.Capabilities.Add(new() { ConnectionId = nas.Id, Name = name, Status = "supported", LastSuccessUtc = now });
        db.NativeAlerts.AddRange(new() { ConnectionId = nas.Id, SourceId = "pending", Class = "SMART", Message = "Device: /dev/sdk [SAT], 5 Currently unreadable (pending) sectors.", LastSeenUtc = now },
            new() { ConnectionId = nas.Id, SourceId = "offline", Class = "SMART", Message = "Device: /dev/sdk [SAT], 5 Offline uncorrectable sectors.", LastSeenUtc = now },
            new() { ConnectionId = nas.Id, SourceId = "other", Class = "SMART", Message = "Device: /dev/sdf [SAT], 999 pending sectors.", LastSeenUtc = now },
            new() { ConnectionId = nas.Id, SourceId = "old", Class = "SMART", Message = "Device: /dev/sdk [SAT], 888 pending sectors.", AbsentSinceUtc = now },
            new() { ConnectionId = nas.Id, SourceId = "dismissed", Class = "SMART", Message = "Device: /dev/sdk [SAT], 777 pending sectors.", DismissedOnNas = true });
        await db.SaveChangesAsync();
        var result = Assert.Single(Assert.Single((await StoragePage.ReadAsync(db, now)).Servers).LooseDrives);
        Assert.Equal("Watch", result.Status.Label);
        Assert.Contains("5 Currently unreadable (pending) sectors", result.Status.Why);
        Assert.Contains("5 Offline uncorrectable sectors", result.Status.Why);
        Assert.DoesNotContain("999", result.Status.Why); Assert.DoesNotContain("888", result.Status.Why); Assert.DoesNotContain("777", result.Status.Why);
        Assert.Null(result.Smart);
        Assert.Empty(await db.Observations.ToArrayAsync());
        (await db.Capabilities.SingleAsync(x => x.Name == "alerts")).Status = "denied";
        await db.SaveChangesAsync();
        result = Assert.Single(Assert.Single((await StoragePage.ReadAsync(db, now)).Servers).LooseDrives);
        Assert.StartsWith("Last known TrueNAS alerts:", result.Status.Why);
    }

    [Fact]
    public void AmbiguousOrStaleAliasesAndOtherNasAlertsAreNotAssignedToADrive()
    {
        var disk = new Disk { Alias = "sdk", Serial = "SERIAL-A", ConnectionId = Guid.NewGuid() };
        var alert = new NativeAlert { ConnectionId = disk.ConnectionId, Class = "SMART", Message = "Device: /dev/sdk [SAT], 5 pending sectors." };
        Assert.Null(StorageAlertReasons.ForDrive(disk, [disk], [alert], false));
        Assert.Null(StorageAlertReasons.ForDrive(disk, [disk, new() { Alias = "sdk" }], [alert], true));
        disk.MappingConfidence = "ambiguous";
        Assert.Null(StorageAlertReasons.ForDrive(disk, [disk], [alert], true));
        disk.MappingConfidence = "inferred";
        alert.ConnectionId = Guid.NewGuid();
        Assert.Null(StorageAlertReasons.ForDrive(disk, [disk], [alert], true));
        alert.ConnectionId = disk.ConnectionId;
        alert.Message = "Device: /dev/sdk1 [SAT], 5 pending sectors.";
        Assert.Null(StorageAlertReasons.ForDrive(disk, [disk], [alert], true));
        alert.Message = "Device: SERIAL-A-OTHER, 5 pending sectors.";
        Assert.Null(StorageAlertReasons.ForDrive(disk, [disk], [alert], true));
        alert.Message = "Device: SERIAL-A, <b>5 pending sectors</b> &amp; an alert.";
        var reason = StorageAlertReasons.ForDrive(disk, [disk], [alert], false);
        Assert.Contains("5 pending sectors & an alert.", reason);
        Assert.DoesNotContain("<b>", reason);
        Assert.Equal("Act now", StorageStatusRules.Drive(false, true, "FAULTED", 0, 0, 0, [], true, true, alertReason: reason).Label);
    }
}
