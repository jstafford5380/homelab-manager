using System.Text.Json;

namespace Homelab.Api;
public static class PlanEditing
{
    internal static void Record(HomelabDb db, PlanningItem item, string? before, string actor, string action)
    {
        db.PlanChanges.Add(new() { PlanId = item.Id, Actor = actor, BeforeJson = before, AfterJson = JsonSerializer.Serialize(item) });
        db.Activity.Add(new() { Actor = actor, Action = action, Detail = item.Id + ": " + item.Title });
    }
}
