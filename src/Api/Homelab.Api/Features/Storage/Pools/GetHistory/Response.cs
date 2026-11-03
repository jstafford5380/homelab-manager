namespace Homelab.Api.Features.Storage.Pools.GetHistory;
public sealed record Response(IEnumerable<System.Text.Json.Nodes.JsonNode> Items, long? NextBefore);
