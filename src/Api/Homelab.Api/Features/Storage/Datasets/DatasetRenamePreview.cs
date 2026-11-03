namespace Homelab.Api;
public sealed record DatasetRenamePreview(long Id, DateTime FetchedUtc, string? SystemSerial, string? Version, Guid ConfigurationRevision, DatasetSnapshot? Snapshot);
