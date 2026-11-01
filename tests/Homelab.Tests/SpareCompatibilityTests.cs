using Homelab.Api;
namespace Homelab.Tests;
public sealed class SpareCompatibilityTests
{
    private static readonly DateTime Now = new(2026,11,1,12,0,0,DateTimeKind.Utc);
    [Theory]
    [InlineData("current", "reviewed compatible")]
    [InlineData("edited", "inventory changed")]
    [InlineData("archived", "spare unavailable")]
    [InlineData("wrong-type", "spare unavailable")]
    [InlineData("missing", "spare unavailable")]
    [InlineData("ambiguous", "identity needs review")]
    [InlineData("serial", "identity needs review")]
    [InlineData("model", "identity needs review")]
    [InlineData("expired", "review expired")]
    [InlineData("absent", "drive not current")]
    [InlineData("retired", "drive not current")]
    [InlineData("empty", "no stock recorded")]
    [InlineData("incompatible", "incompatible")]
    [InlineData("unknown", "unknown")]
    public void OnlyCurrentEvidenceForAnUnambiguousDriveAndUnchangedStockCanApply(string scenario, string expected)
    {
        var disk = new Disk { Serial = "A", Model = "model" };
        InventoryAsset? asset = new() { Type = "spare", UpdatedUtc = Now };
        var review = new SpareCompatibilityReview { ConfirmedIdentity = "A", DiskModel = "model", AssetUpdatedUtc = Now, ValidUntilUtc = Now.AddDays(1), Verdict = "compatible" };
        switch (scenario) {
            case "edited": asset.UpdatedUtc = Now.AddTicks(1); break;
            case "archived": asset.Archived = true; break;
            case "wrong-type": asset.Type = "host"; break;
            case "missing": asset = null; break;
            case "ambiguous": disk.MappingConfidence = "ambiguous"; break;
            case "serial": disk.Serial = "B"; break;
            case "model": disk.Model = "different"; break;
            case "expired": review.ValidUntilUtc = Now; break;
            case "absent": disk.Present = false; break;
            case "retired": disk.Lifecycle = "retired"; break;
            case "empty": asset.Quantity = 0; break;
            case "incompatible": review.Verdict = "incompatible"; break;
            case "unknown": review.Verdict = "unknown"; break;
        }
        Assert.Equal(expected, SpareCompatibility.Status(review,disk,asset,Now));
    }
    [Theory]
    [InlineData("request")][InlineData("identity")][InlineData("verdict")][InlineData("evidence")][InlineData("expired")][InlineData("far-future")][InlineData("timezone")]
    public void InvalidReviewCannotBeRecorded(string scenario)
    {
        var disk = new Disk { Serial = "A" }; var asset = new InventoryAsset { Type = "spare" };
        var input = new SpareReviewInput(Guid.NewGuid(),asset.Id,asset.UpdatedUtc,"A","compatible","Manufacturer sheet and enclosure requirements",Now.AddDays(30));
        input = scenario switch {
            "request" => input with { RequestId = Guid.Empty },
            "identity" => input with { ConfirmedIdentity = "B" },
            "verdict" => input with { Verdict = "replace-now" },
            "evidence" => input with { Evidence = " " },
            "expired" => input with { ValidUntilUtc = Now },
            "far-future" => input with { ValidUntilUtc = Now.AddYears(2) },
            _ => input with { ValidUntilUtc = DateTime.SpecifyKind(Now.AddDays(1),DateTimeKind.Unspecified) }
        };
        Assert.NotNull(SpareCompatibility.Validate(disk,asset,input,Now));
    }
}
