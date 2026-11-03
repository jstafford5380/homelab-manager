using Microsoft.EntityFrameworkCore;
using static Homelab.Api.Planning;
using FastEndpoints;

namespace Homelab.Api.Features.Planning.GetReview;
public sealed class Endpoint(HomelabDb db) : Endpoint<EmptyRequest, Response>
{
    public override void Configure()
    {
        Get("/api/v1/planning/review");
        AuthSchemes(Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationDefaults.AuthenticationScheme);
        Summary(s => s.Summary = "Get Review");
        Description(b =>
        {
            b.WithName("PlanningGetReview");
            b.WithTags("Planning");
            b.Produces<Response>(200);
            b.Produces(401);
            b.Produces(403);
        }, clearDefaults: true);
    }

    public override async Task HandleAsync(EmptyRequest request, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var settings = await db.Settings.AsNoTracking().SingleOrDefaultAsync() ?? new OperatingSettings();
        var disks = await db.Disks.AsNoTracking().ToListAsync();
        var findings = await db.Findings.AsNoTracking().Where(x => x.State != "resolved" && x.DiskId != null).ToListAsync();
        var plans = await db.Plans.AsNoTracking().Where(x => x.Status != "complete").ToListAsync();
        var reviews = disks.Select(d => Review(d, findings, plans, settings, now)).OfType<DriveReview>().OrderBy(x => x.Priority == "Review media evidence" ? 0 : x.Priority == "Review observed issue" ? 1 : 2).ThenBy(x => x.Serial).ToArray();
        await Send.ResultAsync(Results.Ok(new Response(now, RuleVersion, settings.DriveAgeReviewYears, settings.WarrantyNoticeDays, reviews, disks.Count(x => x.Lifecycle != "retired"), await db.Assets.AsNoTracking().Where(x => x.Type == "spare" && !x.Archived).Select(x => new ResponseSparesItem(x.Id, x.Name, x.Quantity, x.UpdatedUtc, x.CompatibilityNotes, x.Verification, x.EstimatedUnitCost, x.Currency)).ToListAsync(), plans.GroupBy(p => p.Currency).Select(g => new ResponseBudgetsItem(g.Key, g.Sum(p => p.Budget ?? 0), g.Count(p => p.Budget == null), g.Count())))));
        return;
    }
}
