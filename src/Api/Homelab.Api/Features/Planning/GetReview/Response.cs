namespace Homelab.Api.Features.Planning.GetReview;
public sealed record ResponseSparesItem(Guid Id, string Name, int Quantity, DateTime UpdatedUtc, string CompatibilityNotes, string Verification, decimal? EstimatedUnitCost, string Currency, int CompatibleDriveCount);
public sealed record ResponseBudgetsItem(string Currency, decimal Total, int WithoutEstimate, int Count, decimal Planned, decimal Ordered);
public sealed record Response(DateTime GeneratedUtc, string RuleVersion, int DriveAgeReviewYears, int WarrantyNoticeDays, DriveReview[] Reviews, int TrackedDriveCount, List<ResponseSparesItem> Spares, IEnumerable<ResponseBudgetsItem> Budgets);
