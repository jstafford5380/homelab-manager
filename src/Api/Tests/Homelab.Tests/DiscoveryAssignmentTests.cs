using Homelab.Api;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
namespace Homelab.Tests;
public sealed class DiscoveryAssignmentTests
{
    private static readonly Guid Revision=Guid.NewGuid();private static readonly DateTime Now=DateTime.UtcNow;
    private static readonly IdentityCandidate Source=new("SERIAL","MODEL","5000c50012345678","sda");
    private static Disk Disk()=>new(){Serial=Source.Serial,Model=Source.Model,Wwn=Source.Wwn,MappingConfidence="ambiguous"};
    private static DiscoveryAssignment Decision(Disk disk)=>new(){DiskId=disk.Id,ConfigurationRevision=Revision,Wwn=Source.Wwn!,Serial=Source.Serial,Model=Source.Model,ValidUntilUtc=Now.AddDays(30)};
    [Fact]public void AssignmentRequiresExactStableIdentityAndSupportsAliasReorder()
    {var disk=Disk();var decision=Decision(disk);Assert.Same(disk,DiscoveryAssignments.Target(decision,[Source with{Alias="sdb"}],[disk],Revision,Now));Assert.Null(DiscoveryAssignments.Target(decision,[Source with{Serial="other"}],[disk],Revision,Now));Assert.Null(DiscoveryAssignments.Target(decision,[Source with{Wwn=null}],[disk],Revision,Now));}
    [Fact]public void ExpiryRevocationConfigurationAndRetirementStopAssignments()
    {var disk=Disk();var d=Decision(disk);Assert.Null(DiscoveryAssignments.Target(d,[Source],[disk],Guid.NewGuid(),Now));Assert.Null(DiscoveryAssignments.Target(d,[Source],[disk],Revision,Now.AddDays(31)));d.RevokedUtc=Now;Assert.Null(DiscoveryAssignments.Target(d,[Source],[disk],Revision,Now));d.RevokedUtc=null;disk.Lifecycle="retired";Assert.Null(DiscoveryAssignments.Target(d,[Source],[disk],Revision,Now));}
    [Fact]public void ContradictoryDuplicateWwnCannotBeOverridden()
    {var disk=Disk();Assert.Null(DiscoveryAssignments.Target(Decision(disk),[Source,Source with{Serial="DIFFERENT",Alias="sdb"}],[disk],Revision,Now));}
    [Fact]public void InventoryWindowAndRevisionAreExplicit()
    {var nas=new NasConnection{ConfigurationRevision=Revision};var snapshot=new DiscoverySnapshot(Revision,Now,[Source]);Assert.True(DiscoveryAssignments.Usable(nas,snapshot,Now));Assert.False(DiscoveryAssignments.Usable(nas,snapshot,Now.AddDays(2)));nas.Enabled=false;Assert.False(DiscoveryAssignments.Usable(nas,snapshot,Now));nas.Enabled=true;nas.ConfigurationRevision=Guid.NewGuid();Assert.False(DiscoveryAssignments.Usable(nas,snapshot,Now));}
    [Fact]public void ReviewTokenIgnoresFetchTimeButBindsSourceAndAssignments()
    {var nas=new NasConnection{ConfigurationRevision=Revision};var disk=Disk();var snapshot=new DiscoverySnapshot(Revision,Now,[Source]);var token=DiscoveryAssignments.Token(nas,snapshot,[disk],[]);Assert.Equal(token,DiscoveryAssignments.Token(nas,snapshot with{FetchedUtc=Now.AddMinutes(1)},[disk],[]));Assert.NotEqual(token,DiscoveryAssignments.Token(nas,snapshot with{Rows=[Source with{Alias="sdb"}]},[disk],[]));Assert.NotEqual(token,DiscoveryAssignments.Token(nas,snapshot,[disk],[Decision(disk)]));}
    [Fact]public async Task ReviewedWwnResolvesOnlyCurrentIdentityFindingAndPreservesOtherRecord()
    {
        await using var connection=new SqliteConnection("Data Source=:memory:");await connection.OpenAsync();await using var db=new HomelabDb(new DbContextOptionsBuilder<HomelabDb>().UseSqlite(connection).Options);await db.Database.MigrateAsync();
        var nas=new NasConnection{ConfigurationRevision=Revision};db.Connections.Add(nas);var prior=new Disk{ConnectionId=nas.Id,Serial=Source.Serial,Model=Source.Model,Wwn="5000c50087654321"};var target=Disk();target.ConnectionId=nas.Id;db.Disks.AddRange(prior,target);await db.SaveChangesAsync();
        await DiskIdentity.ReconcileAsync(db,nas.Id,[Source],Now,default,Revision);await db.SaveChangesAsync();Assert.Equal("ambiguous",target.MappingConfidence);var finding=await db.Findings.SingleAsync();Assert.Equal("open",finding.State);
        var decision=Decision(target);decision.ConnectionId=nas.Id;db.DiscoveryAssignments.Add(decision);await db.SaveChangesAsync();
        await DiskIdentity.ReconcileAsync(db,nas.Id,[Source with{Alias="sdc"}],Now.AddMinutes(1),default,Revision);await db.SaveChangesAsync();Assert.Equal("reviewed",target.MappingConfidence);Assert.True(target.Present);Assert.False(prior.Present);Assert.Equal("resolved",finding.State);Assert.Equal("5000c50087654321",prior.Wwn);Assert.Equal(2,await db.Disks.CountAsync());Assert.False(target.BayVerified);
        await DiskIdentity.ReconcileAsync(db,nas.Id,[Source],Now.AddDays(31),default,Revision);await db.SaveChangesAsync();Assert.Equal("ambiguous",target.MappingConfidence);Assert.Equal(1,await db.Findings.CountAsync(x=>x.State=="open"));
    }
    [Theory][InlineData(false)][InlineData(true)]public async Task ReviewedTargetIsNotOverwrittenByAnotherConflictingSerialGroup(bool reverse)
    {
        await using var connection=new SqliteConnection("Data Source=:memory:");await connection.OpenAsync();await using var db=new HomelabDb(new DbContextOptionsBuilder<HomelabDb>().UseSqlite(connection).Options);await db.Database.EnsureCreatedAsync();
        var nas=new NasConnection{ConfigurationRevision=Revision};db.Connections.Add(nas);var target=Disk();target.ConnectionId=nas.Id;var other=new Disk{ConnectionId=nas.Id,Serial=Source.Serial,Model=Source.Model,Wwn="5000c50087654321"};db.Disks.AddRange(target,other);var decision=Decision(target);decision.ConnectionId=nas.Id;db.DiscoveryAssignments.Add(decision);await db.SaveChangesAsync();
        IdentityCandidate[] rows=[Source,Source with{Wwn=other.Wwn,Alias="sdb"}];if(reverse)Array.Reverse(rows);
        await DiskIdentity.ReconcileAsync(db,nas.Id,rows,Now,default,Revision);await db.SaveChangesAsync();Assert.Equal("reviewed",target.MappingConfidence);Assert.Equal("ambiguous",other.MappingConfidence);Assert.Equal("sda",target.Alias);
    }
    [Fact]public void ConflictingAliasCannotSupportAReviewedAssignment()
    {Assert.False(DiscoveryAssignments.AliasConsistent([Source],[Source,Source with{Wwn="5000c50087654321"}]));Assert.True(DiscoveryAssignments.AliasConsistent([Source],[Source,Source with{Alias="sdb"}]));}
    [Fact]public void SourceCaptureIsBoundedAndRoundTrips()
    {var nas=new NasConnection{DiscoveryIdentityJson=DiscoveryAssignments.Capture(Revision,Now,[Source])};Assert.Equal(Source,Assert.Single(DiscoveryAssignments.Read(nas)!.Rows));Assert.Null(DiscoveryAssignments.Capture(Revision,Now,Enumerable.Repeat(Source,4097).ToArray()));}
}
