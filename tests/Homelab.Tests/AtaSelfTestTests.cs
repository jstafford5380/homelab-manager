using System.Text.Json;
using Homelab.Api;
namespace Homelab.Tests;
public sealed class AtaSelfTestTests
{
    private static AtaSelfTestLog[] Parse(string section, bool verified=true) {
        using var doc=JsonDocument.Parse("{\"ata_smart_self_test_log\":{\"standard\":"+section+"}}");
        return AtaSelfTests.Parse(doc.RootElement,verified);
    }
    [Theory]
    [InlineData(0,"completed without error")][InlineData(16,"aborted by host")][InlineData(32,"interrupted by reset")]
    [InlineData(48,"failed")][InlineData(64,"failed")][InlineData(80,"failed")][InlineData(96,"failed")][InlineData(112,"failed")][InlineData(128,"failed")]
    [InlineData(144,"unknown")][InlineData(240,"in progress")][InlineData(249,"in progress")]
    public void StatusByteDeterminesResultNotThePassedFlag(int status,string result) {
        var log=Assert.Single(Parse($$"""{"table":[{"type":{"value":2,"string":"Extended offline"},"status":{"value":{{status}},"passed":true},"lifetime_hours":120,"lba":0}]}"""));
        var row=Assert.Single(log.Entries);Assert.Equal(result,row.Result);Assert.Equal(status,row.StatusCode);
        Assert.Equal(120,row.LifetimeHours);Assert.Equal(0,row.FirstErrorLba);Assert.Null(row.RemainingPercent);
        Assert.Equal("observed",log.Quality);
    }
    [Fact] public void AbsentLogsAndExplicitEmptyAreDistinct() {
        using var doc=JsonDocument.Parse("{}");Assert.Empty(AtaSelfTests.Parse(doc.RootElement,true));
        Assert.Equal("reported empty",Assert.Single(Parse("{\"count\":0}")).Coverage);
        Assert.Equal("entries unavailable",Assert.Single(Parse("{}")).Coverage);
        Assert.Equal("entries unavailable",Assert.Single(Parse("{\"table\":[]}")).Coverage);
        Assert.Equal("unverified",Assert.Single(Parse("{\"count\":0}",false)).Quality);
    }
    [Fact] public void InvalidNumericsNeverBecomeZeroAndContradictionsCannotBecomePasses() {
        var log=Assert.Single(Parse("""{"table":[{"type":{"value":256},"status":{"value":"0","remaining_percent":101},"lifetime_hours":65536,"lba":281474976710655},{"status":{"value":0,"passed":false}},{"status":{"value":1}}]}"""));
        var first=log.Entries[0];Assert.Null(first.TypeCode);Assert.Null(first.StatusCode);Assert.Null(first.RemainingPercent);Assert.Null(first.LifetimeHours);Assert.Null(first.FirstErrorLba);Assert.Equal("unknown",first.Result);
        Assert.Equal("inconsistent",log.Entries[1].Result);Assert.Equal("inconsistent",log.Entries[2].Result);
    }
    [Theory][InlineData("[]")][InlineData("{\"table\":{}}")][InlineData("{\"table\":[null]}")][InlineData("{\"count\":0,\"table\":[{}]}")]
    public void MalformedLogCannotSilentlyLoseEntries(string section) => Assert.Throws<InvalidDataException>(()=>Parse(section));
    [Fact] public void OversizedLogsAreRejectedInsteadOfTruncated() => Assert.Throws<InvalidDataException>(()=>Parse("{\"table\":["+string.Join(',',Enumerable.Repeat("{}",257))+"]}"));
    [Fact] public void StandardAndExtendedLogsRemainSeparate() {
        using var doc=JsonDocument.Parse("""{"ata_smart_self_test_log":{"standard":{"count":0},"extended":{"table":[{"status":{"value":112}}]}}}""");
        var logs=AtaSelfTests.Parse(doc.RootElement,true);Assert.Equal(2,logs.Length);Assert.EndsWith(".standard",logs[0].Source);Assert.EndsWith(".extended",logs[1].Source);Assert.Equal("failed",logs[1].Entries[0].Result);
    }
}
