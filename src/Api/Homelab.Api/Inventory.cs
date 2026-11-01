using System.Text.Json;
using Microsoft.EntityFrameworkCore;
namespace Homelab.Api;
public static class InventoryEndpoints
{
    public static string? ValidateAsset(InventoryAsset asset)
    {
        if (string.IsNullOrWhiteSpace(asset.Name) || asset.Name.Length > 200 || asset.Type is not ("host" or "service" or "enclosure" or "controller" or "backup target" or "spare" or "power" or "network") || asset.Criticality is not ("low" or "normal" or "important" or "critical") || asset.Verification is not ("unverified" or "verified") || asset.Quantity is < 0 or > 10000 || asset.EstimatedUnitCost < 0 || asset.Currency is null || asset.Currency.Length != 3 || !asset.Currency.All(char.IsAsciiLetter)) return "Check name, asset type, importance, verification, quantity, and budget.";
        if (!string.IsNullOrWhiteSpace(asset.ManagementUrl) && (!Uri.TryCreate(asset.ManagementUrl, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https") || !string.IsNullOrEmpty(uri.UserInfo))) return "Management links must be HTTP(S) URLs without embedded credentials.";
        foreach (var (label, value, limit) in new (string, string?, int)[] {
            ("Purpose", asset.Purpose, 2000), ("Owner", asset.Owner, 200), ("Location", asset.Location, 500),
            ("Version", asset.Version, 200), ("Management link", asset.ManagementUrl, 2000),
            ("Runbook", asset.Runbook, 50000), ("Documentation", asset.Documentation, 10000),
            ("Compatibility notes", asset.CompatibilityNotes, 5000) })
            if (value is null || value.Length > limit) return $"{label} must be text with at most {limit} characters; leave it empty if unknown.";
        return null;
    }
    public static void MapInventory(this RouteGroupBuilder api)
    {
        api.MapGet("/inventory", async (HomelabDb db) => new { assets = await db.Assets.OrderBy(x => x.Name).ToListAsync(), dependencies = await db.Dependencies.ToListAsync() });
        api.MapPost("/inventory/assets", async (InventoryAsset input, HomelabDb db, HttpContext ctx) => {
            if (ValidateAsset(input) is {} error) return Results.BadRequest(new { error });
            input.Id = Guid.NewGuid(); input.UpdatedUtc = DateTime.UtcNow; db.Assets.Add(input);
            db.Activity.Add(new() { Actor = ctx.User.Identity!.Name!, Action = "Inventory asset created", Detail = input.Name }); await db.SaveChangesAsync(); return Results.Ok(input);
        });
        api.MapPut("/inventory/assets/{id:guid}", async (Guid id, InventoryAsset input, HomelabDb db, HttpContext ctx) => {
            var asset = await db.Assets.FindAsync(id); if (asset is null) return Results.NotFound();
            if (ValidateAsset(input) is {} error) return Results.BadRequest(new { error });
            input.Id = id; input.UpdatedUtc = DateTime.UtcNow;
            db.Activity.Add(new() { Actor = ctx.User.Identity!.Name!, Action = "Inventory asset updated", Detail = JsonSerializer.Serialize(new { id, before = asset, after = input }) });
            db.Entry(asset).CurrentValues.SetValues(input); await db.SaveChangesAsync(); return Results.Ok(asset);
        });
        api.MapPost("/inventory/dependencies", async (AssetDependency input, HomelabDb db, HttpContext ctx) => {
            if (input.Evidence is null || input.Evidence.Length > 10000) return Results.BadRequest(new { error = "Relationship evidence must be text with at most 10000 characters; leave it empty if unknown." });
            if (input.SourceId == input.TargetId || input.Relationship is not ("runs_on" or "stores_on" or "backed_up_to" or "powered_by" or "connected_to") || input.Verification is not ("unverified" or "verified") || input.Verification == "verified" && string.IsNullOrWhiteSpace(input.Evidence)) return Results.BadRequest(new { error = "Choose distinct assets, a supported relationship, and evidence for verified links." });
            if (await db.Assets.CountAsync(a => (a.Id == input.SourceId || a.Id == input.TargetId) && !a.Archived) != 2) return Results.BadRequest(new { error = "Both assets must exist and be active." });
            var existing = await db.Dependencies.SingleOrDefaultAsync(d => d.SourceId == input.SourceId && d.TargetId == input.TargetId && d.Relationship == input.Relationship);
            input.Id = existing?.Id ?? Guid.NewGuid(); input.Author = ctx.User.Identity!.Name!; input.UpdatedUtc = DateTime.UtcNow;
            if (existing is null) db.Dependencies.Add(input); else db.Entry(existing).CurrentValues.SetValues(input);
            db.Activity.Add(new() { Actor = input.Author, Action = "Dependency recorded", Detail = input.Relationship }); await db.SaveChangesAsync(); return Results.Ok(input);
        });
        api.MapDelete("/inventory/dependencies/{id:guid}", async (Guid id, HomelabDb db, HttpContext ctx) => {
            var dependency = await db.Dependencies.FindAsync(id); if (dependency is null) return Results.NotFound();
            db.Activity.Add(new() { Actor = ctx.User.Identity!.Name!, Action = "Dependency removed", Detail = JsonSerializer.Serialize(dependency) }); db.Dependencies.Remove(dependency); await db.SaveChangesAsync(); return Results.NoContent();
        });
        api.MapGet("/inventory/export", async (bool? redacted, HomelabDb db) => Results.File(await InventoryExport.CreateAsync(db, redacted == true), "application/json", $"homelab-inventory-{DateTime.UtcNow:yyyyMMdd}.json"));
    }
}
public static class InventoryExport
{
    public static async Task<byte[]> CreateAsync(HomelabDb db, bool redacted)
    {
        var assets = await db.Assets.ToListAsync(); var disks = await db.Disks.ToListAsync(); var policies = await db.Recovery.ToListAsync();
        var export = new {
            schemaVersion = 1, exportedUtc = DateTime.UtcNow, redacted,
            warning = "Inventory and runbooks only; this is not an application database backup. Encryption keys and integration credentials are excluded. Private exports may contain operator-entered notes; store securely.",
            assets = assets.Select(a => new { a.Id, a.Type, name = redacted ? "redacted" : a.Name, owner = redacted ? null : a.Owner, purpose = redacted ? null : a.Purpose, a.Criticality, location = redacted ? null : a.Location, a.Version, managementUrl = redacted ? null : a.ManagementUrl, documentation = redacted ? null : a.Documentation, runbook = redacted ? null : a.Runbook, a.Verification, a.Quantity, compatibilityNotes = redacted ? null : a.CompatibilityNotes, a.EstimatedUnitCost, a.Currency, a.Archived, a.UpdatedUtc }),
            dependencies = (await db.Dependencies.ToListAsync()).Select(d => new { d.SourceId, d.TargetId, d.Relationship, d.Verification, evidence = redacted ? null : d.Evidence }),
            disks = disks.Select(d => new { d.Id, d.ConnectionId, d.Model, serial = redacted ? null : d.Serial, wwn = redacted ? null : d.Wwn, bay = redacted ? null : d.Bay, bayVerified = d.BayVerified && d.MappingConfidence != "ambiguous", bayPreviouslyVerified = d.BayVerified, d.BayVerifiedUtc, bayEvidence = redacted ? null : d.BayEvidence, d.MappingConfidence, d.LastSeenUtc, d.Present, d.Lifecycle, d.PurchasedUtc, d.PurchaseDateConfidence, d.WarrantyEndUtc, notes = redacted ? null : d.Notes }),
            driveAcquisitions = (await db.DriveAcquisitions.ToListAsync()).Select(x => new { x.DiskId, x.Revision, x.InstalledUtc, x.InstallationConfidence, x.PurchaseCost, x.Currency, x.CostConfidence, x.RecordedUtc,
                installationEvidence = redacted ? null : x.InstallationEvidence, purchaseEvidence = redacted ? null : x.PurchaseEvidence, author = redacted ? null : x.Author }),
            datasetRenameReviews = (await db.DatasetRenames.ToListAsync()).Select(x=>new{x.Id,x.ConnectionId,x.FromSampleId,x.ToSampleId,x.RecordedUtc,x.ReversedUtc,fromName=redacted?null:x.FromName,toName=redacted?null:x.ToName,evidence=redacted?null:x.Evidence,author=redacted?null:x.Author,reversalEvidence=redacted?null:x.ReversalEvidence,reversedBy=redacted?null:x.ReversedBy}),
            discoveryAssignments = (await db.DiscoveryAssignments.ToListAsync()).Select(x=>new{x.Id,x.ConnectionId,x.DiskId,x.RecordedUtc,x.ValidUntilUtc,x.RevokedUtc,wwn=redacted?null:x.Wwn,serial=redacted?null:x.Serial,x.Model,evidence=redacted?null:x.Evidence,author=redacted?null:x.Author,revocationEvidence=redacted?null:x.RevocationEvidence,revokedBy=redacted?null:x.RevokedBy}),
            identityHistoryMerges = (await db.IdentityMerges.ToListAsync()).Select(x=>new{x.Id,x.ConnectionId,x.FirstDiskId,x.SecondDiskId,x.RecordedUtc,x.ReversedUtc,evidence=redacted?null:x.Evidence,author=redacted?null:x.Author,reversalEvidence=redacted?null:x.ReversalEvidence,reversedBy=redacted?null:x.ReversedBy}),
            hardwarePaths = (await db.DrivePaths.ToListAsync()).Select(r=>new {r.Id,r.DiskId,r.AssetId,r.AssetType,r.AssetUpdatedUtc,r.Attached,r.ValidUntilUtc,r.RecordedUtc,assetName=redacted?null:r.AssetName,confirmedIdentity=redacted?null:r.ConfirmedIdentity,evidence=redacted?null:r.Evidence,author=redacted?null:r.Author}),
            spareReviews = (await db.SpareReviews.ToListAsync()).Select(r => new { r.Id, r.DiskId, r.AssetId, r.AssetUpdatedUtc, r.Verdict, r.ValidUntilUtc, r.RecordedUtc,
                confirmedIdentity = redacted ? null : r.ConfirmedIdentity, diskModel = r.DiskModel, assetName = redacted ? null : r.AssetName,
                evidence = redacted ? null : r.Evidence, author = redacted ? null : r.Author }),
            maintenance = (await db.Maintenance.ToListAsync()).Select(m => new { m.Id, m.DiskId, m.OccurredUtc, m.DateConfidence, m.Kind, m.Outcome,
                summary = redacted ? null : m.Summary, evidence = redacted ? null : m.Evidence, confirmedIdentity = redacted ? null : m.ConfirmedIdentity,
                author = redacted ? null : m.Author, m.RecordedUtc, m.SourceSchema, m.VoidedUtc, voidReason = redacted ? null : m.VoidReason, voidedBy = redacted ? null : m.VoidedBy }),
            replacements = (await db.Replacements.ToListAsync()).Select(r => new { r.Id, r.OldDiskId, r.NewDiskId, r.OccurredUtc, r.DateConfidence, poolGuid = redacted ? null : r.PoolGuid, membershipEvidence = redacted ? null : r.MembershipEvidence, reason = redacted ? null : r.Reason, evidence = redacted ? null : r.Evidence, oldIdentity = redacted ? null : r.OldIdentity, newIdentity = redacted ? null : r.NewIdentity, author = redacted ? null : r.Author, r.RecordedUtc, r.VoidedUtc, voidReason = redacted ? null : r.VoidReason, voidedBy = redacted ? null : r.VoidedBy }),
            recovery = policies.Select(p => new { p.Id, p.Revision, destinationCapacity = redacted ? null : p.DestinationCapacity, restoreRequirements = redacted ? null : p.RestoreRequirements, owner = redacted ? null : p.Owner, p.Importance, exclusions = redacted ? null : p.Exclusions, retention = redacted ? null : p.Retention, schedule = redacted ? null : p.Schedule, failureDomains = redacted ? null : p.FailureDomains, asset = redacted ? null : p.Asset, destination = redacted ? null : p.Destination, scope = redacted ? null : p.Scope, p.Mechanism, p.Independence, p.RpoHours, p.RtoHours, runbook = redacted ? null : p.Runbook, keyCustodyReference = redacted ? null : p.KeyCustodyReference })
        };
        return JsonSerializer.SerializeToUtf8Bytes(export, new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
    }
}
