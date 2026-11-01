using Homelab.Api;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
namespace Homelab.Tests;
public class SmartReportHistoryTests
{
    [Fact]
    public async Task PagingPreservesTiedImportsAndExcludesOtherDrivesAndNewImports()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new HomelabDb(new DbContextOptionsBuilder<HomelabDb>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        var nas = new NasConnection(); db.Connections.Add(nas);
        var disk = new Disk { ConnectionId = nas.Id, IdentityKey = "one" };
        var other = new Disk { ConnectionId = nas.Id, IdentityKey = "two" };
        db.Disks.AddRange(disk, other); await db.SaveChangesAsync();
        var asOf = DateTime.UtcNow;
        Assert.Empty((await SmartReports.ReadHistoryAsync(db, disk.Id, asOf)).Items);
        for (var i = 0; i < 103; i++) db.SmartReports.Add(new() { DiskId = disk.Id, ReportHash = "hash" + i, Actor = "owner", ImportedUtc = asOf.AddDays(-1), CuratedJson = "{}" });
        db.SmartReports.Add(new() { DiskId = other.Id, ReportHash = "other", ImportedUtc = asOf.AddDays(-1), CuratedJson = "{}" });
        await db.SaveChangesAsync();
        var expected = await db.SmartReports.Where(x => x.DiskId == disk.Id).OrderByDescending(x => x.ImportedUtc).ThenBy(x => x.Id).Select(x => x.Id).ToArrayAsync();
        var first = await SmartReports.ReadHistoryAsync(db, disk.Id, asOf);
        Assert.Equal(50, first.Items.Length); Assert.Equal(50, first.NextOffset);
        db.SmartReports.Add(new() { DiskId = disk.Id, ReportHash = "new", ImportedUtc = asOf.AddSeconds(1), CuratedJson = "{}" }); await db.SaveChangesAsync();
        var second = await SmartReports.ReadHistoryAsync(db, disk.Id, first.AsOf, first.NextOffset!.Value);
        var third = await SmartReports.ReadHistoryAsync(db, disk.Id, first.AsOf, second.NextOffset!.Value);
        Assert.Equal(3, third.Items.Length); Assert.Null(third.NextOffset);
        Assert.Equal(expected, first.Items.Concat(second.Items).Concat(third.Items).Select(x => x.Id));
        Assert.All(first.Items, x => Assert.Equal("owner", x.Actor));
    }
}
