using Homelab.Api;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using System.Text.Json;
namespace Homelab.Tests;
public sealed class HardwareTests
{
    private static readonly DateTime Now=new(2026,11,1,20,0,0,DateTimeKind.Utc);
    private static HardwareMetric Metric(int days=0,string source="collected",int above=0,int count=0) => new(source,30,30,90,80,null,days,above,count,false,80,null,[]);
    [Theory]
    [InlineData(false,false,null,0,0,"none",0,0,"Not monitored")]
    [InlineData(false,false,null,0,0,"collected",0,0,"Healthy")]
    [InlineData(false,true,false,5,5,"collected",0,0,"Act now")]
    [InlineData(false,false,true,5,5,"collected",0,0,"Act now")]
    [InlineData(false,false,false,5,1,"collected",0,0,"Watch")]
    [InlineData(false,false,null,3,0,"collected",0,0,"Near its limit")]
    [InlineData(false,false,null,2,0,"collected",0,0,"Healthy")]
    [InlineData(false,false,null,0,0,"entered",2,4,"Near its limit")]
    [InlineData(false,false,null,0,0,"entered",2,5,"Healthy")]
    [InlineData(false,false,null,0,0,"entered",1,1,"Healthy")]
    [InlineData(true,true,true,5,5,"collected",0,0,"Last known: watch")]
    public void StatusPrecedence(bool stale,bool missing,bool? fan,int useDays,int heatDays,string source,int above,int count,string expected)
    {
        var use=source=="none"?null:Metric(useDays,source,above,count);var heat=heatDays>0?Metric(heatDays):null;
        Assert.Equal(expected,HardwareMetrics.Status(stale,"processor",missing,fan,use,heat,30,"Watch","Last observation").Label);
    }
    [Theory][InlineData(1,1,true)][InlineData(7,1,true)][InlineData(30,1,false)][InlineData(30,3,true)][InlineData(90,2,false)][InlineData(90,3,true)]
    public void CardThresholdsDependOnSelectedRange(int range,int above,bool expected) => Assert.Equal(expected,HardwareMetrics.Status(false,"processor",false,null,Metric(above),null,range).Label=="Near its limit");
    [Fact]
    public void HourlyReportingAcceptsBothEndpointsOfOneSecondSeries()
    {
        var end = DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 300 * 300;
        var data = JsonSerializer.SerializeToElement(new[]
        {
            new
            {
                name = "cpu",
                legend = new[] { "time", "idle" },
                data = Enumerable.Range(0, 3601).Select(i => new[] { (double)(end - 3600 + i), 75d })
            }
        });

        var readings = HardwareCollection.Normalize(data, Guid.NewGuid(), Guid.NewGuid(),
            DateTimeOffset.FromUnixTimeSeconds(end).UtcDateTime, null);

        Assert.Equal(12, readings.Length);
        Assert.All(readings, reading => Assert.Equal(25d, reading.CpuPercent));
    }

