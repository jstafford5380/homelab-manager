using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
namespace Homelab.Api;

public sealed class BackupService(IConfiguration config)
{
    public string? Destination => config["Homelab:BackupPath"] is { Length: > 0 } path ? Path.GetFullPath(path) : null;
    // Caller owns the writer lease. Both scheduler and API use the same serialization boundary.
    public async Task<BackupRecord> CreateAsync(HomelabDb db, CancellationToken ct)
    {
        var destination = Destination ?? throw new InvalidOperationException("Configure Homelab__BackupPath on the server before enabling backups.");
        var dataPath = Path.GetFullPath(config["Homelab:DataPath"] ?? "data");
        if (destination == dataPath || destination.StartsWith(dataPath + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            throw new InvalidOperationException("Choose a backup destination outside the live data directory.");
        Directory.CreateDirectory(destination);
        var record = new BackupRecord();
        record.FileName = $"homelab-{record.CreatedUtc:yyyyMMddTHHmmssfff}-{record.Id:N}.zip";
        var temp = Path.Combine(destination, ".pending-" + record.Id);
        Directory.CreateDirectory(temp);
        try {
            var databaseFile = Path.Combine(temp, "homelab.db");
            var source = (SqliteConnection)db.Database.GetDbConnection();
            if (source.State != System.Data.ConnectionState.Open) await source.OpenAsync(ct);
            using (var target = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = databaseFile, Pooling = false }.ToString())) {
                target.Open(); source.BackupDatabase(target);
                using var check = target.CreateCommand(); check.CommandText = "PRAGMA integrity_check";
                if (!Equals(check.ExecuteScalar(), "ok")) throw new InvalidOperationException("Backup integrity check failed.");
            }
            await using var databaseStream = File.OpenRead(databaseFile);
            var databaseHash = Convert.ToHexString(await SHA256.HashDataAsync(databaseStream, ct));
            await databaseStream.DisposeAsync();
            await File.WriteAllTextAsync(Path.Combine(temp, "manifest.json"), JsonSerializer.Serialize(new {
                formatVersion = 1, createdUtc = record.CreatedUtc, databaseSha256 = databaseHash,
                migrations = await db.Database.GetAppliedMigrationsAsync(ct),
                credentials = "Encrypted references are in the database. Decryption keys must be backed up separately; default restore clears integration credentials and disables collectors.",
                configuration = "Operational settings are in SQLite. Recreate deployment paths, TLS trust, setup token and secret protection configuration on the destination host."
            }, new JsonSerializerOptions { WriteIndented = true }), ct);
            var stagingArchive = Path.Combine(destination, ".pending-" + record.Id + ".zip");
            ZipFile.CreateFromDirectory(temp, stagingArchive);
            var archive = Path.Combine(destination, record.FileName);
            File.Move(stagingArchive, archive);
            record.SizeBytes = new FileInfo(archive).Length;
            await using var stream = File.OpenRead(archive);
            record.Sha256 = Convert.ToHexString(await SHA256.HashDataAsync(stream, ct)); record.Status = "complete";
            db.Backups.Add(record); db.Activity.Add(new() { Action = "Application backup completed", Detail = record.FileName });
            await db.SaveChangesAsync(ct);
            return record;
        } finally {
            Directory.Delete(temp, true);
            var unfinished = Path.Combine(destination, ".pending-" + record.Id + ".zip");
            if (File.Exists(unfinished)) File.Delete(unfinished);
        }
    }
    public async Task RetainAsync(HomelabDb db, OperatingSettings settings, CancellationToken ct)
    {
        if (Destination is not { } destination) return;
        var records = await db.Backups.Where(x => x.Status == "complete").OrderByDescending(x => x.CreatedUtc).ToListAsync(ct);
        var keep = records.GroupBy(x => x.CreatedUtc.Date).Take(settings.BackupDailyCopies).Select(x => x.First().Id).ToHashSet();
        foreach (var record in records.GroupBy(x => System.Globalization.ISOWeek.GetYear(x.CreatedUtc) + "-" + System.Globalization.ISOWeek.GetWeekOfYear(x.CreatedUtc)).Take(settings.BackupWeeklyCopies).Select(x => x.First())) keep.Add(record.Id);
        foreach (var record in records.Where(x => !keep.Contains(x.Id))) {
            if (Path.GetFileName(record.FileName) != record.FileName) continue;
            var file = Path.Combine(destination, record.FileName);
            if (File.Exists(file)) File.Delete(file);
            record.Status = "expired";
        }
        await db.SaveChangesAsync(ct);
    }
}

