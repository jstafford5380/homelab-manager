namespace Homelab.Api.Features.Storage.Pools.GetScanHistory;
public sealed record Response(IEnumerable<PoolScan> Items, long? NextBefore);
