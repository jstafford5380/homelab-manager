using Homelab.Api;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
namespace Homelab.Tests;
public class StoragePageTests
{
    private static readonly DateTime Now = new(2026,11,1,12,0,0,DateTimeKind.Utc);
    [Fact] public void RootOnlyDatasetUsageIsVisibleWithoutDoubleCountingParents()
    {
        var root=new DatasetUsage("tank","tank","FILESYSTEM",1000,null,null,null,null);
        var child=new DatasetUsage("tank/photos","tank","FILESYSTEM",600,null,null,null,null);
        Assert.Equal(root,Assert.Single(Datasets.ForStoragePool(new(Now,[root]),"tank")));
        Assert.Equal(child,Assert.Single(Datasets.ForStoragePool(new(Now,[root,child]),"tank")));
        Assert.Empty(Datasets.ForStoragePool(new(Now,[root]),"other"));
        Assert.Empty(Datasets.ForStoragePool(null,"tank"));
    }
    [Fact] public async Task EmptyTemperatureCacheIsExplainedSeparatelyFromDisabledCollection()
    {
        using var connection=new SqliteConnection("Data Source=:memory:");await connection.OpenAsync();
        await using var db=new HomelabDb(new DbContextOptionsBuilder<HomelabDb>().UseSqlite(connection).Options);await db.Database.MigrateAsync();
        var nas=new NasConnection{Name="Cache NAS",LastSuccessUtc=Now,CachedTemperaturesJson=JsonSerializer.Serialize(new CachedTemperatureSnapshot(Now,[]))};
        db.Connections.Add(nas);db.Disks.Add(new(){ConnectionId=nas.Id,Serial="CACHE",Model="model"});
        db.Settings.Add(new(){ReadCachedTemperatures=true});
        db.Capabilities.Add(new(){ConnectionId=nas.Id,Name="cached_temperatures",Status="supported",LastSuccessUtc=Now});
        await db.SaveChangesAsync();
        var enabled=Assert.Single(Assert.Single((await StoragePage.ReadAsync(db,Now)).Servers).LooseDrives);
        Assert.Null(enabled.TemperatureC);Assert.Equal("TrueNAS returned an empty temperature cache.",enabled.TemperatureNote);
        (await db.Settings.SingleAsync()).ReadCachedTemperatures=false;await db.SaveChangesAsync();
        var disabled=Assert.Single(Assert.Single((await StoragePage.ReadAsync(db,Now)).Servers).LooseDrives);
        Assert.Equal("Collection disabled in Settings.",disabled.TemperatureNote);
    }
    [Fact] public void PhysicalDrivesGroupUnderTheirTopLevelVdevThroughReplacementTrees()
    {
        VdevNodeEvidence Node(string guid,string? parent,string type) => new(guid,parent,"data",type,"ONLINE",null,null,null,0,0,0);
        var nodes=new[]{Node("raidz",null,"RAIDZ1"),Node("replacing","raidz","REPLACING"),Node("old","replacing","DISK"),
            Node("new","replacing","DISK"),Node("peer","raidz","DISK"),Node("mirror",null,"MIRROR"),Node("other","mirror","DISK")};
        var topology=JsonSerializer.Serialize(new[]{new{guid="pool",topology=new{data=new[]{new{guid="raidz",name="raidz1-0"},new{guid="mirror",name="mirror-1"}}}}});
        var groups=StorageDriveGroups.Map(nodes,topology,"pool");
        Assert.Equal(4,groups.Count);Assert.Equal("raidz",groups["old"].Id);Assert.Equal(groups["old"],groups["new"]);
        Assert.Equal(groups["new"],groups["peer"]);Assert.Equal("raidz1-0",groups["peer"].Name);Assert.Equal("mirror-1",groups["other"].Name);
        Assert.NotEqual(groups["old"].Id,groups["other"].Id);
    }
    [Fact] public void BrokenOrCyclicVdevAncestryRemainsUngrouped()
    {
        VdevNodeEvidence Node(string guid,string? parent,string type,string role="data") => new(guid,parent,role,type,"ONLINE",null,null,null,0,0,0);
        var nodes=new[]{Node("missing","absent","DISK"),Node("leaf","a","DISK"),Node("a","b","REPLACING"),Node("b","a","REPLACING"),
            Node("cross","log-root","DISK"),Node("log-root",null,"MIRROR","log")};
        Assert.Empty(StorageDriveGroups.Map(nodes,null,"pool"));
    }
    [Fact] public async Task StorageResponseCarriesThePhysicalDrivesReportedVdevGroup()
    {
        using var connection=new SqliteConnection("Data Source=:memory:");await connection.OpenAsync();
        await using var db=new HomelabDb(new DbContextOptionsBuilder<HomelabDb>().UseSqlite(connection).Options);await db.Database.MigrateAsync();
        var nas=new NasConnection{Enabled=false,Name="Grouped NAS",TopologyJson=JsonSerializer.Serialize(new[]{new{guid="pool",name="tank",status="ONLINE",topology=new{data=new[]{new{guid="100",name="raidz2-0"}}}}})};
        var disk=new Disk{ConnectionId=nas.Id,Serial="GROUPED",Model="model"};
        var root=new VdevNodeEvidence("100",null,"data","RAIDZ2","ONLINE",null,null,null,0,0,0);
        var leaf=new VdevNodeEvidence("101","100","data","DISK","ONLINE",null,null,null,0,0,0,new("inferred",disk.Id,disk.Serial,null,disk.Model,"Test identity match"));
        db.Connections.Add(nas);db.Disks.Add(disk);db.PoolSnapshots.Add(new(){ConnectionId=nas.Id,PoolGuid="pool",Name="tank",FetchedUtc=Now,Status="ONLINE",VdevEvidenceJson=JsonSerializer.Serialize(new VdevSnapshotEvidence(VdevEvidence.Schema,"reported","fixture",[root,leaf]),new JsonSerializerOptions(JsonSerializerDefaults.Web))});
        await db.SaveChangesAsync();
        var page=await StoragePage.ReadAsync(db,Now);var result=Assert.Single(Assert.Single(Assert.Single(page.Servers).Pools).Drives);
        Assert.Equal("100",result.Group!.Id);Assert.Equal("raidz2-0",result.Group.Name);Assert.Equal("data",result.Group.Role);
    }
    [Theory]
    [InlineData(false,false,"FAULTED",9,"critical",true,true,"Missing")]
    [InlineData(false,true,"FAULTED",9,"review",true,true,"Act now")]
    [InlineData(false,true,"ONLINE",0,"critical",false,true,"Act now")]
    [InlineData(false,true,"ONLINE",1,"",false,true,"Watch")]
    [InlineData(false,true,"ONLINE",0,"urgent",false,true,"Watch")]
    [InlineData(false,true,"ONLINE",0,"review",false,true,"Watch")]
    [InlineData(false,true,"ONLINE",0,"",true,true,"Watch")]
    [InlineData(false,true,"ONLINE",0,"",false,false,"Spare")]
    [InlineData(false,true,"ONLINE",0,"",false,true,"Healthy")]
    [InlineData(true,true,"ONLINE",0,"",false,true,"Last known: healthy")]
    [InlineData(true,false,"FAULTED",9,"critical",true,true,"Last known: missing")]
    public void DriveRulesRespectPrecedence(bool stale,bool present,string state,long errors,string severity,bool alert,bool pool,string expected)
    {
        var findings=severity.Length == 0 ? Array.Empty<Finding>() : [new Finding{Severity=severity}];
        Assert.Equal(expected,StorageStatusRules.Drive(stale,present,state,errors,0,0,findings,alert,pool).Label);
    }
    [Fact] public void SmartAndUnknownEvidenceRemainExplicit()
    {
        Assert.Equal("Unknown",StorageStatusRules.Drive(false,true,"ONLINE",0,0,0,[],false,true,true,null,false).Label);
        Assert.Equal("Act now",StorageStatusRules.Drive(false,true,"ONLINE",0,0,0,[],false,true,true,false,true).Label);
        Assert.Equal("Healthy",StorageStatusRules.Drive(false,true,"ONLINE",0,0,0,[],false,true,false,false,false).Label);
        Assert.Equal("Unknown",StorageStatusRules.Drive(false,true,null,null,null,null,[],false,true).Label);
        Assert.Equal("Healthy",StorageStatusRules.Drive(false,true,"ONLINE",0,0,0,[new(){Severity="critical",State="resolved"}],false,true).Label);
    }
    [Fact] public void StalePoolDoesNotEraseItsLastKnownDriveIssue()
    {
        Assert.Equal("Last known: needs a look",StorageStatusRules.Pool(true,"ONLINE",[new("Last known: watch","neutral","old finding")]).Label);
        Assert.Equal("Partly unknown",StorageStatusRules.Pool(false,"ONLINE",[new("Unknown","neutral",null)]).Label);
        Assert.Equal("Degraded",StorageStatusRules.Pool(false,"DEGRADED",[new("Healthy","ok",null)]).Label);
    }
    [Fact] public void FreshnessAndBayConfirmationCannotImplyCurrentCertainty()
    {
        var c=new NasConnection{LastSuccessUtc=Now.AddMinutes(-4),PollSeconds=60};
        Assert.Equal("Reporting",StoragePage.Freshness(c,Now));c.LastSuccessUtc=Now.AddMinutes(-6);Assert.Equal("Out of date",StoragePage.Freshness(c,Now));
        c.LastSuccessUtc=null;Assert.Equal("No collection yet",StoragePage.Freshness(c,Now));c.Enabled=false;Assert.Equal("Paused",StoragePage.Freshness(c,Now));
        var d=new Disk{Bay="Bay 2",BayVerified=true,Serial="serial",Model="model"};
        Assert.False(StorageInventory.BayConfirmed(d)); d.BayConfirmedIdentity=StorageInventory.Identity(d);Assert.True(StorageInventory.BayConfirmed(d));
        d.Wwn="5000c50012345678";Assert.False(StorageInventory.BayConfirmed(d));
    }
    [Fact] public async Task StorageAggregatesOnlyEachServersOwnDataAndPausesStaleForecasts()
    {
        using var connection=new SqliteConnection("Data Source=:memory:");await connection.OpenAsync();
        await using var db=new HomelabDb(new DbContextOptionsBuilder<HomelabDb>().UseSqlite(connection).Options);await db.Database.MigrateAsync();
        NasConnection a=new NasConnection{Name="A",LastSuccessUtc=Now},b=new NasConnection{Name="B",LastSuccessUtc=Now.AddDays(-2)};
        db.Connections.AddRange(a,b);
        Disk ad=new Disk{ConnectionId=a.Id,Serial="A",SizeBytes=8000000000000,Alias="sda",LastSeenUtc=Now},bd=new Disk{ConnectionId=b.Id,Serial="B",SizeBytes=null};
        db.Disks.AddRange(ad,bd);db.Findings.Add(new(){ConnectionId=a.Id,DiskId=ad.Id,Severity="review",Title="Check A"});
        db.NativeAlerts.Add(new(){ConnectionId=a.Id,Class="Alert A",Message="Serial A"});await db.SaveChangesAsync();
        var data=await StoragePage.ReadAsync(db,Now);
        Assert.False(data.SmartEnabled);var sa=data.Servers.Single(x=>x.Id==a.Id);var sb=data.Servers.Single(x=>x.Id==b.Id);
        Assert.Single(sa.LooseDrives);Assert.Equal(ad.Id,sa.LooseDrives[0].Disk.Id);Assert.Equal(1,sa.AttentionCount);Assert.Equal(8000000000000,sa.RawCapacityBytes);
        Assert.Null(sb.RawCapacityBytes);Assert.Null(sb.AttentionCount);Assert.Empty(sb.Alerts);Assert.StartsWith("Last known:",sb.LooseDrives[0].Status.Label);
    }
    [Fact] public void TrendBreaksAtResetsAndUnknownEpochs()
    {
        var rows=new[]{0d,8,0,2}.Select((v,i)=>new Observation{Id=i,Metric="ata.pending_sectors",Quality="observed",Value=v,ObservedUtc=Now.AddDays(i-4),ResetEpoch="one",Source="source",SourceSchema="schema",Unit="sectors"}).ToArray();
        var trend=StorageSmart.Trend("ata.pending_sectors",rows)!;Assert.Equal(2,trend.Segments.Length);Assert.All(trend.Segments,s=>Assert.True(s.Zip(s.Skip(1)).All(p=>p.Second.Value>=p.First.Value)));
        foreach(var r in rows)r.ResetEpoch="";Assert.Equal(4,StorageSmart.Trend("ata.pending_sectors",rows)!.Segments.Length);
    }
    [Theory]
    [InlineData("smartctl --scan",true)]
    [InlineData("smartctl --json -x -n standby /dev/sda",true)]
    [InlineData("smartctl --json -x -n standby /dev/nvme0",true)]
    [InlineData("smartctl -t long /dev/sda",false)]
    [InlineData("smartctl --scan;id",false)]
    [InlineData("smartctl --json -x -n standby /dev/sda -t short",false)]
    [InlineData("smartctl --json -x -n standby /dev/../etc/passwd",false)]
    public void SmartCommandsAreFixedAndReadOnly(string command,bool valid)=>Assert.Equal(valid,SmartSshTransport.ValidCommand(command));
    [Fact] public async Task SleepingDriveRetainsLastReadingWithoutAnotherCommand()
    {
        using var connection=new SqliteConnection("Data Source=:memory:");await connection.OpenAsync();
        await using var db=new HomelabDb(new DbContextOptionsBuilder<HomelabDb>().UseSqlite(connection).Options);await db.Database.MigrateAsync();
        var nas=new NasConnection{Enabled=false};var disk=new Disk{ConnectionId=nas.Id};db.Connections.Add(nas);db.Disks.Add(disk);
        db.SmartReads.Add(new(){DiskId=disk.Id,ReadUtc=Now.AddDays(-1),ReportJson="retained"});await db.SaveChangesAsync();
        var result=new SmartCommandResult(2,JsonSerializer.Serialize(new{smartctl=new{messages=new[]{new{@string="Device is in STANDBY mode, exit(2)"}}}}));
        await SmartSshCollector.StageAsync(db,disk,result,false,Now);await db.SaveChangesAsync();
        var row=await db.SmartReads.SingleAsync();Assert.True(row.SkippedAsleep);Assert.Equal("retained",row.ReportJson);Assert.Equal(Now.AddDays(-1),row.ReadUtc);Assert.Empty(await db.Observations.ToListAsync());
    }
}
