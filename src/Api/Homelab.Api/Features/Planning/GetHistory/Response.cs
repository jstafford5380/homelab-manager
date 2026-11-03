namespace Homelab.Api.Features.Planning.GetHistory;
public sealed record ResponseItemsItem(long Id, DateTime AtUtc, string Actor, PlanningItem? Before, PlanningItem? After);
public sealed record Response(IEnumerable<ResponseItemsItem> Items, long? NextId);