    [Fact]public void EnteredDotsNeverAlterCollectedAveragesOrPreviousPeriod()
    {
        var rows=new[]{new HardwareReading{Source="collected",TakenUtc=Now.AddHours(-1),CpuPercent=10},new HardwareReading{TakenUtc=Now.AddHours(-2),CpuPercent=100},new HardwareReading{TakenUtc=Now.AddHours(-3),PowerWatts=42}};
        var m=HardwareMetrics.Metric(rows,"cpu",Now,30,80,60,Now.AddDays(-1));
        Assert.Equal("collected",m.Source);Assert.Null(m.Average);Assert.Equal(10,m.Latest);Assert.Equal(1,m.ReadingCount);Assert.Equal(2,m.Series.Length);Assert.Null(m.PreviousAverage);Assert.True(m.Partial);
        var power=HardwareMetrics.Metric(rows,"power",Now,30,null,60,null);Assert.Equal("entered",power.Source);Assert.Equal(42,power.Average);
    }
    [Fact]public void SustainedUseCountsDistinctFiveMinuteIntervalsAndNeverBridgesGaps()
    {
        var duplicate=Enumerable.Range(0,20).Select(i=>new HardwareReading{Source="collected",TakenUtc=Now.AddMinutes(-2),CpuPercent=99}).ToArray();
        Assert.Equal(0,HardwareMetrics.Metric(duplicate,"cpu",Now,1,80,60,Now.AddDays(-2)).DaysAboveLevel);
        var hour=Enumerable.Range(0,12).Select(i=>new HardwareReading{Source="collected",TakenUtc=Now.AddMinutes(-5*(i+1)),CpuPercent=99}).ToArray();
        Assert.Equal(1,HardwareMetrics.Metric(hour,"cpu",Now,1,80,60,Now.AddDays(-2)).DaysAboveLevel);
        var sparse=hour.Where((_,i)=>i%2==0).ToArray();Assert.Equal(0,HardwareMetrics.Metric(sparse,"cpu",Now,1,80,60,Now.AddDays(-2)).DaysAboveLevel);
    }
    [Fact]public void RangeCoverageIncludesMissingIntervalsAndChartBreaksRemainVisible()
    {
        var rows=new[]{new HardwareReading{Source="collected",TakenUtc=Now.AddHours(-2)},new HardwareReading{Source="collected",TakenUtc=Now.AddHours(-1)}};
        var c=HardwareMetrics.Coverage(rows,Now,1,Now.AddHours(-2));Assert.Equal(2,c.Samples);Assert.Equal(288,c.ExpectedSamples);Assert.Equal(2,c.Gaps.Length);Assert.Equal(1,c.CoveredDays);
    }
    [Fact]public void InputValidationRejectsInvalidAndUnmeasuredReadings()
    {
        HardwareReadingInput R(double? cpu=null,double? mem=null,DateTime? at=null)=>new(null,at??Now,"moment",null,cpu,mem,null,null,"");
        Assert.NotNull(HardwareReadings.Validate(R(),100,Now));Assert.NotNull(HardwareReadings.Validate(R(101),100,Now));Assert.NotNull(HardwareReadings.Validate(R(mem:101),100,Now));Assert.NotNull(HardwareReadings.Validate(R(double.NaN),100,Now));Assert.NotNull(HardwareReadings.Validate(R(20,at:Now.AddDays(1)),100,Now));Assert.Null(HardwareReadings.Validate(R(20),100,Now));
    }
    [Fact]public void ComponentValidationRejectsNullSuccessorsAndNullSlots()
    {
        Assert.NotNull(HardwareComponents.Validate((HardwareComponentInput?)null,Now));
        var input=new HardwareComponentInput(null,"memory","Memory","","","",null,null,null,null,false,[null!]);
        Assert.NotNull(HardwareComponents.Validate(input,Now));
    }
    [Fact]public void ReportingBoundaryIsTypedFixedAndRejectsGeneralCalls()
    {
        Assert.DoesNotContain("reporting.netdata_get_data",TrueNasClient.ReadMethods.AsEnumerable());
        object[] args=[new[]{new{name="cpu"},new{name="memory"},new{name="arcsize"},new{name="cputemp"}},new{unit="HOUR",page=1,aggregate=false}];
        Assert.True(TrueNasClient.AllowsReadParameters("reporting.netdata_get_data",args));
        Assert.False(TrueNasClient.AllowsReadParameters("reporting.netdata_get_data",[new[]{new{name="disktemp"}},new{unit="HOUR",page=1,aggregate=false}]));
        Assert.False(TrueNasClient.AllowsReadParameters("reporting.netdata_get_data",[args[0],new{unit="YEAR",page=9,aggregate=false}]));
    }
    [Fact]public void TrueNasMemoryExcludesArcAndUnknownDimensionsStayUnknown()
    {
        var at=new DateTimeOffset(Now.AddMinutes(-10)).ToUnixTimeSeconds();
        object G(string name,string[] legend,object[][] rows)=>new{name,legend,data=rows};
        var data=JsonSerializer.SerializeToElement(new[]{G("cpu",["time","idle"],[[at,70]]),G("memory",["time","free"],[[at,1000]]),G("arcsize",["time","arc_size"],[[at,2000]]),G("cputemp",["time","cpu0","cpu1"],[[at,42,86]])});
        var row=Assert.Single(HardwareCollection.Normalize(data,Guid.NewGuid(),Guid.NewGuid(),Now,4000*1048576d));
        Assert.Equal(30,row.CpuPercent);Assert.Equal(1000*1048576d,row.MemoryUsedBytes);Assert.Equal(2000*1048576d,row.CacheBytes);Assert.Equal(86,row.TemperatureC);Assert.Null(row.PowerWatts);
        var missing=Assert.Single(HardwareCollection.Normalize(JsonSerializer.SerializeToElement(new[]{G("memory",["time","free"],[[at,1000]]),G("cpu",["time","idle"],[[at,90]])}),Guid.NewGuid(),Guid.NewGuid(),Now,4000*1048576d));Assert.Null(missing.MemoryUsedBytes);
    }
    [Fact]public async Task ExistingInventoryReadPersistsOnlySupportedFieldsAndHistory()
    {
        using var connection=new SqliteConnection("Data Source=:memory:");await connection.OpenAsync();await using var db=new HomelabDb(new DbContextOptionsBuilder<HomelabDb>().UseSqlite(connection).Options);await db.Database.MigrateAsync();
        var nas=new NasConnection{Name="A",LastSuccessUtc=Now};db.Connections.Add(nas);await db.SaveChangesAsync();
        var info=JsonSerializer.SerializeToElement(new{version="TrueNAS-24.10.2.4",model="CPU",cores=12,physical_cores=6,physmem=64000000000,system_product="NAS model",uptime_seconds=86400});
        await HardwareCollection.StageInventory(db,nas,info,Now,default);await db.SaveChangesAsync();
        Assert.Equal(3,await db.HardwareComponents.CountAsync());Assert.False(await db.HardwareComponents.AnyAsync(c=>c.Kind=="motherboard"));
        var s=await db.HardwareServers.SingleAsync();var d=await HardwareMetrics.Detail(db,s,Now,30,false);
        Assert.Equal("CPU",d.Components.Single(c=>c.Record.Kind=="processor").Title);Assert.Equal(64000000000,d.TotalBytes);Assert.Equal("none",d.Memory.Source);
        var memory=await db.HardwareComponents.SingleAsync(c=>c.Kind=="memory");memory.SlotsJson=JsonSerializer.Serialize(new[]{new HardwareSlot("DIMM1",64000000000),new HardwareSlot("DIMM2",64000000000)});await db.SaveChangesAsync();
        d=await HardwareMetrics.Detail(db,s,Now,30,false);Assert.Equal("Act now",d.Components.Single(c=>c.Record.Kind=="memory").Status.Label);
        nas.LastSuccessUtc=Now.AddDays(-2);memory.LastStatus="Act now";await db.SaveChangesAsync();d=await HardwareMetrics.Detail(db,s,Now,30,false);Assert.Null(d.Server.WatchCount);Assert.StartsWith("Last known",d.Components[0].Status.Label);
    }
    [Fact]public async Task SynologyMemorySeparatesCacheAndFanFailureHasPriority()
    {
        using var connection=new SqliteConnection("Data Source=:memory:");await connection.OpenAsync();await using var db=new HomelabDb(new DbContextOptionsBuilder<HomelabDb>().UseSqlite(connection).Options);await db.Database.MigrateAsync();
        var s=new HardwareServer{Platform="synology",Name="DSM",Connected=true,LastSuccessUtc=Now};db.HardwareServers.Add(s);await db.SaveChangesAsync();
        await SynologyHardwareCollector.Stage(db,s,new Dictionary<string,string>{{"model","DS920+"},{"totalKiB","8000000"},{"freeKiB","1000000"},{"cacheKiB","4000000"},{"buffersKiB","1000000"},{"cpuIdle","7"},{"temperature","55"},{"systemFan","2"}},Now,default);await db.SaveChangesAsync();
        var r=await db.HardwareReadings.SingleAsync();Assert.Equal(2000000*1024d,r.MemoryUsedBytes);Assert.Equal(5000000*1024d,r.CacheBytes);Assert.Equal(93,r.CpuPercent);Assert.Null(r.PowerWatts);
        var d=await HardwareMetrics.Detail(db,s,Now,30,true);Assert.Equal("Act now",d.Components.Single(c=>c.Record.Kind=="cooling").Status.Label);
        Assert.Equal(10,SynologyHardwareReader.Oids.Count);
        s.Connected=false;await db.SaveChangesAsync();d=await HardwareMetrics.Detail(db,s,Now,30,true);
        Assert.Equal("none",d.Cpu.Source);Assert.Equal("Not monitored",d.Components.Single(c=>c.Record.Kind=="cooling").Status.Label);
    }
    [Fact]public async Task HourlyRetentionKeepsEnteredEvidenceAndExactReviewHistoryForAYear()
    {
        using var connection=new SqliteConnection("Data Source=:memory:");await connection.OpenAsync();await using var db=new HomelabDb(new DbContextOptionsBuilder<HomelabDb>().UseSqlite(connection).Options);await db.Database.MigrateAsync();
        var server=new HardwareServer();db.HardwareServers.Add(server);await db.SaveChangesAsync();
        foreach(var day in new[]{40,400})foreach(var minutes in new[]{0,5})db.HardwareReadings.Add(new(){ServerId=server.Id,Source="collected",TakenUtc=Now.AddDays(-day).AddMinutes(minutes),CpuPercent=20+minutes,TemperatureC=50+minutes});
        db.HardwareReadings.Add(new(){ServerId=server.Id,Source="entered",TakenUtc=Now.AddDays(-400),PowerWatts=36});await db.SaveChangesAsync();
        await HardwareRetention.Summarize(db,Now,default);await HardwareRetention.Summarize(db,Now,default);
        var hour=Assert.Single(await db.HardwareHourly.ToArrayAsync());Assert.Equal(2,hour.SampleCount);Assert.Equal(22.5,hour.CpuAverage);Assert.Equal(55,hour.TemperatureMaxC);
        Assert.Equal(5,await db.HardwareReadings.CountAsync());await HardwareRetention.Expire(db,Now,default);
        Assert.Equal(3,await db.HardwareReadings.CountAsync());Assert.Single(await db.HardwareReadings.Where(r=>r.Source=="entered").ToArrayAsync());
    }

