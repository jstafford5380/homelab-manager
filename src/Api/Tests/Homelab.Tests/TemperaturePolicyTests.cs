using Homelab.Api;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
namespace Homelab.Tests;

public sealed class TemperaturePolicyTests
{
    private static OperatingSettings Policy() => new() { TemperatureReviewCelsius = 45, TemperaturePolicyReason = "Fixture local policy, not a model specification" };
    private static Observation Reading(double value) => new() { Metric = "temperature", Value = value, Unit = "°C", Quality = "observed",
        ObservedUtc = DateTime.UtcNow.AddMinutes(-1), Source = "fixture", SourceSchema = "temperature-v1", ResetEpoch = "", SourceKey = Guid.NewGuid().ToString() };
    [Theory] [InlineData(44, false)] [InlineData(45, false)] [InlineData(45.1, true)]
    public void OnlyAboveExplicitLimitCreatesReview(double value, bool expected)
    {
        var rule = TemperaturePolicy.Evaluate(Reading(value), Policy());
        Assert.Equal(expected, rule is not null);
        if (rule is not null) {
            Assert.Equal("review", rule.Severity); Assert.Contains("45 °C", rule.Evidence);
            Assert.Contains("Fixture local policy", rule.Evidence); Assert.Contains("not a universal safe limit", rule.NextStep);
        }
        Assert.Null(TemperaturePolicy.Evaluate(Reading(value), new OperatingSettings()));
    }
    [Theory] [InlineData(-274)] [InlineData(1001)] [InlineData(double.NaN)] [InlineData(double.PositiveInfinity)]
    public void InvalidLimitsCannotEnablePolicy(double limit)
    {
        var settings = Policy(); settings.TemperatureReviewCelsius = limit;
        Assert.NotNull(TemperaturePolicy.Validate(settings)); Assert.Null(TemperaturePolicy.Evaluate(Reading(100), settings));
    }
    [Fact] public void BasisIsRequiredAndBounded()
    {
        var settings = Policy(); settings.TemperaturePolicyReason = " ";
        Assert.NotNull(TemperaturePolicy.Validate(settings));
        settings.TemperaturePolicyReason = new string('x',2001); Assert.NotNull(TemperaturePolicy.Validate(settings));
        settings.TemperaturePolicyReason = null!; Assert.NotNull(TemperaturePolicy.Validate(settings));
        Assert.Null(TemperaturePolicy.Validate(new OperatingSettings()));
    }
    [Theory] [InlineData("unknown")] [InlineData("unverified")]
    public void UnverifiedOrUndatedEvidenceCannotTriggerPolicy(string quality)
    {
        var row = Reading(60); row.Quality = quality; Assert.Null(TemperaturePolicy.Evaluate(row, Policy()));
        row.Quality = "observed"; row.ObservedUtc = null; Assert.Null(TemperaturePolicy.Evaluate(row, Policy()));
        row = Reading(60); row.Unit = "F"; Assert.Null(TemperaturePolicy.Evaluate(row, Policy()));
    }
    [Fact] public async Task PolicyAndEvidencePersistWhileCoolingOrDisablingCannotEraseReview()
    {
        using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<HomelabDb>().UseSqlite(connection).Options;
        await using var db = new HomelabDb(options); await db.Database.MigrateAsync();
        var nas=new NasConnection{Enabled=false};db.Connections.Add(nas);var disk = new Disk{ConnectionId=nas.Id}; db.Disks.Add(disk); var policy = Policy(); db.Settings.Add(policy); await db.SaveChangesAsync();
        var hot = Reading(50); hot.ObservedUtc = DateTime.UtcNow.AddMinutes(-30);
        await ObservationIngestion.StageAsync(db,disk,hot,"owner"); await db.SaveChangesAsync();
        var finding = await db.Findings.SingleAsync(); Assert.Equal("local-temperature-policy-v1",finding.Rule);
        for (var i=2;i>0;i--) {
            var cool=Reading(40); cool.ObservedUtc=DateTime.UtcNow.AddMinutes(-i*5);
            await ObservationIngestion.StageAsync(db,disk,cool,"owner"); await db.SaveChangesAsync();
        }
        Assert.Equal("open",finding.State); var evidence=finding.Evidence;
        policy.TemperatureReviewCelsius=null; await db.SaveChangesAsync();
        await ObservationIngestion.StageAsync(db,disk,Reading(70),"owner"); await db.SaveChangesAsync();
        Assert.Equal(evidence,finding.Evidence); Assert.Equal("open",finding.State);
        await using var reopened = new HomelabDb(options);
        Assert.Null((await reopened.Settings.SingleAsync()).TemperatureReviewCelsius);
        Assert.Contains("Fixture local policy",(await reopened.Findings.SingleAsync()).Evidence);
    }
}
