using Homelab.Api;
namespace Homelab.Tests;

public sealed class ObservationValidationTests
{
    private static readonly DateTime Now = new(2026, 10, 31, 12, 0, 0, DateTimeKind.Utc);
    private static Observation Sample() => new() { Metric = "ata.pending_sectors", Unit = "sectors", Value = 2, Quality = "observed", ObservedUtc = Now.AddMinutes(-1), SourceKey = "sample-1", Source = "operator-export", SourceSchema = "ata-vendor-x", ResetEpoch = "boot-1" };
    [Fact] public void CompleteTimestampedEvidenceIsAccepted() => Assert.Null(ObservationValidation.Validate(Sample(), Now));
    [Fact] public void NullMetadataReturnsValidationErrorsInsteadOfCrashingRules()
    {
        var value = Sample(); value.ResetEpoch = null!; Assert.NotNull(ObservationValidation.Validate(value, Now));
        value = Sample(); value.Unit = null!; Assert.NotNull(ObservationValidation.Validate(value, Now));
        value = Sample(); value.SourceSchema = null!; Assert.NotNull(ObservationValidation.Validate(value, Now));
        value = Sample(); value.SourceKey = new string('x', 201); Assert.NotNull(ObservationValidation.Validate(value, Now));
    }
    [Theory] [InlineData(-1)] [InlineData(1.5)] [InlineData(double.NaN)] [InlineData(double.PositiveInfinity)] [InlineData(9007199254740992d)]
    public void CountsMustBeNonnegativeExactWholeNumbers(double number)
    {
        var value = Sample(); value.Value = number; Assert.NotNull(ObservationValidation.Validate(value, Now));
    }
    [Fact] public void UnitsMustMatchMetricSemantics()
    {
        var value = Sample(); value.Unit = "°C"; Assert.NotNull(ObservationValidation.Validate(value, Now));
        value.Metric = "nvme.media_errors"; value.Unit = "errors"; Assert.Null(ObservationValidation.Validate(value, Now));
    }
    [Fact] public void ObservedEvidenceRequiresKnownNonfutureUtcTime()
    {
        var value = Sample(); value.ObservedUtc = null; Assert.NotNull(ObservationValidation.Validate(value, Now));
        value.ObservedUtc = Now.AddSeconds(1); Assert.NotNull(ObservationValidation.Validate(value, Now));
        value.ObservedUtc = DateTime.SpecifyKind(Now.AddMinutes(-1), DateTimeKind.Unspecified); Assert.NotNull(ObservationValidation.Validate(value, Now));
        value.ObservedUtc = null; value.Quality = "unverified"; Assert.Null(ObservationValidation.Validate(value, Now));
    }
    [Fact] public void MissingValuesStayUnknownAndRequireAnExplanation()
    {
        var value = Sample(); value.Quality = "unknown"; Assert.NotNull(ObservationValidation.Validate(value, Now));
        value.Value = null; Assert.NotNull(ObservationValidation.Validate(value, Now));
        value.MissingReason = "Source does not expose this field"; Assert.Null(ObservationValidation.Validate(value, Now));
        value.Quality = "observed"; Assert.NotNull(ObservationValidation.Validate(value, Now));
    }
    [Fact] public void TemperatureAndHoursAllowFractionalValuesWithoutConfusingThemWithCounters()
    {
        var value = Sample(); value.Metric = "temperature"; value.Unit = "°C"; value.Value = -2.5;
        Assert.Null(ObservationValidation.Validate(value, Now));
        value.Value = -274; Assert.NotNull(ObservationValidation.Validate(value, Now));
        value.Metric = "power_on_hours"; value.Unit = "hours"; value.Value = 0.5;
        Assert.Null(ObservationValidation.Validate(value, Now));
    }
}
