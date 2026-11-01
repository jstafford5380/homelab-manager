using Homelab.Api;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
namespace Homelab.Tests;

public sealed class PlanningTests
{
    [Fact] public async Task ExistingSettingsReceiveUsablePlanningDefaultsOnUpgrade()
    {
        using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        await using var db = new HomelabDb(new DbContextOptionsBuilder<HomelabDb>().UseSqlite(connection).Options);
        var previous = db.Database.GetMigrations().Single(x => x.EndsWith("_PoolHistory"));
        await db.GetService<IMigrator>().MigrateAsync(previous);
        await db.Database.ExecuteSqlRawAsync("INSERT INTO Settings (Id, Timezone, CapacityUnits, InventoryMinutes, SmartMinutes, SampleRetentionDays, RawRetentionDays, BackupsEnabled, BackupDailyCopies, BackupWeeklyCopies) VALUES (1, 'UTC', 'binary', 15, 90, 90, 14, 0, 7, 4)");
        await db.Database.MigrateAsync();
        var settings = await db.Settings.SingleAsync();
        Assert.Equal(5, settings.DriveAgeReviewYears); Assert.Equal(90, settings.WarrantyNoticeDays);
        Assert.Equal("UTC", settings.Timezone);
    }
    private static readonly DateTime Now = new(2026, 10, 31, 12, 0, 0, DateTimeKind.Utc);
    private static DriveReview? Review(Disk disk, Finding[]? findings = null, PlanningItem[]? plans = null, OperatingSettings? settings = null) => Planning.Review(disk, findings ?? [], plans ?? [], settings ?? new(), Now);

    [Fact] public void OldAgeProducesPlanningContextNotMediaDiagnosis()
    {
        var result = Review(new Disk { PurchasedUtc = Now.AddYears(-6), PurchaseDateConfidence = "estimated" })!;
        Assert.Equal("Prepare ahead", result.Priority);
        Assert.Contains(result.Reasons, x => x.Kind == "age-review" && x.Detail.Contains("estimated") && x.Detail.Contains("Age alone is not evidence"));
        Assert.Contains("not a failure alert", result.NextStep);
    }
    [Fact] public void DiscoveryDateCannotSubstituteForUnknownPurchaseDate()
    {
        var result = Review(new Disk { FirstSeenUtc = Now.AddYears(-8) })!;
        Assert.Contains(result.Reasons, x => x.Kind == "age-unknown");
        Assert.DoesNotContain(result.Reasons, x => x.Kind == "age-review");
    }
    [Fact] public void ConfiguredAgeAndWarrantyWindowsAreRespected()
    {
        var disk = new Disk { PurchasedUtc = Now.AddYears(-4), PurchaseDateConfidence = "documented", WarrantyEndUtc = Now.AddDays(31) };
        Assert.Null(Review(disk, settings: new() { DriveAgeReviewYears = 5, WarrantyNoticeDays = 30 }));
        var result = Review(disk, settings: new() { DriveAgeReviewYears = 3, WarrantyNoticeDays = 90 })!;
        Assert.Contains(result.Reasons, x => x.Kind == "age-review"); Assert.Contains(result.Reasons, x => x.Kind == "warranty");
    }
    [Fact] public void ExpiredWarrantyDoesNotClaimFailure()
    {
        var result = Review(new Disk { WarrantyEndUtc = Now.AddDays(-1) })!;
        Assert.Equal("Prepare ahead", result.Priority);
        Assert.Contains(result.Reasons, x => x.Kind == "warranty" && x.Detail.Contains("ended"));
    }
    [Fact] public void AcknowledgedMediaFindingStillInformsPlanningButResolvedFindingDoesNot()
    {
        var disk = new Disk();
        var finding = new Finding { DiskId = disk.Id, Rule = "media-increase-v1:nvme.media_errors", State = "acknowledged", Evidence = "Counter increased from 1 to 3" };
        var result = Review(disk, [finding])!;
        Assert.Equal("Review media evidence", result.Priority);
        Assert.Contains(result.Reasons, x => x.FindingId == finding.Id && x.Detail.Contains(finding.Evidence));
        finding.State = "resolved";
        Assert.Equal("Prepare ahead", Review(disk, [finding])!.Priority);
    }
    [Fact] public void TransportErrorsDoNotCreateMediaReplacementAdvice()
    {
        var disk = new Disk();
        var result = Review(disk, [new() { DiskId = disk.Id, Rule = "transport-increase-v1" }])!;
        Assert.Equal("Review observed issue", result.Priority);
        Assert.Contains("transport error alone does not establish failed media", result.NextStep);
    }
    [Fact] public void IdentityGapsAndAbsentDriveStayExplicitAndRetiredDrivesAreExcluded()
    {
        var disk = new Disk { Present = false, MappingConfidence = "ambiguous" };
        Assert.Equal(2, Review(disk)!.Reasons.Count(x => x.Kind == "identity"));
        disk.Lifecycle = "retired"; Assert.Null(Review(disk));
    }
    [Fact] public void OpenPlansAreLinkedToOnlyTheirOwnDisk()
    {
        var disk = new Disk(); var open = new PlanningItem { DiskId = disk.Id, Status = "ordered" };
        var result = Review(disk, plans: [open, new() { DiskId = disk.Id, Status = "complete" }, new() { DiskId = Guid.NewGuid() }])!;
        Assert.Equal([open.Id], result.OpenPlanIds);
    }
    [Theory] [InlineData(null)] [InlineData("US")] [InlineData("usd")] [InlineData("123")]
    public void InvalidCurrencyIsRejectedBeforeItCanBreakUi(string? currency)
    {
        Assert.NotNull(Planning.ValidatePlan(new() { Title = "Drive review", Currency = currency! }));
    }
}
