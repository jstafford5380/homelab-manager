
namespace Homelab.Api;
public record DriveReview(Guid DiskId, string Model, string Serial, string Priority, string NextStep, DateTime LastSeenUtc, bool Present, string MappingConfidence, PlanningReason[] Reasons, Guid[] OpenPlanIds);