public sealed class BackupScheduler(IServiceScopeFactory scopes, WriteCoordinator writes, BackupService backups, ILogger<BackupScheduler> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        while (!stop.IsCancellationRequested) {
            try {
                using var scope = scopes.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<HomelabDb>();
                var settings = await db.Settings.AsNoTracking().SingleOrDefaultAsync(stop);
                if (settings?.BackupsEnabled == true) {
                    using var lease = await writes.EnterAsync(stop);
                    var cutoff = DateTime.UtcNow.AddHours(-24);
                    if (!await db.Backups.AnyAsync(x => x.Status == "complete" && x.CreatedUtc > cutoff, stop)) {
                        await backups.CreateAsync(db, stop); await backups.RetainAsync(db, settings, stop);
                    }
                }
            } catch (OperationCanceledException) when (stop.IsCancellationRequested) { break; }
              catch (Exception ex) { logger.LogWarning("Scheduled backup failed ({ErrorType}); last successful backup is unchanged.", ex.GetType().Name); }
            try { await Task.Delay(TimeSpan.FromMinutes(5), stop); } catch (OperationCanceledException) { break; }
        }
    }
}

public static class OperationsEndpoints
{
    public static void MapOperations(this RouteGroupBuilder api)
    {
        api.MapGet("/settings", async (HomelabDb db, BackupService backups) => new { settings = await db.Settings.SingleOrDefaultAsync() ?? new OperatingSettings(), backupDestinationConfigured = backups.Destination is not null });
        api.MapPut("/settings", async (OperatingSettings input, HomelabDb db, BackupService backups) => {
            try { TimeZoneInfo.FindSystemTimeZoneById(input.Timezone); } catch { return Results.BadRequest(new { error = "Choose a valid timezone." }); }
            if (input.DriveAgeReviewYears is < 1 or > 30 || input.WarrantyNoticeDays is < 0 or > 730 || input.CapacityUnits is not ("binary" or "decimal") || input.InventoryMinutes is < 1 or > 1440 || input.SmartMinutes is < 15 or > 10080 || input.SampleRetentionDays is < 7 or > 3650 || input.RawRetentionDays is < 0 or > 90 || input.BackupDailyCopies is < 1 or > 90 || input.BackupWeeklyCopies is < 1 or > 52)
                return Results.BadRequest(new { error = "One or more settings are outside supported limits." });
            if (TemperaturePolicy.Validate(input) is {} policyError) return Results.BadRequest(new { error = policyError });
            if (input.BackupsEnabled && backups.Destination is null) return Results.BadRequest(new { error = "Configure the server backup destination first." });
            input.Id = 1;
            var stored = await db.Settings.FindAsync(1);
            if (stored is null) db.Settings.Add(input); else db.Entry(stored).CurrentValues.SetValues(input);
            db.Activity.Add(new() { Action = "Operating settings changed" }); await db.SaveChangesAsync(); return Results.Ok(input);
        });
        api.MapPut("/connections/{id:guid}/credentials", async (Guid id, CredentialRotation input, HomelabDb db, HttpContext context, Microsoft.AspNetCore.DataProtection.IDataProtectionProvider protection) => {
            if (ConnectionEndpoints.ValidateApiKey(input.ApiKey) is {} error) return Results.BadRequest(new { error });
            var connection = await db.Connections.FindAsync(id); if (connection is null) return Results.NotFound();
            if (input.ConfigurationRevision != connection.ConfigurationRevision)
                return Results.Conflict(new { error = "This connection changed since it was loaded. Reload the saved connection and review it before replacing credentials." });
            connection.ProtectedApiKey = Microsoft.AspNetCore.DataProtection.DataProtectionCommonExtensions.Protect(protection.CreateProtector("nas-credentials"), input.ApiKey);
            connection.ConfigurationRevision = Guid.NewGuid(); connection.NextAttemptUtc = null;
            connection.Enabled = true; connection.Status = "Credentials updated — awaiting collection";
            db.Activity.Add(new() { Actor = context.User.Identity!.Name!, Action = "NAS credential rotated", Detail = connection.Name }); await db.SaveChangesAsync(); return Results.Ok();
        });
        api.MapGet("/backups", async (HomelabDb db) => await db.Backups.OrderByDescending(x => x.CreatedUtc).Take(100).ToListAsync());
        api.MapPost("/backups", async (HomelabDb db, BackupService backups, CancellationToken ct) => {
            try { var record = await backups.CreateAsync(db, ct); await backups.RetainAsync(db, await db.Settings.SingleOrDefaultAsync(ct) ?? new OperatingSettings(), ct); return Results.Ok(record); }
            catch (InvalidOperationException) { return Results.BadRequest(new { error = "Backup unavailable. Check the server backup destination and its separation from live data." }); }
            catch (IOException) { return Results.Problem("Backup could not be written. Check destination availability and permissions."); }
        });
    }
}

public record CredentialRotation(string ApiKey, Guid? ConfigurationRevision = null);
