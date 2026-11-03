namespace Homelab.Api.Features.Storage.Smart.GetCachedTemperatures;
public sealed record Response(bool Enabled, string Status, DateTime? LastAttemptUtc, DateTime? LastSuccessUtc, CachedTemperatureSnapshot? Snapshot);
