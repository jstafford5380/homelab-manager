namespace Homelab.Api.Features.Storage.Drives.GetSpareReviews;
public sealed record ResponseItemsItem(SpareCompatibilityReview Review, string Status);
public sealed record Response(DateTime GeneratedUtc, IEnumerable<ResponseItemsItem> Items, long? NextBefore);
