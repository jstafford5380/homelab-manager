using Homelab.Api;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
namespace Homelab.Tests;
public sealed class ObservationHistoryTests
{
    [Fact] public async Task MetricPagesReachOldEvidenceWithoutOtherMetricsCrowdingItOutAndFreezeImports()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        await using var db = new HomelabDb(new DbContextOptionsBuilder<HomelabDb>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync(); var disk = Guid.NewGuid(); var other = Guid.NewGuid(); var now = DateTime.UtcNow;
        var nas=new NasConnection{Enabled=false};db.Connections.Add(nas);db.Disks.AddRange(new Disk{Id=disk,ConnectionId=nas.Id},new Disk{Id=other,ConnectionId=nas.Id});
        for (var i=0;i<1105;i++) db.Observations.Add(new() { ConnectionId=nas.Id,DiskId=disk,Metric="temperature",Value=-5,Unit="°C",ObservedUtc=now.AddDays(-100).AddMinutes(i),SourceKey="temperature-"+i });
        for (var i=0;i<1500;i++) db.Observations.Add(new() { ConnectionId=nas.Id,DiskId=disk,Metric="power_cycles",Value=1,Unit="cycles",ObservedUtc=now.AddMinutes(-1),SourceKey="cycles-"+i });
        db.Observations.Add(new() { ConnectionId=nas.Id,DiskId=other,Metric="temperature",Value=99,ObservedUtc=now,SourceKey="other" });
        await db.SaveChangesAsync();
        var page=await ObservationHistory.ReadAsync(db,disk,"temperature",0,now); Assert.Equal(100,page.Items.Length); Assert.NotNull(page.NextBefore);
        db.Observations.Add(new() { ConnectionId=nas.Id,DiskId=disk,Metric="temperature",Value=999,ObservedUtc=now.AddDays(-200),SourceKey="late-backdated-import" }); await db.SaveChangesAsync();
        var all=page.Items.ToList();
        while(page.NextBefore is {} before) { page=await ObservationHistory.ReadAsync(db,disk,"temperature",0,page.AsOfUtc,page.SnapshotId,before);all.AddRange(page.Items); }
        Assert.Equal(1105,all.Count); Assert.Equal(all.Count,all.Select(x=>x.Id).Distinct().Count()); Assert.All(all,x=>{Assert.Equal(disk,x.DiskId);Assert.Equal("temperature",x.Metric);Assert.Equal(-5,x.Value);});
        var refreshed=await ObservationHistory.ReadAsync(db,disk,"temperature",0,now);Assert.Equal(999,refreshed.Items[0].Value);
    }
    [Theory] [InlineData(1)] [InlineData(7)] [InlineData(30)]
    public async Task WindowUsesObservedTimeRetainsUndatedAndExcludesFuture(int days)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        await using var db = new HomelabDb(new DbContextOptionsBuilder<HomelabDb>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();var disk=Guid.NewGuid();var now=DateTime.UtcNow;var nas=new NasConnection{Enabled=false};db.Connections.Add(nas);db.Disks.Add(new(){Id=disk,ConnectionId=nas.Id});
        foreach(var (key,time) in new (string,DateTime?)[] {("boundary",now.AddDays(-days)),("old",now.AddDays(-days).AddTicks(-1)),("future",now.AddSeconds(1)),("undated",null)})
            db.Observations.Add(new() {ConnectionId=nas.Id,DiskId=disk,Metric="temperature",SourceKey=key,ObservedUtc=time,ReceivedUtc=now,Value=time is null?null:-3,Quality=time is null?"unknown":"observed"});
        await db.SaveChangesAsync();var page=await ObservationHistory.ReadAsync(db,disk,"temperature",days,now);
        Assert.Equal(new[]{"boundary","undated"},page.Items.Select(x=>x.SourceKey).Order().ToArray());Assert.Null(page.NextBefore);
    }
}
