using Homelab.Api;
namespace Homelab.Tests;
public class FindingLifecycleTests
{
    private readonly DateTime now = DateTime.UtcNow;
    private Finding Finding() => new() { Rule = "media-present-v1:ata.pending_sectors", EvidenceUnit = "sectors", EvidenceObservedUtc = now.AddMinutes(-30), EvidenceSource = "fixture", EvidenceEpoch = "boot-1", EvidenceSchema = "ata-vendor-x" };
    private Observation Sample(int minutes) => new() { Metric = "ata.pending_sectors", Unit = "sectors", Value = 0, Quality = "observed", ObservedUtc = now.AddMinutes(minutes), Source = "fixture", ResetEpoch = "boot-1", SourceSchema = "ata-vendor-x" };
    [Fact] public void DifferentUnitsCannotResolveFinding() { var current = Sample(-1); current.Unit = "errors"; Assert.False(FindingLifecycle.CanResolvePresence(Finding(), current, Sample(-2), now, TimeSpan.FromHours(1))); }
    [Fact] public void LegacyUnknownEvidenceUnitCannotResolveAutomatically() { var finding = Finding(); finding.EvidenceUnit = null; Assert.False(FindingLifecycle.CanResolvePresence(finding, Sample(-1), Sample(-2), now, TimeSpan.FromHours(1))); }
    [Fact] public void DifferentUnitsCannotCreateAnIncreaseFinding() { var current = Sample(-1); current.Metric = "ata.reallocated_sectors"; current.Value = 10; current.Unit = "errors"; var previous = Sample(-2); previous.Metric = current.Metric; Assert.Null(HealthRules.Evaluate(current, previous)); }
    [Fact] public void TwoLaterZerosWithinSameEvidenceEpochCanResolve() => Assert.True(FindingLifecycle.CanResolvePresence(Finding(), Sample(-1), Sample(-2), now, TimeSpan.FromHours(1)));
    [Fact] public void ResetCannotResolveEarlierMediaFinding() { var current = Sample(-1); current.ResetEpoch = "boot-2"; Assert.False(FindingLifecycle.CanResolvePresence(Finding(), current, Sample(-2), now, TimeSpan.FromHours(1))); }
    [Fact] public void MissingEvidenceCannotResolveFinding() { var current = Sample(-1); current.Value = null; Assert.False(FindingLifecycle.CanResolvePresence(Finding(), current, Sample(-2), now, TimeSpan.FromHours(1))); }
    [Fact] public void CachedOldEvidenceCannotResolveCurrentFinding() => Assert.False(FindingLifecycle.CanResolvePresence(Finding(), Sample(-1), Sample(-2), now.AddDays(1), TimeSpan.FromHours(1)));
    [Fact] public void AcknowledgmentExpiryReopensReviewWithoutChangingSeverityOrEvidence() { var finding = Finding(); finding.State = "acknowledged"; finding.ReviewUtc = now.AddSeconds(-1); finding.Severity = "urgent"; finding.Evidence = "8 sectors"; Assert.True(FindingLifecycle.ExpireControls(finding, now)); Assert.Equal("open", finding.State); Assert.Equal("urgent", finding.Severity); Assert.Equal("8 sectors", finding.Evidence); Assert.Equal(1, finding.ReminderSequence); Assert.False(FindingLifecycle.ExpireControls(finding, now)); }
    [Fact] public void SnoozeExpiryDoesNotResolveCondition() { var finding = Finding(); finding.SnoozedUntilUtc = now.AddSeconds(-1); FindingLifecycle.ExpireControls(finding, now); Assert.Equal("open", finding.State); Assert.Null(finding.ResolvedUtc); }
    [Fact] public void ChangedEpochDisablesCounterIncreaseComparison() { var current = Sample(-1); current.Metric = "ata.reallocated_sectors"; current.Value = 10; current.ResetEpoch = "boot-2"; var previous = Sample(-2); previous.Metric = current.Metric; Assert.Null(HealthRules.Evaluate(current, previous)); }
}
