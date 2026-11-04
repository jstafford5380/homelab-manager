using Microsoft.EntityFrameworkCore;
using static Homelab.Api.Planning;
using FastEndpoints;

namespace Homelab.Api.Features.Planning.GetReview;
public sealed class Endpoint(HomelabDb db) : Endpoint<EmptyRequest, Response>
{
    public override void Configure()
    {
        Get("/api/v1/planning/review");
        Policies(ApiAccess.Read);
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
        var replacedDiskIds = (await db.Replacements.AsNoTracking().Where(x => x.VoidedUtc == null).Select(x => x.OldDiskId).ToListAsync(ct)).ToHashSet();
        // Match Storage: an absent drive with a recorded replacement is historical,
        // not missing work. A present drive or a voided replacement still needs review.
        var activeDisks = disks.Where(d => d.Present || !replacedDiskIds.Contains(d.Id)).ToArray();
        var reviews = activeDisks.Select(d => Review(d, findings, plans, settings, now)).OfType<DriveReview>().OrderBy(x => x.Priority == "Review media evidence" ? 0 : x.Priority == "Review observed issue" ? 1 : 2).ThenBy(x => x.Serial).ToArray();
        var assets = await db.Assets.AsNoTracking().Where(x => x.Type == "spare" && !x.Archived).ToListAsync(ct);
        var assetMap = assets.ToDictionary(x => x.Id);
        var diskMap = disks.ToDictionary(x => x.Id);
        // Only the latest assessment for each drive/spare pair can establish compatibility.
        var latestIds = db.SpareReviews.GroupBy(x => new { x.DiskId, x.AssetId }).Select(g => g.Max(x => x.Id));
        var checks = await db.SpareReviews.AsNoTracking().Where(x => latestIds.Contains(x.Id)).ToListAsync(ct);
        var assessed = checks.Where(x => diskMap.ContainsKey(x.DiskId) && assetMap.ContainsKey(x.AssetId))
            .Select(x => new { Review = x, Status = SpareCompatibility.Status(x, diskMap[x.DiskId], assetMap[x.AssetId], now) }).ToArray();
        reviews = reviews.Select(r => {
            var current = assessed.Where(x => x.Review.DiskId == r.DiskId)
                .OrderBy(x => x.Status == "reviewed compatible" ? 0 : 1).ThenByDescending(x => x.Review.Id).FirstOrDefault();
            var disk = diskMap[r.DiskId];
            return r with {
                Location = string.IsNullOrWhiteSpace(disk.Bay) ? null : (disk.Present ? "" : "Last seen in ") + disk.Bay,
                SpareFit = current is null ? new("unchecked", null, null, null) : new(
                    current.Status == "reviewed compatible" ? "compatible" : current.Status == "review expired" ? "expired" : current.Status == "incompatible" ? "incompatible" : "unchecked",
                    current.Review.AssetId, current.Review.AssetName, current.Review.ValidUntilUtc)
            };
        }).ToArray();
        await Send.ResultAsync(Results.Ok(new Response(now, RuleVersion, settings.DriveAgeReviewYears, settings.WarrantyNoticeDays, reviews, activeDisks.Count(x => x.Lifecycle != "retired"),
            assets.Select(x => new ResponseSparesItem(x.Id, x.Name, x.Quantity, x.UpdatedUtc, x.CompatibilityNotes, x.Verification, x.EstimatedUnitCost, x.Currency,
                assessed.Count(a => a.Review.AssetId == x.Id && a.Status == "reviewed compatible"))).ToList(),
            plans.GroupBy(p => p.Currency).Select(g => new ResponseBudgetsItem(g.Key, g.Sum(p => p.Budget ?? 0), g.Count(p => p.Budget == null), g.Count(),
                g.Where(p => p.Status == "planned").Sum(p => p.Budget ?? 0), g.Where(p => p.Status == "ordered").Sum(p => p.Budget ?? 0))))));
        return;
    }
}
