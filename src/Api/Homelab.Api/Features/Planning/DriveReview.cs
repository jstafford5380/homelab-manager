
namespace Homelab.Api;
public record DriveReview(Guid DiskId, string Model, string Serial, string Priority, string NextStep, DateTime LastSeenUtc, bool Present, string MappingConfidence, PlanningReason[] Reasons, Guid[] OpenPlanIds)
{
    public string PriorityKey => Priority == "Review media evidence" ? "media" : Priority == "Review observed issue" ? "issue" : "prepare";
    public string? Label { get; init; }
    public string? Location { get; init; }
    public ReviewOpenPlan? OpenPlan { get; init; }
    public ReviewSpareFit SpareFit { get; init; } = new("unchecked", null, null, null);
}
public record ReviewOpenPlan(Guid Id, string Status);
public record ReviewSpareFit(string Status, Guid? AssetId, string? AssetName, DateTime? ValidUntilUtc);
