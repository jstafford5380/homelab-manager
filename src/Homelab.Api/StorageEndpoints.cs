using System.Text.Json;
using Microsoft.EntityFrameworkCore;
namespace Homelab.Api;
public static class StorageEndpoints
{
    public static void MapStorage(this RouteGroupBuilder api)
    {
        api.MapCapacity();
        api.MapGet("/connections/{id:guid}/pools/{guid}/scans", async (Guid id, string guid, long? before, HomelabDb db, CancellationToken ct) => {
            if (!await db.Connections.AnyAsync(x => x.Id == id, ct)) return Results.NotFound();
            if (before is <= 0) return Results.BadRequest();
            var rows = await db.PoolScans.AsNoTracking().Where(x => x.ConnectionId == id && x.PoolGuid == guid && (before == null || x.Id < before))
                .OrderByDescending(x => x.Id).Take(51).ToListAsync(ct);
            return Results.Ok(new { items = rows.Take(50), nextBefore = rows.Count > 50 ? (long?)rows[49].Id : null });
        });
        api.MapGet("/connections/{id:guid}/pools/{guid}/history", async (Guid id, string guid, long? before, HomelabDb db) => {
            if (!await db.Connections.AnyAsync(x => x.Id == id)) return Results.NotFound();
            var rows = await db.PoolSnapshots.AsNoTracking().Where(x => x.ConnectionId == id && x.PoolGuid == guid && (before == null || x.Id < before))
                .OrderByDescending(x => x.Id).Take(101).ToListAsync();
            return Results.Ok(new { items = rows.Take(100).Select((row,index) => {
                var result=JsonSerializer.SerializeToNode(row,new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
                result.AsObject().Remove("sourceContextJson"); // Internal identity/timing guard, not a new UI identifier.
                result["vdevComparison"]=JsonSerializer.SerializeToNode(VdevChanges.Compare(row,index+1<rows.Count?rows[index+1]:null),new JsonSerializerOptions(JsonSerializerDefaults.Web));
                return result;
            }), nextBefore = rows.Count > 100 ? (long?)rows[99].Id : null });
        });
        api.MapGet("/connections/{id:guid}/pools/{guid}/daily-history", async (Guid id, string guid, DateTime? beforeDay, long? beforeId, HomelabDb db, BackupService backups) => {
            if (!await db.Connections.AnyAsync(x => x.Id == id)) return Results.NotFound();
            if ((beforeDay is null) != (beforeId is null)) return Results.BadRequest();
            var rows = await db.PoolDailySummaries.AsNoTracking()
                .Where(x => x.ConnectionId == id && x.PoolGuid == guid &&
                    (beforeDay == null || x.DayUtc < beforeDay || x.DayUtc == beforeDay && x.Id < beforeId))
                .OrderByDescending(x => x.DayUtc).ThenByDescending(x => x.Id).Take(51).ToListAsync();
            var days = (await db.Settings.AsNoTracking().SingleOrDefaultAsync() ?? new OperatingSettings()).SampleRetentionDays;
            var cutoff = PoolRetention.DetailCutoff(DateTime.UtcNow, days);
            var pending = await db.PoolSnapshots.CountAsync(x => x.ConnectionId == id && x.PoolGuid == guid && x.FetchedUtc < cutoff);
            return Results.Ok(new { items = rows.Take(50), nextDay = rows.Count > 50 ? (DateTime?)rows[49].DayUtc : null,
                nextId = rows.Count > 50 ? (long?)rows[49].Id : null, detailDays = days, summaryYears = 2,
                backupConfigured = backups.Destination is not null, pendingSnapshots = pending });
        });
        api.MapGet("/findings/{id:guid}", async (Guid id, HomelabDb db) => {
            var finding = await db.Findings.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id);
            if (finding is null) return Results.NotFound();
            var result = System.Text.Json.JsonSerializer.SerializeToNode(finding, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web))!;
            result["resolutionToken"] = FindingLifecycle.ResolutionToken(finding);
            return Results.Ok(result);
        });
        api.MapGet("/storage", async (HomelabDb db) => {
            var capabilities = await db.Capabilities.AsNoTracking().ToListAsync(); var now = DateTime.UtcNow;
            return new { systems = (await db.Connections.AsNoTracking().ToListAsync()).Select(x => new { x.Id, x.Name, x.Status, x.LastSuccessUtc, x.TopologyJson, redundancy = PoolRedundancy.FromStored(x.TopologyJson, x.Enabled && capabilities.Any(c => c.ConnectionId == x.Id && c.Name == "pools" && c.EffectiveStatus(now) == "supported")) }), capabilities = capabilities.Select(x => new { x.ConnectionId, x.Name, status = x.EffectiveStatus(now), x.Detail, x.LastSuccessUtc }), nativeAlerts = await db.NativeAlerts.OrderByDescending(x => x.LastSeenUtc).Take(200).ToListAsync() };
        });
        api.MapGet("/disks/{id:guid}", async (Guid id, HomelabDb db) => {
            var disk = await db.Disks.FindAsync(id); if (disk is null) return Results.NotFound();
            return Results.Ok(new { disk, aliases = await db.DiskAliases.Where(x => x.DiskId == id).OrderByDescending(x => x.FirstSeenUtc).ToListAsync(), annotations = await db.DiskAnnotations.Where(x => x.DiskId == id).OrderByDescending(x => x.Id).Take(100).ToListAsync(), findings = await db.Findings.Where(x => x.DiskId == id).ToListAsync(), plans = await db.Plans.Where(x => x.DiskId == id).ToListAsync() });
        });
        api.MapPut("/disks/{id:guid}/metadata", async (Guid id, DiskMetadata input, HomelabDb db, HttpContext context) => {
            var disk = await db.Disks.FindAsync(id); if (disk is null) return Results.NotFound();
            var error = ValidateMetadata(disk, input); if (error is not null) return Results.BadRequest(new { error });
            var before = JsonSerializer.Serialize(disk);
            disk.Bay = input.Bay; disk.BayVerified = input.BayVerified;
            disk.BayEvidence = input.BayVerified ? input.BayEvidence : null;
            disk.BayVerifiedUtc = input.BayVerified ? DateTime.UtcNow : null;
            disk.PurchasedUtc = input.PurchasedUtc; disk.PurchaseDateConfidence = input.PurchaseDateConfidence;
            disk.WarrantyEndUtc = input.WarrantyEndUtc; disk.Notes = input.Notes; disk.Lifecycle = input.Lifecycle;
            db.DiskAnnotations.Add(new() { DiskId = id, Actor = context.User.Identity!.Name!, BeforeJson = before, AfterJson = JsonSerializer.Serialize(disk) });
            db.Activity.Add(new() { Actor = context.User.Identity.Name!, Action = "Disk metadata updated", Detail = id.ToString() });
            await db.SaveChangesAsync(); return Results.Ok(disk);
        });
    }
    public static string? ValidateMetadata(Disk disk, DiskMetadata input)
    {
        if (input.BayVerified && (string.IsNullOrWhiteSpace(input.Bay) || string.IsNullOrWhiteSpace(input.BayEvidence) || input.ConfirmedSerial != disk.Serial || string.IsNullOrWhiteSpace(disk.Serial) || disk.MappingConfidence == "ambiguous")) return "Bay verification requires unambiguous identity, matching serial confirmation, a bay label, and evidence.";
        if (input.PurchaseDateConfidence is not ("unknown" or "estimated" or "documented") || input.Lifecycle is not ("installed" or "spare" or "retired")) return "Invalid date confidence or lifecycle.";
        if (input.PurchasedUtc is {} purchased && (purchased.Kind != DateTimeKind.Utc || purchased < DateTime.UnixEpoch) ||
            input.WarrantyEndUtc is {} warranty && (warranty.Kind != DateTimeKind.Utc || warranty < DateTime.UnixEpoch)) return "Purchase and warranty dates must include a UTC timezone and be on or after January 1, 1970.";
        if (input.PurchasedUtc > DateTime.UtcNow || input.PurchasedUtc is not null && input.PurchaseDateConfidence == "unknown" || input.PurchasedUtc is null && input.PurchaseDateConfidence != "unknown") return "Specify whether an entered purchase date is estimated or documented.";
        if (input.Notes is null) return "Notes must be text; leave them empty if unknown.";
        if (input.Notes.Length > 10000 || input.Bay?.Length > 100 || input.BayEvidence?.Length > 2000) return "Metadata is too long.";
        return null;
    }
}
public record DiskMetadata(string? Bay, bool BayVerified, string? ConfirmedSerial, string? BayEvidence, DateTime? PurchasedUtc, string PurchaseDateConfidence, DateTime? WarrantyEndUtc, string Notes, string Lifecycle);
