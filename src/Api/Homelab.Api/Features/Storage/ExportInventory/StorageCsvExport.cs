using Microsoft.Data.Sqlite;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Homelab.Api.Features.Storage.ExportInventory;

public static class StorageCsvExport
{
    public static readonly string[] IncludedTables = [
        "Connections", "Disks", "DiskAliases", "DiskAnnotations", "DriveAcquisitions", "Maintenance", "Replacements",
        "DrivePaths", "SpareReviews", "Assets", "DiscoveryAssignments", "IdentityMerges",
        "SmartReports", "SmartReads", "SelfTestEvidence", "Observations", "ApiPowerOnStates",
        "PoolSnapshots", "PoolScans", "PoolDailySummaries", "DatasetSamples", "DatasetRenames",
        "Capabilities", "NativeAlerts", "CollectionRuns", "CollectionDailySummaries", "Findings", "Plans", "PlanChanges"
    ];
    private static string Filter(string table) => table switch {
        "Assets" => " WHERE Id IN (SELECT AssetId FROM DrivePaths UNION SELECT AssetId FROM SpareReviews)",
        "Plans" => " WHERE DiskId IS NOT NULL",
        "PlanChanges" => " WHERE PlanId IN (SELECT Id FROM Plans WHERE DiskId IS NOT NULL)",
        _ => ""
    };
    public static readonly string[] Header = ["record_type", "record_id", "server_id", "drive_id", "name", "model", "serial", "alias", "pool", "capacity_bytes", "location", "notes", "record_json"];
    private static string Quote(string identifier) => "\"" + identifier.Replace("\"", "\"\"") + "\"";
    private sealed record Column(string Name, string Type, int PrimaryKey);
    private sealed record Table(Column[] Columns, long Rows, string Sha256);

    public static async Task<FileStream> CreateAsync(HomelabDb db, DateTime exportedUtc, CancellationToken ct)
    {
        var options = new FileStreamOptions {
            Mode = FileMode.CreateNew, Access = FileAccess.ReadWrite, Share = FileShare.None,
            Options = FileOptions.Asynchronous | FileOptions.DeleteOnClose
        };
        if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        var stream = new FileStream(Path.Combine(Path.GetTempPath(), "homelab-storage-" + Guid.NewGuid().ToString("N")), options);
        try {
            await db.Database.OpenConnectionAsync(ct);
            var connection = (SqliteConnection)db.Database.GetDbConnection();
            // A deferred read transaction gives one WAL snapshot without reserving the writer
            // for the duration of a potentially large historical export.
            await using var snapshot = connection.BeginTransaction(deferred: true);
            await using var transaction = await db.Database.UseTransactionAsync(snapshot, ct)
                ?? throw new InvalidOperationException("Could not establish the export snapshot.");
            var tables = IncludedTables.Order(StringComparer.Ordinal).ToArray();
            var manifest = new SortedDictionary<string, Table>(StringComparer.Ordinal);
            await using (var writer = new StreamWriter(stream, new UTF8Encoding(true), 65536, leaveOpen: true) { NewLine = "\r\n" }) {
                await Row(writer, Header, ct);
                foreach (var table in tables) {
                    var columns = new List<Column>();
                    await using (var schema = connection.CreateCommand()) {
                        schema.Transaction = snapshot;
                        schema.CommandText = "PRAGMA table_info(" + Quote(table) + ")";
                        await using var reader = await schema.ExecuteReaderAsync(ct);
                        while (await reader.ReadAsync(ct)) columns.Add(new(reader.GetString(1), reader.GetString(2), reader.GetInt32(5)));
                    }
                    using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                    long count = 0;
                    await using var command = connection.CreateCommand();
                    command.Transaction = snapshot;
                    var keys = columns.Where(x => x.PrimaryKey > 0).OrderBy(x => x.PrimaryKey).Select(x => x.Name).ToArray();
                    command.CommandText = "SELECT * FROM " + Quote(table) + Filter(table) + " ORDER BY " + string.Join(',', keys.Select(Quote));
                    await using var rows = await command.ExecuteReaderAsync(ct);
                    while (await rows.ReadAsync(ct)) {
                        var record = new SortedDictionary<string, object?>(StringComparer.Ordinal);
                        for (var i = 0; i < rows.FieldCount; i++) {
                            var name = rows.GetName(i);
                            // Integration secrets are never exported, even in encrypted form.
                            record[name] = name.StartsWith("Protected", StringComparison.Ordinal) ?
                                (await rows.IsDBNullAsync(i, ct) ? null : "") : await rows.IsDBNullAsync(i, ct) ? null : rows.GetValue(i);
                        }
                        var json = JsonSerializer.Serialize(record);
                        digest.AppendData(Encoding.UTF8.GetBytes(json + "\n"));
                        string Value(params string[] names) => names.Select(n => record.GetValueOrDefault(n)).FirstOrDefault(v => v is not null)?.ToString() ?? "";
                        await Row(writer, [table, JsonSerializer.Serialize(keys.Select(k => record[k])),
                            table == "Connections" ? Value("Id") : Value("ConnectionId", "ServerId"),
                            table == "Disks" ? Value("Id") : Value("DiskId"), Value("Name", "Nickname", "Title"),
                            Value("Model"), Value("Serial"), Value("Alias"), Value("PoolName", "PoolGuid", "Pool"),
                            Value("SizeBytes", "CapacityBytes"), Value("Bay", "Location"), Value("Notes", "Summary", "Detail"), json], ct);
                        count++;
                    }
                    manifest.Add(table, new(columns.ToArray(), count, Convert.ToHexString(digest.GetHashAndReset())));
                }
                var metadata = JsonSerializer.Serialize(new {
                    format = "homelab-storage-csv", version = 2, scope = "storage", exportedUtc,
                    migrations = await db.Database.GetAppliedMigrationsAsync(ct), tables = manifest,
                    includedTables = IncludedTables,
                    restore = "Use scripts/restore-storage-csv.py with an empty database initialized by the same app version. Credentials are excluded; restored collectors are disabled. This restores application records, not NAS data or ZFS configuration."
                });
                await Row(writer, ["__manifest__", "2", "", "", "Storage inventory export", "", "", "", "", "", "", "", metadata], ct);
                await writer.FlushAsync(ct);
            }
            await transaction.CommitAsync(ct);
            stream.Position = 0;
            return stream;
        } catch { await stream.DisposeAsync(); throw; }
    }

    private static async Task Row(StreamWriter writer, IEnumerable<string> fields, CancellationToken ct)
    {
        // Readable cells are spreadsheet-safe. Exact strings/types remain in record_json,
        // which always begins with an object/array delimiter and is the restoration source.
        var cells = fields.Select(value => {
            if (value.Length > 0 && ("=+-@".Contains(value.TrimStart().FirstOrDefault()) || value[0] is '\t' or '\r' or '\n')) value = "'" + value;
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        });
        await writer.WriteLineAsync(string.Join(',', cells).AsMemory(), ct);
    }
}
