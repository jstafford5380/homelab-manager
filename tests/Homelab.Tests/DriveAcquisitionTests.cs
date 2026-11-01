using Homelab.Api;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
namespace Homelab.Tests;
public sealed class DriveAcquisitionTests
{
    private static readonly DateTime Now = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private static DriveAcquisitionInput Empty => new(null, null, "unknown", "", null, "", "unknown", "");
    [Fact] public void UnknownIsDistinctFromFreeAndBothNeedConsistentConfidence()
    {
        Assert.Null(DriveAcquisitions.Validate(Empty, Now));
        Assert.Null(DriveAcquisitions.Validate(Empty with { PurchaseCost=0, Currency="USD", CostConfidence="documented", PurchaseEvidence="Gift receipt" }, Now));
        Assert.Null(DriveAcquisitions.Validate(Empty with { InstalledUtc=Now.AddYears(-1), InstallationConfidence="estimated", InstallationEvidence="Owner recollection" }, Now));
    }
    [Theory] [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)] [InlineData(5)] [InlineData(6)] [InlineData(7)] [InlineData(8)] [InlineData(9)] [InlineData(10)] [InlineData(11)]
    public void RejectsContradictoryOrMalformedManualEvidence(int scenario)
    {
        var input = scenario switch {
            0 => Empty with { InstalledUtc=Now.AddDays(1) },
            1 => Empty with { InstalledUtc=DateTime.SpecifyKind(Now,DateTimeKind.Unspecified) },
            2 => Empty with { InstalledUtc=Now, InstallationConfidence="documented" },
            3 => Empty with { InstallationConfidence="estimated" },
            4 => Empty with { PurchaseCost=-1 },
            5 => Empty with { PurchaseCost=1_000_000_001m },
            6 => Empty with { PurchaseCost=10, Currency="USD", CostConfidence="documented" },
            7 => Empty with { Currency="USD" },
            8 => Empty with { CostConfidence="documented" },
            9 => Empty with { PurchaseEvidence=null! },
            10 => Empty with { InstallationEvidence=new string('x',4001) },
            _ => Empty with { PurchaseCost=10, Currency="US1", CostConfidence="documented", PurchaseEvidence="Receipt" }
        };
        Assert.NotNull(DriveAcquisitions.Validate(input, Now));
    }
    [Fact] public async Task ScopedNotesPreserveCostPrecisionAndRedactEvidenceWithoutBecomingHealthEvidence()
    {
        using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        await using var db = new HomelabDb(new DbContextOptionsBuilder<HomelabDb>().UseSqlite(connection).Options);
        await db.Database.MigrateAsync();
        var nas = new NasConnection { Enabled=false }; var disk = new Disk { ConnectionId=nas.Id };
        db.Connections.Add(nas); db.Disks.Add(disk);
        db.DriveAcquisitions.Add(new() { DiskId=disk.Id, PurchaseCost=123.456m, Currency="USD", CostConfidence="documented", PurchaseEvidence="private-receipt", InstallationEvidence="private-installation", Author="private-author", RecordedUtc=Now });
        await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        Assert.Equal(123.456m,(await db.DriveAcquisitions.SingleAsync()).PurchaseCost);
        Assert.Empty(await db.Observations.ToListAsync()); Assert.Empty(await db.Findings.ToListAsync());
        foreach(var redacted in new[]{false,true}) {
            using var export=JsonDocument.Parse(await InventoryExport.CreateAsync(db,redacted));
            var row=export.RootElement.GetProperty("driveAcquisitions")[0];
            Assert.Equal(123.456m,row.GetProperty("purchaseCost").GetDecimal());
            Assert.Equal(JsonValueKind.Null,row.GetProperty("installedUtc").ValueKind);
            foreach(var field in new[]{"purchaseEvidence","installationEvidence","author"})
                Assert.Equal(redacted?JsonValueKind.Null:JsonValueKind.String,row.GetProperty(field).ValueKind);
        }
        await Assert.ThrowsAsync<SqliteException>(()=>db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM Disks WHERE Id={disk.Id}"));
        db.DriveAcquisitions.Add(new(){DiskId=Guid.NewGuid()});
        await Assert.ThrowsAsync<DbUpdateException>(()=>db.SaveChangesAsync());
    }
}