    [Fact]public async Task ReviewLevelChangesRecomputeUseWithoutReusingPreviousPartsEvidence()
    {
        using var connection=new SqliteConnection("Data Source=:memory:");await connection.OpenAsync();await using var db=new HomelabDb(new DbContextOptionsBuilder<HomelabDb>().UseSqlite(connection).Options);await db.Database.MigrateAsync();
        var nas=new NasConnection{LastSuccessUtc=Now};db.Connections.Add(nas);await db.SaveChangesAsync();await HardwareInventory.EnsureServers(db,default);
        var server=await db.HardwareServers.SingleAsync();server.CollectionStartedUtc=Now.AddDays(-40);server.LastMetricsUtc=Now;
        var oldPart=new HardwareComponent{ServerId=server.Id,Kind="processor",Name="Old CPU",RetiredUtc=Now.AddHours(-1)};
        var newPart=new HardwareComponent{ServerId=server.Id,Kind="processor",Name="New CPU",InstalledUtc=Now.AddHours(-1)};
        db.HardwareComponents.AddRange(oldPart,newPart);
        foreach(var day in new[]{1,2,3})foreach(var minute in Enumerable.Range(0,12))db.HardwareReadings.Add(new(){ServerId=server.Id,ConfigurationRevision=nas.ConfigurationRevision,Source="collected",TakenUtc=Now.AddDays(-day).AddMinutes(-minute*5),CpuPercent=85});
        db.Settings.Add(new(){HardwareCpuPercent=90});await db.SaveChangesAsync();
        var d=await HardwareMetrics.Detail(db,server,Now,30,true);Assert.Null(d.Cpu.Status);Assert.Equal("Not monitored",Assert.Single(d.Components).Status.Label);
        (await db.Settings.SingleAsync()).HardwareCpuPercent=80;await db.SaveChangesAsync();d=await HardwareMetrics.Detail(db,server,Now,30,true);
        Assert.Equal("Near its limit",d.Cpu.Status!.Label);Assert.Equal(80,d.Cpu.ReviewLevel);Assert.Equal("Not monitored",Assert.Single(d.Components).Status.Label);Assert.Single(d.RetiredComponents!);
    }

    [Theory][InlineData(true)][InlineData(false)]
    public async Task ExistingSettingsMigrationRepairsOnlyInvalidZeroDefaults(bool invalid)
    {
        using var connection=new SqliteConnection("Data Source=:memory:");await connection.OpenAsync();await using var db=new HomelabDb(new DbContextOptionsBuilder<HomelabDb>().UseSqlite(connection).Options);
        await db.GetService<IMigrator>().MigrateAsync("20261102000009_Hardware");
        db.Settings.Add(new(){HardwareCpuPercent=invalid?0:65,HardwareMemoryPercent=invalid?0:75,HardwareTemperatureC=0,HardwareSustainedMinutes=invalid?0:120});await db.SaveChangesAsync();
        await db.Database.MigrateAsync();db.ChangeTracker.Clear();var settings=await db.Settings.SingleAsync();
        Assert.Equal(invalid?80:65,settings.HardwareCpuPercent);Assert.Equal(invalid?80:75,settings.HardwareMemoryPercent);
        Assert.Equal(invalid?80:0,settings.HardwareTemperatureC);Assert.Equal(invalid?60:120,settings.HardwareSustainedMinutes);
    }

}
