using Homelab.Api;
namespace Homelab.Tests;
public class HealthRulesTests
{
    private static Observation Sample(string metric, double? value, int minute = 0) => new() { Metric = metric, Value = value, Quality = "observed", ResetEpoch = "fixture-boot-1", ObservedUtc = new DateTime(2026, 1, 1, 0, minute, 0, DateTimeKind.Utc) };
    [Fact] public void MissingValuesDoNotInventHealthyEvidence() => Assert.Null(HealthRules.Evaluate(Sample("ata.pending_sectors", null), null));
    [Fact] public void PendingSectorsWarrantReview() => Assert.Equal("urgent", HealthRules.Evaluate(Sample("ata.pending_sectors", 8), null)?.Severity);
    [Fact] public void CounterResetDoesNotBecomeAnIncrease() => Assert.Null(HealthRules.Evaluate(Sample("ata.reallocated_sectors", 0, 1), Sample("ata.reallocated_sectors", 8)));
    [Fact] public void StableCounterDoesNotOpenRepeatedFindings() => Assert.Null(HealthRules.Evaluate(Sample("ata.reallocated_sectors", 8, 1), Sample("ata.reallocated_sectors", 8)));
    [Fact] public void OlderEvidenceCannotCreateTrend() => Assert.Null(HealthRules.Evaluate(Sample("ata.reallocated_sectors", 12), Sample("ata.reallocated_sectors", 8, 1)));
    [Fact] public void TransportErrorsDoNotDiagnoseMediaFailure() => Assert.Contains("does not establish failed media", HealthRules.Evaluate(Sample("ata.crc_errors", 10, 1), Sample("ata.crc_errors", 1))!.NextStep);
    [Theory] [InlineData("pool.scrub")] [InlineData("disk.wipe")] [InlineData("pool.replace")] [InlineData("system.reboot")] [InlineData("disk.offline")] [InlineData("pool.clear")] [InlineData("smart.test.manual")] [InlineData("system.general.update")] [InlineData("filesystem.setperm")] [InlineData("auth.login_with_api_key")]
    public async Task MutationMethodsAreRejectedBeforeNetwork(string method) { await using var client = new TrueNasClient(); await Assert.ThrowsAsync<InvalidOperationException>(() => client.ReadAsync(method, CancellationToken.None)); }
}
