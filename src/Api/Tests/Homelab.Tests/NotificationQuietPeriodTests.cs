using Homelab.Api;
namespace Homelab.Tests;
public class NotificationQuietPeriodTests
{
    [Theory]
    [InlineData("22:00", "07:00", "2026-11-04T05:00:00Z", true)]
    [InlineData("22:00", "07:00", "2026-11-04T14:00:00Z", false)]
    [InlineData("09:00", "17:00", "2026-11-04T16:00:00Z", true)]
    [InlineData("09:00", "17:00", "2026-11-05T00:00:00Z", false)]
    public void RangesUseTheConfiguredTimezoneAndExcludeTheirEnd(string start, string end, string utc, bool expected) =>
        Assert.Equal(expected, NotificationQuietPeriod.Active(new() { QuietStart = start, QuietEnd = end }, DateTime.Parse(utc).ToUniversalTime(), "America/Phoenix"));
    [Theory]
    [InlineData(null, null, true)] [InlineData("22:00", "07:00", true)]
    [InlineData("22:00", null, false)] [InlineData("07:00", "07:00", false)]
    [InlineData("25:00", "07:00", false)] [InlineData("", "", false)]
    public void ValidateBothTimes(string? start, string? end, bool expected) => Assert.Equal(expected, NotificationQuietPeriod.Valid(start, end));
}
