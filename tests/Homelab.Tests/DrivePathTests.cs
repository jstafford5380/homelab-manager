using Homelab.Api;
namespace Homelab.Tests;
public sealed class DrivePathTests
{
    private static readonly DateTime Now=DateTime.UtcNow;
    private readonly Disk disk=new(){Serial="SERIAL",Model="MODEL"};
    private readonly InventoryAsset asset=new(){Type="controller",Name="HBA",UpdatedUtc=Now};
    private DrivePathInput Input()=>new(Guid.NewGuid(),asset.Id,asset.UpdatedUtc,disk.Serial,true,"Verified serial label and cable route",Now.AddDays(30));
    private DrivePathReview Row()=>new(){DiskId=disk.Id,AssetId=asset.Id,AssetUpdatedUtc=asset.UpdatedUtc,ConfirmedIdentity=disk.Serial,DiskModel=disk.Model,Attached=true,ValidUntilUtc=Now.AddDays(30)};
    [Fact]public void ValidRelationshipIsRecordedEvidenceNotCausalProof(){Assert.Null(DrivePaths.Validate(disk,asset,Input(),Now));Assert.Equal("recorded relationship",DrivePaths.Status(Row(),disk,asset,true,Now));}
    [Theory][InlineData("host")][InlineData("service")][InlineData("spare")]
    public void OnlyRelevantComponentsAccepted(string type){asset.Type=type;Assert.NotNull(DrivePaths.Validate(disk,asset,Input(),Now));}
    [Fact]public void IdentityEvidenceAndExpiryRequired(){Assert.NotNull(DrivePaths.Validate(disk,asset,Input() with{ConfirmedIdentity="other"},Now));Assert.NotNull(DrivePaths.Validate(disk,asset,Input() with{Evidence=""},Now));Assert.NotNull(DrivePaths.Validate(disk,asset,Input() with{ValidUntilUtc=Now},Now));disk.MappingConfidence="ambiguous";Assert.NotNull(DrivePaths.Validate(disk,asset,Input(),Now));}
    [Fact]public void ArchivedComponentCanBeEndedButCannotBeLinked(){asset.Archived=true;Assert.NotNull(DrivePaths.Validate(disk,asset,Input(),Now));Assert.Null(DrivePaths.Validate(disk,asset,Input() with{Attached=false},Now));}
    [Fact]public void ExpiryChangedInventoryAndIdentityInvalidateRelationship(){var row=Row();Assert.Equal("review expired",DrivePaths.Status(row,disk,asset,true,Now.AddDays(31)));asset.UpdatedUtc=Now.AddSeconds(1);Assert.Equal("asset changed",DrivePaths.Status(row,disk,asset,true,Now));asset.UpdatedUtc=Now;disk.Model="changed";Assert.Equal("identity needs review",DrivePaths.Status(row,disk,asset,true,Now));}
    [Fact]public void LaterEndedRecordDoesNotEraseEarlierEvidence(){var row=Row();Assert.Equal("superseded",DrivePaths.Status(row,disk,asset,false,Now));row.Attached=false;Assert.Equal("ended",DrivePaths.Status(row,disk,asset,true,Now));}
    [Fact]public void IdempotencyIncludesAllUserEvidence(){var input=Input();var row=Row();row.Evidence=input.Evidence;Assert.True(DrivePaths.Same(row,input));Assert.False(DrivePaths.Same(row,input with{Evidence="changed"}));Assert.False(DrivePaths.Same(row,input with{Attached=false}));}
}
