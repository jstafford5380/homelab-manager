using Homelab.Api;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
namespace Homelab.Tests;
public sealed class ScanOverviewTests
{
    private static readonly DateTime Now = new(2026,11,1,12,0,0,DateTimeKind.Utc);
    [Fact] public async Task LatestCompletionOrMissingScanSuppressesOlderActiveReportPerNasAndPool()
    {
        using var f = new Fixture();
        f.Db.PoolSnapshots.AddRange(f.Row("pool",Now.AddMinutes(-2)), f.Row("pool",Now.AddMinutes(-1),"FINISHED"), f.Row("other",Now.AddMinutes(-2)), f.Row("other",Now.AddMinutes(-1),null));
        var other = new NasConnection { Enabled=false,Name="Other NAS" }; f.Db.Connections.Add(other);
        f.Db.PoolSnapshots.Add(new() { ConnectionId=other.Id,PoolGuid="pool",Name="Separate pool",FetchedUtc=Now,ScanFunction="RESILVER",ScanState="SCANNING" });
        await f.Db.SaveChangesAsync();
        var item=Assert.Single(await ScanOverview.ReadAsync(f.Db,Now)); Assert.Equal(other.Id,item.ConnectionId); Assert.Equal("paused",item.Freshness);
    }
    [Theory] [InlineData(true,"supported",30,"fresh")] [InlineData(false,"supported",30,"paused")]
    [InlineData(true,"denied",30,"denied")] [InlineData(true,"supported",181,"stale")]
    public async Task SourceFreshnessAndConnectionPauseGateCurrentState(bool enabled,string capability,int age,string expected)
    {
        using var f=new Fixture(); f.Nas.Enabled=enabled; f.Coverage.Status=capability;
        f.Db.PoolSnapshots.Add(f.Row("pool",Now.AddSeconds(-age))); await f.Db.SaveChangesAsync();
        var item=Assert.Single(await ScanOverview.ReadAsync(f.Db,Now)); Assert.Equal(expected,item.Freshness);
        Assert.Equal(expected=="fresh",item.ValidUntilUtc is not null); Assert.Null(item.Percentage); Assert.Null(item.Errors);
    }
    [Fact] public async Task RemovedPoolAndFutureSampleRemainUnverified()
    {
        using var f=new Fixture(); f.Db.PoolSnapshots.AddRange(f.Row("removed",Now),f.Row("pool",Now.AddMinutes(1))); await f.Db.SaveChangesAsync();
        Assert.All(await ScanOverview.ReadAsync(f.Db,Now),x=>Assert.Equal("unverified",x.Freshness));
    }
    [Fact] public async Task LatestTimeWinsOverInsertionOrderAndInvalidMeasurementsStayUnknown()
    {
        using var f=new Fixture(); var latest=f.Row("pool",Now,"PAUSED"); latest.ScanPercentage=101; latest.ScanErrors=-1;
        f.Db.PoolSnapshots.Add(latest); await f.Db.SaveChangesAsync(); f.Db.PoolSnapshots.Add(f.Row("pool",Now.AddMinutes(-1),"FINISHED")); await f.Db.SaveChangesAsync();
        var item=Assert.Single(await ScanOverview.ReadAsync(f.Db,Now)); Assert.Equal("PAUSED",item.State); Assert.Null(item.Percentage); Assert.Null(item.Errors);
    }
    [Fact] public async Task EqualTimeUsesLastInsertedReportAndRetainsZeroMeasurements()
    {
        using var f=new Fixture(); f.Db.PoolSnapshots.Add(f.Row("pool",Now,"FINISHED")); await f.Db.SaveChangesAsync();
        var latest=f.Row("pool",Now); latest.ScanPercentage=0; latest.ScanErrors=0; f.Db.PoolSnapshots.Add(latest); await f.Db.SaveChangesAsync();
        var item=Assert.Single(await ScanOverview.ReadAsync(f.Db,Now)); Assert.Equal(0,item.Percentage); Assert.Equal(0,item.Errors);
    }
    private sealed class Fixture : IDisposable
    {
        private readonly SqliteConnection connection=new("Data Source=:memory:");
        public HomelabDb Db { get; }
        public NasConnection Nas { get; }=new() { Name="Fixture",Enabled=true,TopologyJson="[{\"guid\":\"pool\"},{\"guid\":\"other\"}]" };
        public Capability Coverage { get; }
        public Fixture() { connection.Open(); Db=new(new DbContextOptionsBuilder<HomelabDb>().UseSqlite(connection).Options); Db.Database.EnsureCreated();
            Coverage=new() {ConnectionId=Nas.Id,Name="pools",Status="supported",ExpectedSeconds=60,LastSuccessUtc=Now}; Db.Connections.Add(Nas); Db.Capabilities.Add(Coverage); Db.SaveChanges(); }
        public PoolSnapshot Row(string pool,DateTime at,string? state="SCANNING")=>new(){ConnectionId=Nas.Id,PoolGuid=pool,Name=pool,FetchedUtc=at,ScanFunction="SCRUB",ScanState=state};
        public void Dispose(){Db.Dispose();connection.Dispose();}
    }
}
