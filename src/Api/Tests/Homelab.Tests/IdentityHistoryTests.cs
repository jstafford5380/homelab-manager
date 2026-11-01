using Homelab.Api;
namespace Homelab.Tests;
public sealed class IdentityHistoryTests
{
    private readonly Disk a=new(){Serial="A",Model="M"};private readonly Disk b=new(){Serial="B",Model="M"};private readonly Disk c=new(){Serial="C",Model="M"};
    [Fact]public void TransitiveGroupsSplitWithoutChangingRecordIds()
    {var ab=new IdentityHistoryMerge{FirstDiskId=a.Id,SecondDiskId=b.Id};var bc=new IdentityHistoryMerge{FirstDiskId=b.Id,SecondDiskId=c.Id};var graph=new IdentityHistoryGraph([a,b,c],[ab,bc]);Assert.Equal(3,graph.Group(a.Id).Length);ab.ReversedUtc=DateTime.UtcNow;Assert.Equal([a.Id],graph.Group(a.Id));Assert.Equal(2,graph.Group(b.Id).Length);Assert.Equal("A",a.Serial);}
    [Fact]public void ReviewTokenChangesForIdentityBayPresenceAndMembershipButNotPollTime()
    {var graph=new IdentityHistoryGraph([a,b],[]);var token=graph.Token([a.Id,b.Id]);a.LastSeenUtc=DateTime.UtcNow.AddHours(1);Assert.Equal(token,graph.Token([a.Id,b.Id]));a.Bay="3";Assert.NotEqual(token,graph.Token([a.Id,b.Id]));a.Bay=null;a.Present=false;Assert.NotEqual(token,graph.Token([a.Id,b.Id]));a.Present=true;a.Wwn="5000c50012345678";Assert.NotEqual(token,graph.Token([a.Id,b.Id]));}
    [Fact]public void TokensAreStableAcrossMemberOrderAndDuplicateIds()
    {var graph=new IdentityHistoryGraph([a,b],[]);Assert.Equal(graph.Token([a.Id,b.Id]),graph.Token([b.Id,a.Id,a.Id]));}
    [Fact]public void ReversedLinksRemainAuditableButDoNotJoinGroups()
    {var link=new IdentityHistoryMerge{FirstDiskId=a.Id,SecondDiskId=b.Id,ReversedUtc=DateTime.UtcNow,Evidence="original",ReversalEvidence="correction"};var graph=new IdentityHistoryGraph([a,b],[link]);Assert.Single(graph.Group(a.Id));Assert.Equal("original",Assert.Single(graph.Links).Evidence);Assert.Equal("correction",link.ReversalEvidence);}
    [Fact]public void MissingSerialUsesWwnOrRecordIdAsExplicitConfirmation()
    {a.Serial="";Assert.Equal(a.Id.ToString(),IdentityHistory.Confirmation(a));a.Wwn="0x5000c50012345678";Assert.Equal("5000c50012345678",IdentityHistory.Confirmation(a));}
}
