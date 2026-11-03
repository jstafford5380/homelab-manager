using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Homelab.Api;
using Homelab.Api.Features.Storage.ExportInventory;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualBasic.FileIO;

namespace Homelab.Tests;

public class StorageCsvExportTests
{
    [Fact]
    public async Task CsvRestoresCompleteRecordsAndRelationshipsWithoutCredentialsOrDataLoss()
    {
        var folder = Path.Combine(Path.GetTempPath(), "storage-csv-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try {
            var path = Path.Combine(folder, "schema.db");
            await using var db = Open(path);
            await db.Database.MigrateAsync();
            const string secret = "SECRET-MUST-NEVER-BE-EXPORTED";
            var nas = new NasConnection { Name = "NAS, \"media\"\n日本語", ProtectedApiKey = secret, TopologyJson = "[{\"name\":\"Main\"}]", DatasetsJson = "{\"unknown\":null,\"used\":0}" };
            var otherNas = new NasConnection { Name = "Second NAS", ProtectedApiKey = secret };
            var disk = new Disk { ConnectionId = nas.Id, IdentityKey = "stable-key", Nickname = "=1+2", Serial = "S,\"1\"\nα", Model = "WD", Notes = "First line, \"quoted\"\r\nSecond line 🗄", Bay = "Bay 3", BayVerified = true, SizeBytes = 18000000000000, PurchasedUtc = DateTime.UtcNow.AddYears(-2) };
            var spare = new Disk { ConnectionId = otherNas.Id, IdentityKey = "spare-key", Serial = "SPARE", Model = "SSD", Present = false, Lifecycle = "retired" };
            var acquisition = new DriveAcquisition { DiskId = disk.Id, PurchaseCost = 123.45m, Currency = "USD", InstallationConfidence = "estimated", RecordedUtc = DateTime.UtcNow };
            db.Connections.AddRange(nas, otherNas); db.Disks.AddRange(disk, spare);
            db.DriveAcquisitions.Add(acquisition);
            db.Maintenance.Add(new() { DiskId = disk.Id, RequestId = Guid.NewGuid(), Summary = "Owner's note\nMore text", OccurredUtc = DateTime.UtcNow, Author = "original-owner" });
            db.DiskAnnotations.Add(new() { DiskId = disk.Id, BeforeJson = "{\"bay\":null}", AfterJson = "{\"bay\":\"Bay 3\"}" });
            db.Replacements.Add(new() { OldDiskId = spare.Id, NewDiskId = disk.Id, Reason = "Historical replacement", OccurredUtc = DateTime.UtcNow });
            var storagePlan = new PlanningItem { DiskId = disk.Id, Title = "Replacement plan", Budget = 250.25m };
            var unrelatedPlan = new PlanningItem { Title = "EXCLUDED-GENERAL-PLAN" };
            db.Plans.AddRange(storagePlan, unrelatedPlan);
            db.PlanChanges.AddRange(new() { PlanId = storagePlan.Id, AfterJson = "{}" }, new() { PlanId = unrelatedPlan.Id, AfterJson = "EXCLUDED-GENERAL-PLAN-HISTORY" });
            var enclosure = new InventoryAsset { Name = "Storage enclosure", Type = "enclosure" };
            db.Assets.AddRange(enclosure, new InventoryAsset { Name = "EXCLUDED-GENERAL-ASSET" });
            db.DrivePaths.Add(new() { DiskId = disk.Id, AssetId = enclosure.Id, RequestId = Guid.NewGuid(), AssetName = enclosure.Name });
            db.Recovery.Add(new() { Asset = "EXCLUDED-RECOVERY" });
            db.Activity.Add(new() { Detail = "EXCLUDED-ACTIVITY" });
            db.PoolSnapshots.Add(new() { ConnectionId = nas.Id, PoolGuid = "pool", Name = "Main", SizeBytes = 100000000000000, FetchedUtc = DateTime.UtcNow, VdevEvidenceJson = "{\"nested\":[1,null]}" });
            db.DatasetSamples.Add(new() { ConnectionId = nas.Id, ConfigurationRevision = nas.ConfigurationRevision, SnapshotJson = "{\"datasets\":[{\"name\":\"Main/photos\",\"used\":0}]}" });
            db.ApiPowerOnStates.Add(new() { ConnectionId = nas.Id, SnapshotJson = "{\"hours\":12345}" });
            db.SmartReads.Add(new() { DiskId = disk.Id, ReportJson = "{\"power_on_hours\":12345}", ReadUtc = DateTime.UtcNow });
            db.Settings.Add(new() { Timezone = "EXCLUDED-SETTINGS", BackupsEnabled = true, ReadApiPowerOnHours = true });
            db.HardwareServers.Add(new() { ConnectionId = nas.Id, Name = "EXCLUDED-HARDWARE", ProtectedCommunity = secret, Connected = true });
            db.Owners.Add(new() { Username = "owner", PasswordHash = secret });
            db.NotificationChannels.Add(new() { ProtectedUrl = secret });
            await db.SaveChangesAsync();
            var archive = Path.Combine(folder, "inventory.csv");
            await using (var output = File.Create(archive)) {
                await using var export = await StorageCsvExport.CreateAsync(db, DateTime.UtcNow, default);
                await export.CopyToAsync(output);
            }
            var csv = await File.ReadAllTextAsync(archive);
            Assert.DoesNotContain(secret, csv);
            Assert.DoesNotContain("EXCLUDED-", csv);
            using (var reader = new TextFieldParser(archive, Encoding.UTF8) { HasFieldsEnclosedInQuotes = true, TrimWhiteSpace = false }) {
                reader.SetDelimiters(",");
                Assert.Equal(StorageCsvExport.Header, reader.ReadFields());
                var records = new List<string[]>();
                while (!reader.EndOfData) records.Add(reader.ReadFields()!);
                Assert.All(records, row => Assert.Equal(StorageCsvExport.Header.Length, row.Length));
                var drive = records.Single(row => row[0] == "Disks" && row[4] == "'=1+2");
                Assert.Equal(disk.Notes, drive[11]);
                Assert.Equal("=1+2", JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(drive[^1])!["Nickname"].GetString());
                var metadataRow = Assert.Single(records, row => row[0] == "__manifest__");
                var metadata = JsonSerializer.Deserialize<JsonElement>(metadataRow[^1]);
                Assert.Equal("storage", metadata.GetProperty("scope").GetString());
                Assert.Equal(StorageCsvExport.IncludedTables.Order(), metadata.GetProperty("tables").EnumerateObject().Select(x => x.Name).Order());
                Assert.DoesNotContain(records, row => new[] { "HardwareServers", "Recovery", "Settings", "Activity" }.Contains(row[0]));
                Assert.Equal(2, records.Count(row => row[0] == "Connections"));
            }
            var destination = Path.Combine(folder, "restored");
            var restored = await Restore(archive, path, destination);
            Assert.True(restored.ExitCode == 0, restored.Error);
            await using var target = Open(Path.Combine(destination, "homelab.db"));
            Assert.Equal(JsonSerializer.Serialize(disk), JsonSerializer.Serialize(await target.Disks.SingleAsync(x => x.Id == disk.Id)));
            Assert.Equal(JsonSerializer.Serialize(spare), JsonSerializer.Serialize(await target.Disks.SingleAsync(x => x.Id == spare.Id)));
            Assert.Equal(JsonSerializer.Serialize(acquisition), JsonSerializer.Serialize(await target.DriveAcquisitions.SingleAsync()));
            Assert.Equal((await db.Maintenance.SingleAsync()).Summary, (await target.Maintenance.SingleAsync()).Summary);
            Assert.Equal((await db.PoolSnapshots.SingleAsync()).VdevEvidenceJson, (await target.PoolSnapshots.SingleAsync()).VdevEvidenceJson);
            Assert.Equal((await db.DatasetSamples.SingleAsync()).SnapshotJson, (await target.DatasetSamples.SingleAsync()).SnapshotJson);
            Assert.Equal((await db.SmartReads.SingleAsync()).ReportJson, (await target.SmartReads.SingleAsync()).ReportJson);
            Assert.Equal((await db.ApiPowerOnStates.SingleAsync()).SnapshotJson, (await target.ApiPowerOnStates.SingleAsync()).SnapshotJson);
            Assert.Equal((await db.Replacements.SingleAsync()).OldDiskId, (await target.Replacements.SingleAsync()).OldDiskId);
            Assert.Equal(storagePlan.Budget, (await target.Plans.SingleAsync()).Budget);
            Assert.Empty(await target.Owners.ToArrayAsync()); Assert.Empty(await target.NotificationChannels.ToArrayAsync());
            Assert.All(await target.Connections.ToArrayAsync(), c => { Assert.False(c.Enabled); Assert.Equal("", c.ProtectedApiKey); });
            Assert.Empty(await target.Settings.ToArrayAsync());
            Assert.Empty(await target.HardwareServers.ToArrayAsync());
            Assert.Empty(await target.Recovery.ToArrayAsync());
            Assert.Empty(await target.Activity.ToArrayAsync());
            Assert.Equal(enclosure.Id, (await target.Assets.SingleAsync()).Id);
            Assert.Equal(enclosure.Id, (await target.DrivePaths.SingleAsync()).AssetId);
            Assert.Equal(storagePlan.Id, (await target.PlanChanges.SingleAsync()).PlanId);
            // Compare every column of every exported record, apart from intentional restore safety changes.
            await using (var exportedAgain = await StorageCsvExport.CreateAsync(target, DateTime.UtcNow, default)) {
                using var reader = new StreamReader(exportedAgain, Encoding.UTF8, leaveOpen: true);
                Assert.Equal(CanonicalRecords(csv), CanonicalRecords(await reader.ReadToEndAsync()));
            }
            // A second restore cannot overwrite a directory, and damaged exports never create a target.
            Assert.NotEqual(0, (await Restore(archive, path, destination)).ExitCode);
            var broken = Path.Combine(folder, "broken.csv");
            await File.WriteAllTextAsync(broken, csv.Replace("Historical replacement", "Modified replacement"), new UTF8Encoding(true));
            var rejectedDestination = Path.Combine(folder, "rejected");
            Assert.NotEqual(0, (await Restore(broken, path, rejectedDestination)).ExitCode);
            Assert.False(Directory.Exists(rejectedDestination));
        } finally { SqliteConnection.ClearAllPools(); Directory.Delete(folder, true); }
    }
    private static string[] CanonicalRecords(string csv)
    {
        using var parser = new TextFieldParser(new StringReader(csv)) { HasFieldsEnclosedInQuotes = true, TrimWhiteSpace = false };
        parser.SetDelimiters(","); parser.ReadFields();
        var records = new List<string>();
        while (!parser.EndOfData) {
            var fields = parser.ReadFields()!;
            if (fields[0] == "__manifest__") continue;
            var data = JsonNode.Parse(fields[^1])!.AsObject();
            var safetyFields = fields[0] switch {
                "Connections" => new[] { "Enabled", "NextAttemptUtc", "Status" },
                "HardwareServers" => ["ProtectedCommunity", "Connected", "NextAttemptUtc"],
                "Settings" => ["BackupsEnabled"], _ => Array.Empty<string>()
            };
            foreach (var field in safetyFields) data.Remove(field);
            records.Add(fields[0] + ":" + fields[1] + ":" + data.ToJsonString());
        }
        return records.Order(StringComparer.Ordinal).ToArray();
    }
    private static HomelabDb Open(string path) => new(new DbContextOptionsBuilder<HomelabDb>().UseSqlite($"Data Source={path};Pooling=False").Options);
    private static async Task<(int ExitCode, string Error)> Restore(string archive, string schema, string destination)
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "scripts", "restore-storage-csv.py"))) root = root.Parent;
        Assert.NotNull(root);
        var start = new ProcessStartInfo("python3") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        foreach (var argument in new[] { Path.Combine(root!.FullName, "scripts", "restore-storage-csv.py"), archive, destination, "--schema", schema }) start.ArgumentList.Add(argument);
        using var process = Process.Start(start)!;
        var error = process.StandardError.ReadToEndAsync(); var output = process.StandardOutput.ReadToEndAsync();
        await process.WaitForExitAsync(); await output;
        return (process.ExitCode, await error);
    }
}

public class StorageCsvHttpTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task DownloadRequiresAuthenticationAndReturnsACsvAttachment()
    {
        using var client = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/storage/export.csv")).StatusCode);
        client.DefaultRequestHeaders.Add("X-Homelab-Request", "1");
        var credentials = new { username = "csv-owner", password = "csv-test-password", setupToken = "refactor-test" };
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/v1/setup", credentials)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/v1/login", credentials)).StatusCode);
        var result = await client.GetAsync("/api/v1/storage/export.csv");
        Assert.Equal(HttpStatusCode.OK, result.StatusCode);
        Assert.Equal("text/csv", result.Content.Headers.ContentType!.MediaType);
        Assert.Equal("attachment", result.Content.Headers.ContentDisposition!.DispositionType);
        Assert.Contains(".csv", result.Content.Headers.ContentDisposition.FileName);
        var body = await result.Content.ReadAsStringAsync();
        Assert.Contains("__manifest__", body); Assert.Contains("record_json", body);
        Assert.DoesNotContain("csv-test-password", body);
    }
}
