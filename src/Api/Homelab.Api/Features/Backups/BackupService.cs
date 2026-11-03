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
        try
        {
            var databaseFile = Path.Combine(temp, "homelab.db");
            var source = (SqliteConnection)db.Database.GetDbConnection();
            if (source.State != System.Data.ConnectionState.Open)
                await source.OpenAsync(ct);
            using (var target = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = databaseFile, Pooling = false }.ToString()))
            {
                target.Open();
                source.BackupDatabase(target);
                using var check = target.CreateCommand();
                check.CommandText = "PRAGMA integrity_check";
                if (!Equals(check.ExecuteScalar(), "ok"))
                    throw new InvalidOperationException("Backup integrity check failed.");
            }

            await using var databaseStream = File.OpenRead(databaseFile);
            var databaseHash = Convert.ToHexString(await SHA256.HashDataAsync(databaseStream, ct));
            await databaseStream.DisposeAsync();
            await File.WriteAllTextAsync(Path.Combine(temp, "manifest.json"), JsonSerializer.Serialize(new { formatVersion = 1, createdUtc = record.CreatedUtc, databaseSha256 = databaseHash, migrations = await db.Database.GetAppliedMigrationsAsync(ct), credentials = "Encrypted references are in the database. Decryption keys must be backed up separately; default restore clears integration credentials and disables collectors.", configuration = "Operational settings are in SQLite. Recreate deployment paths, TLS trust, setup token and secret protection configuration on the destination host." }, new JsonSerializerOptions { WriteIndented = true }), ct);
            var stagingArchive = Path.Combine(destination, ".pending-" + record.Id + ".zip");
            ZipFile.CreateFromDirectory(temp, stagingArchive);
            var archive = Path.Combine(destination, record.FileName);
            File.Move(stagingArchive, archive);
            record.SizeBytes = new FileInfo(archive).Length;
            await using var stream = File.OpenRead(archive);
            record.Sha256 = Convert.ToHexString(await SHA256.HashDataAsync(stream, ct));
            record.Status = "complete";
            db.Backups.Add(record);
            db.Activity.Add(new() { Action = "Application backup completed", Detail = record.FileName });
            await db.SaveChangesAsync(ct);
            return record;
        }
        finally
        {
            Directory.Delete(temp, true);
            var unfinished = Path.Combine(destination, ".pending-" + record.Id + ".zip");
            if (File.Exists(unfinished))
                File.Delete(unfinished);
        }
    }

    public async Task RetainAsync(HomelabDb db, OperatingSettings settings, CancellationToken ct)
    {
        if (Destination is not { } destination)
            return;
        var records = await db.Backups.Where(x => x.Status == "complete").OrderByDescending(x => x.CreatedUtc).ToListAsync(ct);
        var keep = records.GroupBy(x => x.CreatedUtc.Date).Take(settings.BackupDailyCopies).Select(x => x.First().Id).ToHashSet();
        foreach (var record in records.GroupBy(x => System.Globalization.ISOWeek.GetYear(x.CreatedUtc) + "-" + System.Globalization.ISOWeek.GetWeekOfYear(x.CreatedUtc)).Take(settings.BackupWeeklyCopies).Select(x => x.First()))
            keep.Add(record.Id);
        foreach (var record in records.Where(x => !keep.Contains(x.Id)))
        {
            if (Path.GetFileName(record.FileName) != record.FileName)
                continue;
            var file = Path.Combine(destination, record.FileName);
            if (File.Exists(file))
                File.Delete(file);
            record.Status = "expired";
        }

        await db.SaveChangesAsync(ct);
    }
}
