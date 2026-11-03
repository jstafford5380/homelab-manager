namespace Homelab.Api.Features.Storage.Datasets.GetInventory;
public sealed record Response(bool Enabled, string Status, DateTime? ValidUntilUtc, DatasetSnapshot? Snapshot);
