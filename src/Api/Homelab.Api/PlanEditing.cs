using System.Text.Json;
using Microsoft.EntityFrameworkCore;
namespace Homelab.Api;

public sealed class PlanChange
{
    public long Id { get; set; }
    public Guid PlanId { get; set; }
    public DateTime AtUtc { get; set; } = DateTime.UtcNow;
    public string Actor { get; set; } = "";
    public string? BeforeJson { get; set; }
    public string AfterJson { get; set; } = "";
}
public static class PlanEditing
{
    public static void MapPlanEditing(this RouteGroupBuilder api)
    {
        api.MapPost("/plans", async (PlanningItem item, HomelabDb db, HttpContext context) => {
            if (Planning.ValidatePlan(item) is {} error) return Results.BadRequest(new { error });
            if (item.DiskId is {} disk && !await db.Disks.AnyAsync(x => x.Id == disk)) return Results.BadRequest(new { error = "Disk does not exist." });
            item.Id = Guid.NewGuid(); item.Revision = Guid.NewGuid();
            db.Plans.Add(item); Record(db, item, null, context.User.Identity!.Name!, "Plan created");
            await db.SaveChangesAsync(); return Results.Ok(item);
        });
        api.MapPut("/plans/{id:guid}", async (Guid id, PlanningItem input, HomelabDb db, HttpContext context) => {
            var item = await db.Plans.FindAsync(id); if (item is null) return Results.NotFound();
            if (item.Revision != input.Revision) return Results.Conflict(new { error = "This plan changed since you opened it. Your edits were not saved. Reopen the latest plan before trying again." });
            if (Planning.ValidatePlan(input) is {} error) return Results.BadRequest(new { error });
            // A plan's physical-drive evidence cannot be silently reassigned during an edit.
            if (item.DiskId != input.DiskId) return Results.BadRequest(new { error = "The linked physical drive cannot be changed. Create a separate plan for another drive." });
            var before = JsonSerializer.Serialize(item);
            item.Title = input.Title; item.Reason = input.Reason; item.Owner = input.Owner;
            item.Status = input.Status; item.Budget = input.Budget; item.Currency = input.Currency;
            item.DueUtc = input.DueUtc; item.Revision = Guid.NewGuid();
            Record(db, item, before, context.User.Identity!.Name!, "Plan updated");
            await db.SaveChangesAsync(); return Results.Ok(item);
        });
        api.MapGet("/plans/{id:guid}/history", async (Guid id, long? before, HomelabDb db) => {
            if (!await db.Plans.AnyAsync(x => x.Id == id)) return Results.NotFound();
            var rows = await db.PlanChanges.AsNoTracking().Where(x => x.PlanId == id && (before == null || x.Id < before)).OrderByDescending(x => x.Id).Take(51).ToListAsync();
            return Results.Ok(new { items = rows.Take(50).Select(x => new { x.Id, x.AtUtc, x.Actor,
                before = x.BeforeJson == null ? null : JsonSerializer.Deserialize<PlanningItem>(x.BeforeJson), after = JsonSerializer.Deserialize<PlanningItem>(x.AfterJson) }), nextId = rows.Count > 50 ? (long?)rows[49].Id : null });
        });
    }
    private static void Record(HomelabDb db, PlanningItem item, string? before, string actor, string action)
    {
        db.PlanChanges.Add(new() { PlanId = item.Id, Actor = actor, BeforeJson = before, AfterJson = JsonSerializer.Serialize(item) });
        db.Activity.Add(new() { Actor = actor, Action = action, Detail = item.Id + ": " + item.Title });
    }
}
