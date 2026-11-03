namespace Homelab.Api.Features.Storage.Datasets.GetRenameHistory;
public sealed record Response(IEnumerable<DatasetRenameReview> Items, long? NextBefore);
