using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Homelab.Api;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Homelab.Tests;

public sealed class ObservationApiTests
{
    [Fact] public async Task AuthenticatedImportOwnsProvenanceAndRejectsForgeryInvalidDataAndDuplicates()
    {
        var folder = Path.Combine(Path.GetTempPath(), "homelab-api-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var nas = new NasConnection { Enabled = false, Name = "Offline fixture", SystemSerial = "NAS-A", ObservedSystemSerial = "NAS-B", SystemIdentityState = "changed", CachedTemperaturesJson = JsonSerializer.Serialize(new CachedTemperatureSnapshot(DateTime.UtcNow, [new("sda", 32, null), new("sdb", null, "No temperature in the cache.")])) };
        nas.TopologyJson = new JsonArray(PoolRedundancyTests.Pool()).ToJsonString();
        var disk = new Disk { ConnectionId = nas.Id, Serial = "FIXTURE-ONLY", Model = "model", IdentityKey = "fixture" };
        var otherDisk = new Disk { ConnectionId = nas.Id, Serial = "OTHER", Model = "model", IdentityKey = "other" };
        Process? process = null;
        var processStarted = false;
        try {
            var options = new DbContextOptionsBuilder<HomelabDb>().UseSqlite(new SqliteConnectionStringBuilder { DataSource = Path.Combine(folder, "homelab.db"), Pooling = false }.ToString()).Options;
            await using (var db = new HomelabDb(options)) {
                await db.Database.MigrateAsync(); db.Connections.Add(nas); db.Disks.AddRange(disk, otherDisk);
                for(var i=0;i<12;i++){var at=DateTime.UtcNow.AddHours(-i-1);db.DatasetSamples.Add(new(){ConnectionId=nas.Id,ConfigurationRevision=nas.ConfigurationRevision,HourUtc=at,FetchedUtc=at,SystemSerial="fixture-source",SnapshotJson=JsonSerializer.Serialize(new DatasetSnapshot(at,[new(i==11?"tank/old":"tank/photos","tank","FILESYSTEM",i,null,0,0,null)]))});}

                for (var i = 0; i < 51; i++) db.PoolDailySummaries.Add(new() { ConnectionId = nas.Id, PoolGuid = "pool", GroupKey = $"fixture-{i}", DayUtc = DateTime.UtcNow.Date.AddDays(-100 - i % 2), SampleCount = i + 1 });
                db.PoolDailySummaries.Add(new() { ConnectionId = nas.Id, PoolGuid = "other-pool", GroupKey = "excluded", DayUtc = DateTime.UtcNow.Date });
                var otherNas = new NasConnection { Enabled = false }; db.Connections.Add(otherNas);
                for (var i = 0; i < 51; i++) {
                    db.CollectionRuns.Add(new() { ConnectionId = nas.Id, StartedUtc = DateTime.UtcNow.AddMinutes(-i), EndedUtc = DateTime.UtcNow.AddMinutes(-i), Result = "partial" });
                    db.CollectionDailySummaries.Add(new() { ConnectionId = nas.Id, GroupKey = $"collection-{i}", DayUtc = DateTime.UtcNow.Date.AddDays(-100 - i % 2), Result = "partial", AttemptCount = i + 1 });
                }
                db.CollectionRuns.Add(new() { ConnectionId = otherNas.Id, Result = "excluded" });
                db.CollectionDailySummaries.Add(new() { ConnectionId = otherNas.Id, GroupKey = "other-nas", DayUtc = DateTime.UtcNow.Date, Result = "excluded" });
                for (var day = 0; day <= 30; day++) db.PoolSnapshots.Add(new() { ConnectionId = nas.Id, PoolGuid = "capacity-pool", FetchedUtc = DateTime.UtcNow.AddDays(day-30), SizeBytes = 1000000, AllocatedBytes = 100000 + day * 10000, Status = "ONLINE", TopologySignature = "fixture" });
                db.PoolSnapshots.Add(new() { ConnectionId = otherNas.Id, PoolGuid = "capacity-pool", FetchedUtc = DateTime.UtcNow, Status = "OFFLINE" });
                var memberStart=DateTime.UtcNow.AddHours(-2);
                for(var i=0;i<101;i++) {
                    var at=memberStart.AddMinutes(i);
                    db.PoolSnapshots.Add(new() {ConnectionId=nas.Id,PoolGuid="member-pool",FetchedUtc=at.AddSeconds(2),
                        SourceContextJson=VdevChanges.CaptureContext(JsonSerializer.SerializeToElement(new {system_serial="NAS-A",version="24.10.2",uptime_seconds=10000+i*60}),nas.ConfigurationRevision,at,at.AddSeconds(1)),
                        VdevEvidenceJson=VdevEvidence.Capture(JsonSerializer.SerializeToElement(new {topology=new {data=new[]{new {guid="member-guid",type="DISK",stats=new {read_errors=i}}}}}))});
                }
                var scanSnapshot = new PoolSnapshot { ConnectionId=nas.Id, PoolGuid="scan-pool", Name="Scan fixture", FetchedUtc=DateTime.UtcNow, ScanFunction="RESILVER", ScanState="SCANNING", ScanPercentage=45.5, ScanStartedUtc=DateTime.UtcNow.AddHours(-1) };
                db.PoolSnapshots.Add(scanSnapshot); await PoolScanHistory.RecordAsync(db,scanSnapshot);
                for (var i=0;i<51;i++) await PoolScanHistory.RecordAsync(db,new PoolSnapshot { ConnectionId=nas.Id, PoolGuid="scan-pool", FetchedUtc=DateTime.UtcNow,
                    ScanFunction="SCRUB",ScanState="FINISHED",ScanStartedUtc=DateTime.UtcNow.AddDays(-i-1) });
                await PoolScanHistory.RecordAsync(db,new PoolSnapshot { ConnectionId=otherNas.Id, PoolGuid="scan-pool", FetchedUtc=DateTime.UtcNow,
                    ScanFunction="SCRUB",ScanState="FINISHED",ScanStartedUtc=DateTime.UtcNow.AddDays(-1),SourceSchema="other-nas" });
                await db.SaveChangesAsync();
            }
            var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start(); var port = ((IPEndPoint)listener.LocalEndpoint).Port; listener.Stop();
            var address = new Uri($"http://127.0.0.1:{port}");
            var root = FindRoot();
#if DEBUG
            const string configuration = "Debug";
#else
            const string configuration = "Release";
#endif
            var binary = Path.Combine(root, "Homelab.Api", "bin", configuration, "net10.0", "Homelab.Api.dll");
            Assert.True(File.Exists(binary), "Build the API project before running its process-level tests.");
            var start = new ProcessStartInfo("dotnet") { WorkingDirectory = Path.GetDirectoryName(binary)!, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
            start.ArgumentList.Add(binary); start.ArgumentList.Add("--urls"); start.ArgumentList.Add(address.ToString());
            foreach (var key in start.Environment.Keys.Where(k => k.StartsWith("Homelab__", StringComparison.OrdinalIgnoreCase) || k.StartsWith("OTEL_", StringComparison.OrdinalIgnoreCase)).ToArray()) start.Environment.Remove(key);
            start.Environment["ASPNETCORE_ENVIRONMENT"] = "Development";
            start.Environment["Homelab__DataPath"] = folder;
            start.Environment["Homelab__SetupToken"] = "isolated-api-fixture-token";
            start.Environment["OTEL_SDK_DISABLED"] = "true";
            var logs = new StringBuilder();
            process = new Process { StartInfo = start };
            DataReceivedEventHandler collectLog = (_, e) => { if (e.Data is not null) lock (logs) { if (logs.Length < 50000) logs.AppendLine(e.Data); } };
            process.OutputDataReceived += collectLog; process.ErrorDataReceived += collectLog;
            processStarted = process.Start(); Assert.True(processStarted); process.BeginOutputReadLine(); process.BeginErrorReadLine();
            using var client = new HttpClient(new HttpClientHandler { CookieContainer = new(), AllowAutoRedirect = false, UseProxy = false }) { BaseAddress = address, Timeout = TimeSpan.FromSeconds(3) };
            var ready = false;
            var startupWatch = Stopwatch.StartNew();
            while (startupWatch.Elapsed < TimeSpan.FromSeconds(15) && !process.HasExited) {
                try { using var response = await client.GetAsync("/health/ready"); if (response.IsSuccessStatusCode) { ready = true; break; } }
                catch (HttpRequestException) { }
                catch (TaskCanceledException) { }
                await Task.Delay(100);
            }
            string startupLog; lock (logs) startupLog = logs.ToString();
            Assert.True(ready, "Isolated API did not start: " + startupLog);
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync($"/api/v1/disks/{disk.Id}/acquisition")).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized,(await client.GetAsync($"/api/v1/connections/{nas.Id}/dataset-renames")).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized,(await client.GetAsync($"/api/v1/connections/{nas.Id}/dataset-history")).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized,(await client.GetAsync("/api/v1/activity/history")).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync($"/api/v1/disks/{disk.Id}/observations")).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync($"/api/v1/disks/{disk.Id}/observation-history?metric=temperature&days=7")).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync($"/api/v1/disks/{disk.Id}/maintenance")).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync($"/api/v1/connections/{nas.Id}/pools/pool/daily-history")).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync($"/api/v1/connections/{nas.Id}/collection-history")).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync($"/api/v1/connections/{nas.Id}/collection-daily-history")).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync($"/api/v1/connections/{nas.Id}/cached-temperatures")).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync($"/api/v1/connections/{nas.Id}/datasets")).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync($"/api/v1/disks/{disk.Id}/smart-reports")).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync($"/api/v1/disks/{disk.Id}/smart-reports/history")).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync($"/api/v1/connections/{nas.Id}/pools/capacity-pool/capacity")).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync($"/api/v1/disks/{disk.Id}/evidence-coverage")).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync($"/api/v1/connections/{nas.Id}/pools/scan-pool/scans")).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync($"/api/v1/disks/{disk.Id}/spare-reviews")).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized,(await client.GetAsync($"/api/v1/disks/{disk.Id}/hardware-paths")).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized,(await client.GetAsync($"/api/v1/disks/{disk.Id}/identity-history")).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized,(await client.GetAsync($"/api/v1/connections/{nas.Id}/identity-review")).StatusCode);
            var credentials = new { username = "evidence-owner", password = "fixture-9", setupToken = "isolated-api-fixture-token" };
            Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/api/v1/setup", credentials)).StatusCode);
            client.DefaultRequestHeaders.Add("X-Homelab-Request", "1");
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsync("/api/v1/storage/power-on-hours/run", null)).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/v1/setup", new { username = credentials.username, password = "12345678", setupToken = credentials.setupToken })).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/v1/setup", credentials)).StatusCode);
            Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync("/api/v1/setup", credentials)).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/v1/login", new { username = credentials.username, password = "wrong" })).StatusCode);
            var login = await client.PostAsJsonAsync("/api/v1/login", credentials);
            Assert.Equal(HttpStatusCode.OK, login.StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync("/api/v1/storage/power-on-hours/run", null)).StatusCode);
            client.DefaultRequestHeaders.Remove("X-Homelab-Request");
            Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsync("/api/v1/storage/power-on-hours/run", null)).StatusCode);
            client.DefaultRequestHeaders.Add("X-Homelab-Request", "1");
            var cookie = string.Join(";", login.Headers.GetValues("Set-Cookie"));
            Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase); Assert.Contains("samesite=strict", cookie, StringComparison.OrdinalIgnoreCase);
            using (var rsa = System.Security.Cryptography.RSA.Create(2048)) {
                var request = new System.Security.Cryptography.X509Certificates.CertificateRequest("CN=Fixture CA", rsa, System.Security.Cryptography.HashAlgorithmName.SHA256, System.Security.Cryptography.RSASignaturePadding.Pkcs1);
                using var cert = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddDays(1));
                var pem = cert.ExportCertificatePem();
                var input = new { name="CA enrollment fixture", apiUri="wss://example.invalid/websocket", apiKey="fixture-key", trustedCaPem=pem };
                Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/v1/connections", new { name=input.name, apiUri=input.apiUri, apiKey=input.apiKey, trustedCaPem="-----BEGIN PRIVATE KEY-----" })).StatusCode);
                var response = await client.PostAsJsonAsync("/api/v1/connections", input);
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                var created = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
                await using var check = new HomelabDb(options);
                var enrolled = await check.Connections.SingleAsync(x => x.Id == created);
                Assert.Equal(pem, enrolled.TrustedCaPem);
                check.Connections.Remove(enrolled); await check.SaveChangesAsync();
            }
            foreach (var testPath in new[] { "/api/v1/notifications/test", "/api/v1/notifications/test/" })
                Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync(testPath, new { }).WaitAsync(TimeSpan.FromSeconds(5))).StatusCode);
            var telegramInput = new ChannelInput(null, false, true, null, "telegram", "123456:abcdefghijklmnopqrstuvwxyz", "-100123456");
            Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync("/api/v1/notifications", telegramInput with { ChatId = "" })).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync("/api/v1/notifications", telegramInput)).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync("/api/v1/notifications", telegramInput with { QuietStart = "22:00" })).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync("/api/v1/notifications", telegramInput with { QuietStart = "22:00", QuietEnd = "22:00" })).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync("/api/v1/notifications", telegramInput with { QuietStart = "22:00", QuietEnd = "07:00" })).StatusCode);
            var telegramSettings = await client.GetStringAsync("/api/v1/notifications");
            Assert.Contains("22:00", telegramSettings); Assert.Contains("07:00", telegramSettings);
            Assert.Contains("telegram", telegramSettings);
            Assert.DoesNotContain(telegramInput.BotToken!, telegramSettings); Assert.DoesNotContain(telegramInput.ChatId!, telegramSettings);
            Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync("/api/v1/notifications", new ChannelInput(null, true, true, null, "telegram"))).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync("/api/v1/notifications", new ChannelInput(null, false, true, null, "webhook"))).StatusCode);
            await using (var check = new HomelabDb(options)) {
                var channel = await check.NotificationChannels.SingleAsync();
                Assert.Equal("telegram", channel.Kind); Assert.False(channel.Enabled); Assert.Null(channel.TestedUtc);
                Assert.DoesNotContain(telegramInput.BotToken!, channel.ProtectedUrl); Assert.DoesNotContain(telegramInput.ChatId!, channel.ProtectedUrl);
                check.NotificationChannels.Remove(channel); await check.SaveChangesAsync();
            }
            // Manual compatibility is local evidence, revision-bound and idempotent; it never reserves stock.
            var spare = new InventoryAsset { Name = "Fixture spare", Type = "spare", Quantity = 2 };
            var createdSpare = await client.PostAsJsonAsync("/api/v1/inventory/assets",spare);
            Assert.Equal(HttpStatusCode.OK,createdSpare.StatusCode);
            spare = (await createdSpare.Content.ReadFromJsonAsync<InventoryAsset>())!;
            var spareInput = new SpareReviewInput(Guid.NewGuid(),spare.Id,spare.UpdatedUtc,disk.Serial,"compatible","PRIVATE-SPARE-EVIDENCE: capacity/interface/sector/fit checked against fixture sheets",DateTime.UtcNow.AddDays(30));
            var sparePath = $"/api/v1/disks/{disk.Id}/spare-reviews";
            Assert.Equal(HttpStatusCode.BadRequest,(await client.PostAsJsonAsync(sparePath,spareInput with { ConfirmedIdentity = "wrong" })).StatusCode);
            Assert.Equal(HttpStatusCode.Conflict,(await client.PostAsJsonAsync(sparePath,spareInput with { AssetUpdatedUtc = spare.UpdatedUtc.AddTicks(-1) })).StatusCode);
            var savedSpare = await client.PostAsJsonAsync(sparePath,spareInput);
            Assert.Equal(HttpStatusCode.OK,savedSpare.StatusCode);
            var spareRow = (await savedSpare.Content.ReadFromJsonAsync<SpareCompatibilityReview>())!;
            Assert.Equal("evidence-owner",spareRow.Author);
            Assert.Equal(disk.Id,spareRow.DiskId);
            Assert.Equal(spareRow.Id,(await (await client.PostAsJsonAsync(sparePath,spareInput)).Content.ReadFromJsonAsync<SpareCompatibilityReview>())!.Id);
            Assert.Equal(HttpStatusCode.Conflict,(await client.PostAsJsonAsync(sparePath,spareInput with { Verdict = "unknown" })).StatusCode);
            var spareHistory = await client.GetFromJsonAsync<JsonElement>(sparePath);
            Assert.Equal("reviewed compatible",spareHistory.GetProperty("items")[0].GetProperty("status").GetString());
            Assert.Equal(0,(await client.GetFromJsonAsync<JsonElement>($"/api/v1/disks/{otherDisk.Id}/spare-reviews")).GetProperty("items").GetArrayLength());
            Assert.Equal(HttpStatusCode.BadRequest,(await client.GetAsync(sparePath+"?before=0")).StatusCode);
            spare.Quantity = 1;
            Assert.Equal(HttpStatusCode.OK,(await client.PutAsJsonAsync($"/api/v1/inventory/assets/{spare.Id}",spare)).StatusCode);
            Assert.Equal("inventory changed",(await client.GetFromJsonAsync<JsonElement>(sparePath)).GetProperty("items")[0].GetProperty("status").GetString());
            await using (var check = new HomelabDb(options)) {
                spare = await check.Assets.SingleAsync(x => x.Id == spare.Id);
                Assert.Single(await check.SpareReviews.ToListAsync());
                Assert.Single(await check.Activity.Where(x => x.Action == "Spare compatibility reviewed").ToListAsync());
                Assert.Equal(1,spare.Quantity);
                Assert.Contains("PRIVATE-SPARE-EVIDENCE",Encoding.UTF8.GetString(await InventoryExport.CreateAsync(check,false)));
                Assert.DoesNotContain("PRIVATE-SPARE-EVIDENCE",Encoding.UTF8.GetString(await InventoryExport.CreateAsync(check,true)));
            }
            Assert.Equal(HttpStatusCode.OK,(await client.PostAsJsonAsync(sparePath,spareInput with { RequestId = Guid.NewGuid(), AssetUpdatedUtc = spare.UpdatedUtc, Verdict = "unknown" })).StatusCode);
            spareHistory = await client.GetFromJsonAsync<JsonElement>(sparePath);
            Assert.Equal("unknown",spareHistory.GetProperty("items")[0].GetProperty("status").GetString());
            Assert.Equal("superseded",spareHistory.GetProperty("items")[1].GetProperty("status").GetString());
            var storage = await client.GetFromJsonAsync<JsonElement>("/api/v1/storage");
            var pausedLayout = storage.GetProperty("systems").EnumerateArray().Single(x => x.GetProperty("id").GetGuid() == nas.Id).GetProperty("redundancy")[0];
            Assert.Equal("pool", pausedLayout.GetProperty("poolGuid").GetString());
            Assert.Equal("unavailable", pausedLayout.GetProperty("status").GetString());
            Assert.Equal(0, pausedLayout.GetProperty("groups").GetArrayLength());
            // Stale connection edits and invalid enrollment must not mutate state or audit history.
            var connectionPath = $"/api/v1/connections/{nas.Id}";
            var connectionEdit = new ConnectionSettings("Offline fixture", "wss://nas-fixture.invalid/websocket", null, null, 60, false, null, null, nas.ConfigurationRevision);
            int connectionCount, connectionAuditCount;
            await using (var beforeConnection = new HomelabDb(options)) {
                connectionCount = await beforeConnection.Connections.CountAsync();
                connectionAuditCount = await beforeConnection.Activity.CountAsync();
            }
            foreach (var invalidKey in new string?[] { null, "", "key\nsecret", new('k', 8193) }) {
                var rejected = await client.PostAsJsonAsync("/api/v1/connections", new { name = "Rejected fixture", apiUri = "wss://nas-fixture.invalid/websocket", apiKey = invalidKey });
                Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
                Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync(connectionPath + "/credentials", new { apiKey = invalidKey, configurationRevision = nas.ConfigurationRevision })).StatusCode);
            }
            Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync(connectionPath, connectionEdit with { Name = "bad\nname" })).StatusCode);
            Assert.Equal(HttpStatusCode.Conflict, (await client.PutAsJsonAsync(connectionPath, connectionEdit with { ConfigurationRevision = null })).StatusCode);
            await using (var unchanged = new HomelabDb(options)) {
                Assert.Equal(connectionCount, await unchanged.Connections.CountAsync());
                Assert.Equal(connectionAuditCount, await unchanged.Activity.CountAsync());
                var savedConnection = await unchanged.Connections.SingleAsync(x => x.Id == nas.Id);
                Assert.Equal(nas.ConfigurationRevision, savedConnection.ConfigurationRevision);
                Assert.Equal(nas.ProtectedApiKey, savedConnection.ProtectedApiKey);
            }
            Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync(connectionPath, connectionEdit)).StatusCode);
            var connectionList = await client.GetFromJsonAsync<JsonElement>("/api/v1/connections");
            var latestConnection = connectionList.EnumerateArray().Single(x => x.GetProperty("id").GetGuid() == nas.Id);
            var newConnectionRevision = latestConnection.GetProperty("configurationRevision").GetGuid();
            Assert.NotEqual(nas.ConfigurationRevision, newConnectionRevision);
            Assert.Equal(JsonValueKind.Null, latestConnection.GetProperty("observedSystemSerial").ValueKind);
            Assert.Equal("NAS-A", latestConnection.GetProperty("systemSerial").GetString());
            Assert.Equal("awaiting validation", latestConnection.GetProperty("systemIdentityState").GetString());
            var identityReview = new AcceptNasIdentity("NAS-A", "NAS-B", "Old endpoint identity verification", nas.ConfigurationRevision);
            Assert.Equal(HttpStatusCode.Conflict, (await client.PutAsJsonAsync(connectionPath + "/identity", identityReview)).StatusCode);
            Assert.Equal(HttpStatusCode.Conflict, (await client.PutAsJsonAsync(connectionPath + "/identity", identityReview with { ConfigurationRevision = null })).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync(connectionPath + "/identity", identityReview with { ConfigurationRevision = newConnectionRevision })).StatusCode);

            Assert.False(latestConnection.TryGetProperty("protectedApiKey", out _));
            Assert.False(latestConnection.TryGetProperty("apiKey", out _));
            Assert.Equal(HttpStatusCode.Conflict, (await client.PutAsJsonAsync(connectionPath, connectionEdit with { Name = "Obsolete change" })).StatusCode);
            Assert.Equal(HttpStatusCode.Conflict, (await client.PutAsJsonAsync(connectionPath + "/credentials", new { apiKey = "stale-fixture-secret", configurationRevision = nas.ConfigurationRevision })).StatusCode);
            await using (var unchanged = new HomelabDb(options)) {
                var savedConnection = await unchanged.Connections.SingleAsync(x => x.Id == nas.Id);
                Assert.Equal(newConnectionRevision, savedConnection.ConfigurationRevision);
                Assert.Equal("Offline fixture", savedConnection.Name); Assert.False(savedConnection.Enabled);
                Assert.Equal(nas.ProtectedApiKey, savedConnection.ProtectedApiKey);
                Assert.Equal(connectionAuditCount + 1, await unchanged.Activity.CountAsync());
            }
            var maintenancePath = $"/api/v1/disks/{disk.Id}/maintenance";
            var maintenanceInput = new MaintenanceInput(Guid.NewGuid(), DateTime.UtcNow.AddHours(-2), "estimated", "cabling", "completed", "Synthetic cable inspection", "Fixture maintenance log", disk.Serial);
            Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync(maintenancePath, maintenanceInput with { ConfirmedIdentity = "wrong" })).StatusCode);
            var maintenanceResponse = await client.PostAsJsonAsync(maintenancePath, maintenanceInput);
            Assert.Equal(HttpStatusCode.OK, maintenanceResponse.StatusCode);
            var maintenanceRecord = (await maintenanceResponse.Content.ReadFromJsonAsync<DriveMaintenance>())!;
            Assert.Equal(credentials.username, maintenanceRecord.Author); Assert.Equal(disk.Id, maintenanceRecord.DiskId);
            var duplicateMaintenance = await client.PostAsJsonAsync(maintenancePath, maintenanceInput);
            Assert.Equal(HttpStatusCode.OK, duplicateMaintenance.StatusCode);
            Assert.Equal(maintenanceRecord.Id, (await duplicateMaintenance.Content.ReadFromJsonAsync<DriveMaintenance>())!.Id);
            Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync(maintenancePath, maintenanceInput with { Summary = "Different submission" })).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsJsonAsync($"/api/v1/disks/{otherDisk.Id}/maintenance/{maintenanceRecord.Id}/void", new { reason = "Wrong drive" })).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync(maintenancePath + $"/{maintenanceRecord.Id}/void", new { reason = "" })).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync(maintenancePath + $"/{maintenanceRecord.Id}/void", new { reason = "Fixture correction" })).StatusCode);
            Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync(maintenancePath + $"/{maintenanceRecord.Id}/void", new { reason = "Repeated correction" })).StatusCode);
            var maintenancePage = await client.GetFromJsonAsync<JsonElement>(maintenancePath + "?days=1");
            Assert.Single(maintenancePage.GetProperty("items").EnumerateArray());
            Assert.Equal("Fixture correction", maintenancePage.GetProperty("items")[0].GetProperty("voidReason").GetString());
            Assert.Equal("Synthetic cable inspection", maintenancePage.GetProperty("items")[0].GetProperty("summary").GetString());
            Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync(maintenancePath + "?days=5")).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync(maintenancePath + "?before=0")).StatusCode);
            Assert.Empty((await client.GetFromJsonAsync<JsonElement>($"/api/v1/disks/{otherDisk.Id}/maintenance")).GetProperty("items").EnumerateArray());
            await using (var verification = new HomelabDb(options)) {
                Assert.Single(await verification.Maintenance.ToListAsync());
                Assert.Equal(2, await verification.Activity.CountAsync(x => x.Action.StartsWith("Drive maintenance")));
                Assert.Empty(await verification.Observations.ToListAsync());
                Assert.Empty(await verification.Findings.ToListAsync());
            }
            var privateExport = await client.GetStringAsync("/api/v1/inventory/export?redacted=false");
            var redactedExport = await client.GetStringAsync("/api/v1/inventory/export?redacted=true");
            Assert.Contains("Fixture maintenance log", privateExport); Assert.DoesNotContain("Fixture maintenance log", redactedExport);
            await using (var fixtureRows = new HomelabDb(options)) {
                fixtureRows.Maintenance.AddRange(Enumerable.Range(0, 30).Select(i => new DriveMaintenance {
                    DiskId = disk.Id, RequestId = Guid.NewGuid(), OccurredUtc = DateTime.UtcNow.AddDays(-10), DateConfidence = "documented",
                    Kind = "inspection", Outcome = "unknown", Summary = "Older fixture " + i, Evidence = "Synthetic pagination fixture", ConfirmedIdentity = disk.Serial,
                    Author = "fixture", RecordedUtc = DateTime.UtcNow }));
                await fixtureRows.SaveChangesAsync();
            }
            var recentMaintenance = await client.GetFromJsonAsync<JsonElement>(maintenancePath + "?days=1");
            Assert.Single(recentMaintenance.GetProperty("items").EnumerateArray());
            var firstMaintenancePage = await client.GetFromJsonAsync<JsonElement>(maintenancePath + "?days=30");
            Assert.Equal(25, firstMaintenancePage.GetProperty("items").GetArrayLength());
            var maintenanceCursor = firstMaintenancePage.GetProperty("nextBefore").GetInt64();
            var secondMaintenancePage = await client.GetFromJsonAsync<JsonElement>(maintenancePath + $"?days=30&before={maintenanceCursor}");
            Assert.Equal(6, secondMaintenancePage.GetProperty("items").GetArrayLength());
            Assert.Equal(JsonValueKind.Null, secondMaintenancePage.GetProperty("nextBefore").ValueKind);
            Assert.DoesNotContain(secondMaintenancePage.GetProperty("items").EnumerateArray(), item => firstMaintenancePage.GetProperty("items").EnumerateArray().Any(first => first.GetProperty("id").GetInt64() == item.GetProperty("id").GetInt64()));
            var scanPath = $"/api/v1/connections/{nas.Id}/pools/scan-pool/scans";
            var scanPage = await client.GetFromJsonAsync<JsonElement>(scanPath);
            Assert.Equal(50,scanPage.GetProperty("items").GetArrayLength());
            Assert.All(scanPage.GetProperty("items").EnumerateArray(), row => {
                Assert.Equal(nas.Id,row.GetProperty("connectionId").GetGuid());
                Assert.EndsWith("Z",row.GetProperty("scanStartedUtc").GetString());
            });
            var olderScans = await client.GetFromJsonAsync<JsonElement>(scanPath+"?before="+scanPage.GetProperty("nextBefore").GetInt64());
            Assert.Equal(2,olderScans.GetProperty("items").GetArrayLength());
            Assert.Equal(JsonValueKind.Null,olderScans.GetProperty("nextBefore").ValueKind);
            Assert.Equal(HttpStatusCode.BadRequest,(await client.GetAsync(scanPath+"?before=0")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound,(await client.GetAsync($"/api/v1/connections/{Guid.NewGuid()}/pools/scan-pool/scans")).StatusCode);
            var scanOverview = (await client.GetFromJsonAsync<JsonElement>("/api/v1/overview")).GetProperty("scanReports");
            Assert.Equal(1, scanOverview.GetArrayLength());
            Assert.Equal("paused", scanOverview[0].GetProperty("freshness").GetString());
            Assert.Equal(45.5, scanOverview[0].GetProperty("percentage").GetDouble());
            Assert.Equal(JsonValueKind.Null, scanOverview[0].GetProperty("errors").ValueKind);
            Assert.Equal(JsonValueKind.Null, scanOverview[0].GetProperty("validUntilUtc").ValueKind);
            Assert.EndsWith("Z", scanOverview[0].GetProperty("fetchedUtc").GetString());
            var capacityOverview = await client.GetFromJsonAsync<JsonElement>("/api/v1/overview");
            var outlook = Assert.Single(capacityOverview.GetProperty("capacityOutlook").EnumerateArray(), x => x.GetProperty("connectionId").GetGuid() == nas.Id && x.GetProperty("poolGuid").GetString() == "capacity-pool");
            Assert.Equal("paused", outlook.GetProperty("status").GetString());
            Assert.Equal(JsonValueKind.Null, outlook.GetProperty("projectedDateAtThreshold").ValueKind);
            Assert.Equal(30, outlook.GetProperty("basisDays").GetInt32());
            Assert.Equal(.8, outlook.GetProperty("thresholdFraction").GetDouble());
            Assert.InRange(outlook.GetProperty("usedFraction").GetDouble(), .39, .41);
            var capacityPath = $"/api/v1/connections/{nas.Id}/pools/capacity-pool/capacity";
            var capacity = await client.GetFromJsonAsync<JsonElement>(capacityPath);
            Assert.Equal("paused", capacity.GetProperty("status").GetString());
            Assert.Equal(JsonValueKind.Null, capacity.GetProperty("daysTo80Percent").ValueKind);
            Assert.InRange(capacity.GetProperty("samples").GetInt32(), 30, 31);
            Assert.Equal(400000, capacity.GetProperty("allocatedBytes").GetInt64());
            Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync(capacityPath + "?days=5")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/v1/connections/{Guid.NewGuid()}/pools/pool/capacity")).StatusCode);
            var memberPath=$"/api/v1/connections/{nas.Id}/pools/member-pool/history";
            var members=await client.GetFromJsonAsync<JsonElement>(memberPath);
            Assert.Equal(100,members.GetProperty("items").GetArrayLength());
            foreach(var row in members.GetProperty("items").EnumerateArray()) {
                Assert.False(row.TryGetProperty("sourceContextJson",out _));
                Assert.Equal("compared",row.GetProperty("vdevComparison").GetProperty("status").GetString());
                Assert.Equal(1,row.GetProperty("vdevComparison").GetProperty("members")[0].GetProperty("read").GetProperty("difference").GetInt64());
            }
            var oldestMembers=await client.GetFromJsonAsync<JsonElement>(memberPath+"?before="+members.GetProperty("nextBefore").GetInt64());
            Assert.Equal("baseline",Assert.Single(oldestMembers.GetProperty("items").EnumerateArray()).GetProperty("vdevComparison").GetProperty("status").GetString());
            var summaryPath = $"/api/v1/connections/{nas.Id}/pools/pool/daily-history";
            var daily = await client.GetFromJsonAsync<JsonElement>(summaryPath);
            var firstPage = daily.GetProperty("items").EnumerateArray().Select(x => x.GetProperty("id").GetInt64()).ToArray();
            Assert.Equal(50, firstPage.Length);
            Assert.False(daily.GetProperty("backupConfigured").GetBoolean());
            var cursorDay = Uri.EscapeDataString(daily.GetProperty("nextDay").GetString()!);
            var cursorId = daily.GetProperty("nextId").GetInt64();
            var older = await client.GetFromJsonAsync<JsonElement>($"{summaryPath}?beforeDay={cursorDay}&beforeId={cursorId}");
            Assert.Equal(1, older.GetProperty("items").GetArrayLength());
            Assert.DoesNotContain(older.GetProperty("items")[0].GetProperty("id").GetInt64(), firstPage);
            Assert.Equal(JsonValueKind.Null, older.GetProperty("nextId").ValueKind);
            Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync(summaryPath + "?beforeId=1")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/v1/connections/{Guid.NewGuid()}/pools/pool/daily-history")).StatusCode);
            foreach (var dailyMode in new[] { false, true }) {
                var path = $"/api/v1/connections/{nas.Id}/" + (dailyMode ? "collection-daily-history" : "collection-history");
                var recent = await client.GetFromJsonAsync<JsonElement>(path);
                var items = recent.GetProperty("items").EnumerateArray().ToArray(); Assert.Equal(50, items.Length);
                Assert.All(items, x => Assert.Equal("partial", x.GetProperty("result").GetString()));
                Assert.All(items, x => Assert.False(x.GetProperty("coverage").GetProperty("known").GetBoolean()));
                var nextId = recent.GetProperty("nextId").GetInt64();
                var query = dailyMode ? $"?beforeDay={Uri.EscapeDataString(recent.GetProperty("nextDay").GetString()!)}&beforeId={nextId}" : $"?before={nextId}";
                var next = await client.GetFromJsonAsync<JsonElement>(path + query);
                Assert.Equal(1, next.GetProperty("items").GetArrayLength());
                Assert.DoesNotContain(next.GetProperty("items")[0].GetProperty("id").GetInt64(), items.Select(x => x.GetProperty("id").GetInt64()));
                Assert.Equal(JsonValueKind.Null, next.GetProperty("nextId").ValueKind);
            }
            Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync($"/api/v1/connections/{nas.Id}/collection-daily-history?beforeId=1")).StatusCode);
            var datasets = await client.GetFromJsonAsync<JsonElement>($"/api/v1/connections/{nas.Id}/datasets");
            Assert.False(datasets.GetProperty("enabled").GetBoolean()); Assert.Equal("disabled", datasets.GetProperty("status").GetString());
            Assert.Equal(JsonValueKind.Null, datasets.GetProperty("snapshot").ValueKind);
            Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/v1/connections/{Guid.NewGuid()}/datasets")).StatusCode);
            var cache = await client.GetFromJsonAsync<JsonElement>($"/api/v1/connections/{nas.Id}/cached-temperatures");
            Assert.False(cache.GetProperty("enabled").GetBoolean()); Assert.Equal("disabled", cache.GetProperty("status").GetString());
            var cacheSnapshot = cache.GetProperty("snapshot"); Assert.EndsWith("Z", cacheSnapshot.GetProperty("fetchedUtc").GetString());
            Assert.Equal(32, cacheSnapshot.GetProperty("readings")[0].GetProperty("celsius").GetInt32());
            Assert.Equal(JsonValueKind.Null, cacheSnapshot.GetProperty("readings")[1].GetProperty("celsius").ValueKind);
            Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/v1/connections/{Guid.NewGuid()}/cached-temperatures")).StatusCode);
            // Malformed edits must fail before changing local records or their audit trail.
            var assetResponse = await client.PostAsJsonAsync("/api/v1/inventory/assets", new InventoryAsset { Name = "Fixture spare" });
            Assert.Equal(HttpStatusCode.OK, assetResponse.StatusCode);
            var asset = (await assetResponse.Content.ReadFromJsonAsync<InventoryAsset>())!;
            await using (var check = new HomelabDb(options)) {
                var activityCount = await check.Activity.CountAsync();
                foreach (var field in new[] { "currency", "purpose", "owner", "location", "version", "managementUrl", "runbook", "documentation", "compatibilityNotes" }) {
                    var malformed = JsonSerializer.SerializeToNode(asset, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
                    malformed[field] = null;
                    var rejected = await client.PutAsJsonAsync($"/api/v1/inventory/assets/{asset.Id}", malformed);
                    Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
                    Assert.False(string.IsNullOrWhiteSpace((await rejected.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("error").GetString()));
                    Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/v1/inventory/assets", malformed)).StatusCode);
                }
                asset.Owner = new string('x', 201);
                Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync($"/api/v1/inventory/assets/{asset.Id}", asset)).StatusCode);
                foreach (var evidence in new string?[] { null, new string('x', 10001) })
                    Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/v1/inventory/dependencies", new AssetDependency { SourceId = asset.Id, TargetId = Guid.NewGuid(), Evidence = evidence! })).StatusCode);
                Assert.Equal(activityCount, await check.Activity.CountAsync());
                Assert.Equal(2,await check.Assets.CountAsync());
                Assert.Equal("", (await check.Assets.SingleAsync(x => x.Id == asset.Id)).Owner);
                Assert.Empty(await check.Dependencies.ToListAsync());
            }
            var metadata = new DiskMetadata(null, false, null, null, DateTime.UtcNow.Date.AddYears(-1), "documented", DateTime.UtcNow.Date.AddYears(1), "Local purchase evidence", "installed");
            var metadataPath = $"/api/v1/disks/{disk.Id}/metadata";
            Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync(metadataPath, metadata)).StatusCode);
            await using (var check = new HomelabDb(options)) {
                var activityCount = await check.Activity.CountAsync();
                foreach (var malformed in new[] { metadata with { Notes = null! }, metadata with { Notes = new string('x', 10001) }, metadata with { PurchasedUtc = DateTime.SpecifyKind(metadata.PurchasedUtc!.Value, DateTimeKind.Unspecified) }, metadata with { WarrantyEndUtc = DateTime.UnixEpoch.AddDays(-1) } })
                    Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync(metadataPath, malformed)).StatusCode);
                Assert.Equal(activityCount, await check.Activity.CountAsync());
                Assert.Equal(1, await check.DiskAnnotations.CountAsync(x => x.DiskId == disk.Id));
                Assert.Equal(metadata.Notes, (await check.Disks.SingleAsync(x => x.Id == disk.Id)).Notes);
            }
            var acquisitionPath = $"/api/v1/disks/{disk.Id}/acquisition";
            Assert.Equal(JsonValueKind.Null, (await client.GetFromJsonAsync<JsonElement>(acquisitionPath)).GetProperty("record").ValueKind);
            var acquisition = new DriveAcquisitionInput(null, DateTime.UtcNow.Date.AddYears(-1), "estimated", "private installation log", 219.99m, "usd", "documented", "private receipt reference");
            var acquisitionResponse = await client.PutAsJsonAsync(acquisitionPath, acquisition);
            Assert.Equal(HttpStatusCode.OK, acquisitionResponse.StatusCode);
            var savedAcquisition = (await acquisitionResponse.Content.ReadFromJsonAsync<DriveAcquisition>())!;
            Assert.Equal("USD", savedAcquisition.Currency); Assert.Equal(credentials.username, savedAcquisition.Author);
            Assert.Equal(HttpStatusCode.Conflict, (await client.PutAsJsonAsync(acquisitionPath, acquisition with { PurchaseCost=5 })).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync(acquisitionPath, acquisition with { Revision=savedAcquisition.Revision, PurchaseCost=-1 })).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/v1/disks/{Guid.NewGuid()}/acquisition")).StatusCode);
            Assert.Equal(JsonValueKind.Null, (await client.GetFromJsonAsync<JsonElement>($"/api/v1/disks/{otherDisk.Id}/acquisition")).GetProperty("record").ValueKind);
            var acquisitionUpdate = await client.PutAsJsonAsync(acquisitionPath, acquisition with { Revision=savedAcquisition.Revision, PurchaseCost=0, PurchaseEvidence="Corrected gift receipt" });
            Assert.Equal(HttpStatusCode.OK, acquisitionUpdate.StatusCode);
            var revisedAcquisition = (await acquisitionUpdate.Content.ReadFromJsonAsync<DriveAcquisition>())!;
            Assert.NotEqual(savedAcquisition.Revision, revisedAcquisition.Revision);
            Assert.Equal(0m, revisedAcquisition.PurchaseCost);
            await using (var check = new HomelabDb(options)) {
                Assert.Equal(3, await check.DiskAnnotations.CountAsync(x => x.DiskId == disk.Id));
                var acquisitionHistory = await check.DiskAnnotations.Where(x => x.DiskId == disk.Id).OrderByDescending(x => x.Id).FirstAsync();
                Assert.Contains("219.99", acquisitionHistory.BeforeJson); Assert.Contains("Corrected gift receipt", acquisitionHistory.AfterJson);
            }
            var acquisitionExport = await client.GetFromJsonAsync<JsonElement>("/api/v1/inventory/export?redacted=true");
            Assert.Equal(JsonValueKind.Null, acquisitionExport.GetProperty("driveAcquisitions")[0].GetProperty("purchaseEvidence").ValueKind);
            var recoveryResponse = await client.PostAsJsonAsync("/api/v1/recovery", new RecoveryPolicy { Asset = "Fixture photos", Destination = "Offline copy", Scope = "photos" });
            Assert.Equal(HttpStatusCode.OK, recoveryResponse.StatusCode);
            var recovery = (await recoveryResponse.Content.ReadFromJsonAsync<RecoveryPolicy>())!;
            await using (var check = new HomelabDb(options)) {
                var activityCount = await check.Activity.CountAsync();
                foreach (var field in new[] { "asset", "destination", "scope", "exclusions", "retention", "schedule", "failureDomains", "runbook", "keyCustodyReference", "evidence", "importance" }) {
                    foreach (var value in new string?[] { null, new string('x', 50001) }) {
                        var malformed = JsonSerializer.SerializeToNode(recovery, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
                        malformed[field] = value;
                        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/v1/recovery", malformed)).StatusCode);
                        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync($"/api/v1/recovery/{recovery.Id}", malformed)).StatusCode);
                    }
                }
                Assert.Equal(activityCount, await check.Activity.CountAsync());
                Assert.Equal(recovery.Revision, (await check.Recovery.SingleAsync(x => x.Id == recovery.Id)).Revision);
                Assert.Equal(1, await check.Recovery.CountAsync());
            }
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/v1/recovery/{recovery.Id}/history?kind=runs")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/v1/recovery/{Guid.NewGuid()}/history?kind=runs")).StatusCode);
            foreach(var suffix in new[]{"kind=invalid", "kind=runs&beforeId="+Guid.NewGuid(), "kind=runs&beforeAtUtc=2026-01-01T00:00:00Z"})
                Assert.Equal(HttpStatusCode.BadRequest,(await client.GetAsync($"/api/v1/recovery/{recovery.Id}/history?{suffix}")).StatusCode);
            Assert.Equal(HttpStatusCode.OK,(await client.GetAsync("/api/v1/activity/history")).StatusCode);
            foreach(var query in new[]{"snapshotId=-1","before=0","before=1","snapshotId=1&before=2"})
                Assert.Equal(HttpStatusCode.BadRequest,(await client.GetAsync("/api/v1/activity/history?"+query)).StatusCode);
            var datasetHistory=await client.GetFromJsonAsync<JsonElement>($"/api/v1/connections/{nas.Id}/dataset-history");
            Assert.Equal(10,datasetHistory.GetProperty("items").GetArrayLength());var datasetCeiling=datasetHistory.GetProperty("snapshotId").GetInt64();var datasetBefore=datasetHistory.GetProperty("nextBefore").GetInt64();
            var olderDatasetHistory=await client.GetFromJsonAsync<JsonElement>($"/api/v1/connections/{nas.Id}/dataset-history?snapshotId={datasetCeiling}&before={datasetBefore}");
            Assert.Equal(2,olderDatasetHistory.GetProperty("items").GetArrayLength());Assert.Equal(JsonValueKind.Null,olderDatasetHistory.GetProperty("nextBefore").ValueKind);
            Assert.DoesNotContain(olderDatasetHistory.GetProperty("items").EnumerateArray().Select(x=>x.GetProperty("id").GetInt64()),id=>datasetHistory.GetProperty("items").EnumerateArray().Any(x=>x.GetProperty("id").GetInt64()==id));
            Assert.Equal(HttpStatusCode.NotFound,(await client.GetAsync($"/api/v1/connections/{Guid.NewGuid()}/dataset-history")).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest,(await client.GetAsync($"/api/v1/connections/{nas.Id}/dataset-history?before=1")).StatusCode);
            var renamePath=$"/api/v1/connections/{nas.Id}/dataset-renames";
            var renamePreview=await client.GetFromJsonAsync<JsonElement>(renamePath+"/review?fromSampleId=12&toSampleId=1");
            var renameInput=new DatasetRenameInput(12,1,"tank/old","tank/photos","tank/old","tank/photos",renamePreview.GetProperty("reviewToken").GetString()!,"Private reviewed rename log",true,Guid.NewGuid());
            Assert.Equal(HttpStatusCode.Conflict,(await client.PostAsJsonAsync(renamePath,renameInput with{ReviewToken="stale"})).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest,(await client.PostAsJsonAsync(renamePath,renameInput with{ConfirmedSameDataset=false})).StatusCode);
            var renamed=await client.PostAsJsonAsync(renamePath,renameInput);Assert.Equal(HttpStatusCode.OK,renamed.StatusCode);var renameRecord=(await renamed.Content.ReadFromJsonAsync<DatasetRenameReview>())!;
            Assert.Equal(credentials.username,renameRecord.Author);Assert.Equal(HttpStatusCode.OK,(await client.PostAsJsonAsync(renamePath,renameInput)).StatusCode);
            Assert.Equal(HttpStatusCode.Conflict,(await client.PostAsJsonAsync(renamePath,renameInput with{Evidence="different"})).StatusCode);
            Assert.Equal(HttpStatusCode.Conflict,(await client.PostAsJsonAsync(renamePath,renameInput with{RequestId=Guid.NewGuid()})).StatusCode);
            var reversal=new DatasetRenameReversal("Private correction evidence");Assert.Equal(HttpStatusCode.OK,(await client.PostAsJsonAsync(renamePath+$"/{renameRecord.Id}/reverse",reversal)).StatusCode);Assert.Equal(HttpStatusCode.OK,(await client.PostAsJsonAsync(renamePath+$"/{renameRecord.Id}/reverse",reversal)).StatusCode);
            Assert.Equal(HttpStatusCode.Conflict,(await client.PostAsJsonAsync(renamePath+$"/{renameRecord.Id}/reverse",new DatasetRenameReversal("different"))).StatusCode);
            var renameHistory=await client.GetFromJsonAsync<JsonElement>(renamePath);Assert.Single(renameHistory.GetProperty("items").EnumerateArray());
            var renameExport=await client.GetFromJsonAsync<JsonElement>("/api/v1/inventory/export?redacted=true");var redactedRename=renameExport.GetProperty("datasetRenameReviews")[0];foreach(var field in new[]{"fromName","toName","evidence","author","reversalEvidence","reversedBy"})Assert.Equal(JsonValueKind.Null,redactedRename.GetProperty(field).ValueKind);
            await using(var verifyRename=new HomelabDb(options)){Assert.Single(await verifyRename.DatasetRenames.ToListAsync());Assert.Equal(1,await verifyRename.Activity.CountAsync(x=>x.Action=="Dataset rename reviewed"));Assert.Equal(1,await verifyRename.Activity.CountAsync(x=>x.Action=="Dataset rename review reversed"));}
            var originalRevision = recovery.Revision;
            var recoveryPath = $"/api/v1/recovery/{recovery.Id}";
            var run = new BackupEvidence { PolicyRevision = originalRevision, AttemptedUtc = DateTime.UtcNow.AddHours(-1), RecoveryPointUtc = DateTime.UtcNow.AddHours(-2), Outcome = "success", DestinationConfirmed = true, Evidence = "Verified destination manifest", ReviewByUtc = DateTime.UtcNow.AddDays(1) };
            var exercise = new RestoreExercise { PolicyRevision = originalRevision, TestedUtc = DateTime.UtcNow.AddMinutes(-30), RecoveryPointUtc = run.RecoveryPointUtc!.Value, Scope = "photos", Checks = "Opened restored photos", Result = "passed", DurationMinutes = 15, Evidence = "Exercise log", ReviewByUtc = run.ReviewByUtc };
            await using (var check = new HomelabDb(options)) {
                var activityCount = await check.Activity.CountAsync();
                foreach (var (path, valid, cases) in new[] {
                    (recoveryPath + "/runs", JsonSerializer.SerializeToNode(run, new JsonSerializerOptions(JsonSerializerDefaults.Web))!, new (string, object?)[] {
                        ("attemptedUtc", "2026-01-01T12:00:00"), ("recoveryPointUtc", "1969-12-31T12:00:00Z"),
                        ("reviewByUtc", DateTime.UtcNow.AddDays(1).ToString("yyyy-MM-ddTHH:mm:ss")),
                        ("failureReason", null), ("failureReason", new string('x', 10001)), ("evidence", null) }),
                    (recoveryPath + "/exercises", JsonSerializer.SerializeToNode(exercise, new JsonSerializerOptions(JsonSerializerDefaults.Web))!, new (string, object?)[] {
                        ("testedUtc", "2026-01-01T12:00:00"), ("recoveryPointUtc", "1969-12-31T12:00:00Z"),
                        ("reviewByUtc", DateTime.UtcNow.AddDays(1).ToString("yyyy-MM-ddTHH:mm:ss")),
                        ("scope", new string('x', 10001)), ("checks", new string('x', 10001)), ("evidence", new string('x', 10001)), ("durationMinutes", 0) }) }) {
                    foreach (var (field, value) in cases) {
                        var malformed = valid.DeepClone(); malformed[field] = JsonSerializer.SerializeToNode(value);
                        var rejected = await client.PostAsJsonAsync(path, malformed);
                        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
                        Assert.False(string.IsNullOrWhiteSpace((await rejected.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("error").GetString()));
                    }
                }
                Assert.Equal(activityCount, await check.Activity.CountAsync());
                Assert.Empty(await check.BackupEvidence.Where(x => x.PolicyId == recovery.Id).ToListAsync());
                Assert.Empty(await check.RestoreExercises.Where(x => x.PolicyId == recovery.Id).ToListAsync());
            }
            Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync(recoveryPath + "/runs", run)).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync(recoveryPath + "/exercises", exercise)).StatusCode);
            recovery.Scope = "photos and documents";
            var revisedResponse = await client.PutAsJsonAsync(recoveryPath, recovery);
            Assert.Equal(HttpStatusCode.OK, revisedResponse.StatusCode);
            var revised = (await revisedResponse.Content.ReadFromJsonAsync<RecoveryPolicy>())!;
            Assert.NotEqual(originalRevision, revised.Revision);
            await using (var check = new HomelabDb(options)) {
                var activityCount = await check.Activity.CountAsync();
                Assert.Equal(HttpStatusCode.Conflict, (await client.PutAsJsonAsync(recoveryPath, recovery)).StatusCode);
                Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync(recoveryPath + "/runs", run)).StatusCode);
                Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync(recoveryPath + "/exercises", exercise)).StatusCode);
                run.PolicyRevision = Guid.Empty; exercise.PolicyRevision = Guid.Empty;
                Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync(recoveryPath + "/runs", run)).StatusCode);
                Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync(recoveryPath + "/exercises", exercise)).StatusCode);
                Assert.Equal(activityCount, await check.Activity.CountAsync());
                var stored = await check.Recovery.SingleAsync(x => x.Id == recovery.Id);
                var runs = await check.BackupEvidence.Where(x => x.PolicyId == recovery.Id).ToListAsync();
                var exercises = await check.RestoreExercises.Where(x => x.PolicyId == recovery.Id).ToListAsync();
                Assert.Equal(originalRevision, Assert.Single(runs).PolicyRevision);
                Assert.Equal(originalRevision, Assert.Single(exercises).PolicyRevision);
                Assert.Equal("unverified", RecoveryEvaluator.Assess(stored, runs, exercises, DateTime.UtcNow).RecoveryPoint);
            }
            run.PolicyRevision = revised.Revision; exercise.PolicyRevision = revised.Revision; exercise.Scope = revised.Scope;
            Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync(recoveryPath + "/runs", run)).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync(recoveryPath + "/exercises", exercise)).StatusCode);
            var policyRaces = await Task.WhenAll(client.PutAsJsonAsync(recoveryPath, revised), client.PutAsJsonAsync(recoveryPath, revised));
            Assert.Single(policyRaces, x => x.StatusCode == HttpStatusCode.OK);
            Assert.Single(policyRaces, x => x.StatusCode == HttpStatusCode.Conflict);
            foreach (var response in policyRaces) response.Dispose();
            var proposedPlan = new PlanningItem { Id = Guid.NewGuid(), DiskId = disk.Id, Title = "Review replacement", Reason = "Original drive evidence", Budget = 250, Owner = "Planner" };
            var createdPlanResponse = await client.PostAsJsonAsync("/api/v1/plans", proposedPlan); Assert.Equal(HttpStatusCode.OK, createdPlanResponse.StatusCode);
            var plan = (await createdPlanResponse.Content.ReadFromJsonAsync<PlanningItem>())!;
            Assert.NotEqual(proposedPlan.Id, plan.Id); Assert.NotEqual(proposedPlan.Revision, plan.Revision);
            var staleRevision = plan.Revision;
            plan.Title = "Budget a compatible spare"; plan.Reason = "Updated quote; retain the earlier evidence in history";
            plan.Owner = "Future owner"; plan.Budget = 325; plan.Currency = "EUR"; plan.Status = "ordered"; plan.DueUtc = DateTime.UtcNow.Date.AddDays(30);
            var edit = await client.PutAsJsonAsync($"/api/v1/plans/{plan.Id}", plan); Assert.Equal(HttpStatusCode.OK, edit.StatusCode);
            var updatedPlan = (await edit.Content.ReadFromJsonAsync<PlanningItem>())!;
            Assert.NotEqual(staleRevision, updatedPlan.Revision); Assert.Equal("Future owner", updatedPlan.Owner); Assert.Equal(325, updatedPlan.Budget);
            Assert.Equal(HttpStatusCode.Conflict, (await client.PutAsJsonAsync($"/api/v1/plans/{plan.Id}", plan)).StatusCode);
            updatedPlan.DiskId = otherDisk.Id;
            Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync($"/api/v1/plans/{plan.Id}", updatedPlan)).StatusCode);
            updatedPlan.DiskId = disk.Id; updatedPlan.Owner = null!;
            Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync($"/api/v1/plans/{plan.Id}", updatedPlan)).StatusCode);
            updatedPlan.Owner = "Future owner"; updatedPlan.Budget = -1;
            Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync($"/api/v1/plans/{plan.Id}", updatedPlan)).StatusCode);
            var planHistory = await client.GetFromJsonAsync<JsonElement>($"/api/v1/plans/{plan.Id}/history");
            Assert.Equal(2, planHistory.GetProperty("items").GetArrayLength());
            var planChange = planHistory.GetProperty("items")[0];
            Assert.Equal(credentials.username, planChange.GetProperty("actor").GetString());
            Assert.Equal("Original drive evidence", planChange.GetProperty("before").GetProperty("reason").GetString());
            Assert.Equal(325, planChange.GetProperty("after").GetProperty("budget").GetDecimal());
            Assert.EndsWith("Z", planChange.GetProperty("after").GetProperty("dueUtc").GetString());
            updatedPlan.Budget = 325; updatedPlan.Status = "complete";
            var races = await Task.WhenAll(client.PutAsJsonAsync($"/api/v1/plans/{plan.Id}", updatedPlan), client.PutAsJsonAsync($"/api/v1/plans/{plan.Id}", updatedPlan));
            Assert.Single(races, x => x.StatusCode == HttpStatusCode.OK); Assert.Single(races, x => x.StatusCode == HttpStatusCode.Conflict);
            foreach (var response in races) response.Dispose();
            var before = DateTime.UtcNow;
            var forged = new Observation { Id = 12345, ConnectionId = Guid.NewGuid(), DiskId = otherDisk.Id, ReceivedUtc = before.AddYears(1), RecordedBy = "forged-owner", SourceKey = "evidence-one", Source = "fixture export", SourceSchema = "fixture-v1", ResetEpoch = "one", Metric = "ata.pending_sectors", Unit = "sectors", Value = 3, Quality = "observed", ObservedUtc = before.AddMinutes(-1) };
            var imported = await client.PostAsJsonAsync($"/api/v1/disks/{disk.Id}/observations", forged);
            Assert.Equal(HttpStatusCode.OK, imported.StatusCode);
            var saved = (await imported.Content.ReadFromJsonAsync<Observation>())!;
            Assert.NotEqual(forged.Id, saved.Id); Assert.Equal(disk.Id, saved.DiskId); Assert.Equal(nas.Id, saved.ConnectionId);
            Assert.Equal(credentials.username, saved.RecordedBy); Assert.InRange(saved.ReceivedUtc, before, DateTime.UtcNow);
            Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync($"/api/v1/disks/{disk.Id}/observations", forged)).StatusCode);
            forged.SourceKey = "invalid-schema"; forged.SourceSchema = null!;
            Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync($"/api/v1/disks/{disk.Id}/observations", forged)).StatusCode);
            forged.SourceSchema = "fixture-v1"; forged.Unit = "hours";
            Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync($"/api/v1/disks/{disk.Id}/observations", forged)).StatusCode);
            forged.Unit = "sectors"; client.DefaultRequestHeaders.Remove("X-Homelab-Request");
            Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync($"/api/v1/disks/{disk.Id}/observations", forged)).StatusCode);
            client.DefaultRequestHeaders.Add("X-Homelab-Request", "1");
            var history = (await client.GetFromJsonAsync<JsonElement>($"/api/v1/disks/{disk.Id}/observations"));
            Assert.Equal(1, history.GetArrayLength());
            Assert.Equal(credentials.username, history[0].GetProperty("recordedBy").GetString());
            Assert.EndsWith("Z", history[0].GetProperty("receivedUtc").GetString());
            Assert.EndsWith("Z", history[0].GetProperty("observedUtc").GetString());
            Assert.Empty((await client.GetFromJsonAsync<Observation[]>($"/api/v1/disks/{otherDisk.Id}/observations"))!);
            var historyUrl = $"/api/v1/disks/{disk.Id}/observation-history?metric=ata.pending_sectors&days=7";
            var historyPage = (await client.GetFromJsonAsync<ObservationPage>(historyUrl))!;
            Assert.Equal(saved.Id, Assert.Single(historyPage.Items).Id); Assert.Null(historyPage.NextBefore);
            Assert.Equal("baseline", historyPage.Comparisons[saved.Id].Status); Assert.Null(historyPage.Comparisons[saved.Id].Difference);
            Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync(historyUrl + "&before=1")).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync(historyUrl.Replace("days=7", "days=8"))).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync(historyUrl.Replace("ata.pending_sectors", "arbitrary"))).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(historyUrl.Replace(disk.Id.ToString(), Guid.NewGuid().ToString()))).StatusCode);

            await using (var verification = new HomelabDb(options)) {
                var audit = await verification.Activity.SingleAsync(x => x.Action == "Disk evidence imported");
                Assert.Equal(credentials.username, audit.Actor); Assert.Contains("evidence-one", audit.Detail);
                Assert.Equal("sectors", (await verification.Findings.SingleAsync(x => x.Rule != SelfTestFindings.Rule)).EvidenceUnit);
            }
            var reportJson = JsonSerializer.Serialize(new {
                json_format_version = new[] { 1, 0 }, smartctl = new { version = new[] { 7, 4 }, exit_status = 0 },
                device = new { protocol = "ATA" }, serial_number = disk.Serial, model_name = disk.Model,
                local_time = new { time_t = DateTimeOffset.UtcNow.AddMinutes(-2).ToUnixTimeSeconds() },
                temperature = new { current = 32 }, power_on_time = new { hours = 12000 }, power_cycle_count = 42,
                ata_smart_attributes = new { table = new[] { new { id = 197, name = "Current_Pending_Sector", value = 100, worst = 99, thresh = 0, raw = new { value = 4, @string = "4" } } } },
                ata_smart_self_test_log = new { standard = new { table = new[] { new { type = new { value = 2, @string = "Extended offline" }, status = new { value = 112, @string = "Completed: read failure", passed = false }, lifetime_hours = 12000, lba = 1234 } } } },
                private_output = "SECRET REPORT COMMAND PATH"
            });
            var reportInput = new SmartReportInput(reportJson, "report-epoch", true);
            var reportPath = $"/api/v1/disks/{disk.Id}/smart-reports";
            var reportPreviewResponse = await client.PostAsJsonAsync(reportPath + "/preview", reportInput); Assert.Equal(HttpStatusCode.OK, reportPreviewResponse.StatusCode);
            var reportPreview = (await reportPreviewResponse.Content.ReadFromJsonAsync<SmartReportPreview>())!;
            await using (var verification = new HomelabDb(options)) { Assert.Equal(1, await verification.Observations.CountAsync()); Assert.Equal(0, await verification.SmartReports.CountAsync()); }
            Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync(reportPath, reportInput)).StatusCode);
            reportInput = reportInput with { ExpectedHash = reportPreview.ReportHash, ConfirmedSerial = disk.Serial };
            await using (var legacy = new HomelabDb(options)) {
                legacy.SmartReports.Add(new() { DiskId = disk.Id, ReportHash = reportPreview.PreviousReportHash!, Actor = "legacy ATA fixture", CuratedJson = "{}" }); await legacy.SaveChangesAsync();
            }
            Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync(reportPath, reportInput)).StatusCode);
            await using (var legacy = new HomelabDb(options)) {
                Assert.Equal(1, await legacy.Observations.CountAsync());
                (await legacy.SmartReports.SingleAsync()).ReportHash = reportPreview.BeforeSelfTestsHash!;
                await legacy.SaveChangesAsync();
            }
            Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync(reportPath, reportInput)).StatusCode);
            await using (var legacy = new HomelabDb(options)) {
                Assert.Equal(1, await legacy.Observations.CountAsync());
                legacy.SmartReports.Remove(await legacy.SmartReports.SingleAsync()); await legacy.SaveChangesAsync();
            }
            Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync(reportPath, reportInput)).StatusCode);
            Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync(reportPath, reportInput with { ContinuityGroup = "changed-choice", ConfirmAtaCounterUnits = false })).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync($"/api/v1/disks/{otherDisk.Id}/smart-reports/preview", reportInput)).StatusCode);
            var reportHistory = await client.GetFromJsonAsync<JsonElement>(reportPath);
            var pagedReports = await client.GetFromJsonAsync<JsonElement>(reportPath + "/history");
            Assert.Single(pagedReports.GetProperty("items").EnumerateArray());
            Assert.Equal(JsonValueKind.Null, pagedReports.GetProperty("nextOffset").ValueKind);
            Assert.EndsWith("Z", pagedReports.GetProperty("asOf").GetString());
            Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync(reportPath + "/history?offset=-1")).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync(reportPath + "/history?offset=50")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/v1/disks/{Guid.NewGuid()}/smart-reports/history")).StatusCode);
            Assert.Equal(1, reportHistory.GetArrayLength()); Assert.Equal(credentials.username, reportHistory[0].GetProperty("actor").GetString());
            Assert.DoesNotContain("SECRET REPORT COMMAND PATH", reportHistory.GetRawText());
            var savedTest = reportHistory[0].GetProperty("report").GetProperty("ataSelfTests")[0].GetProperty("entries")[0];
            Assert.Equal("failed",savedTest.GetProperty("result").GetString());
            Assert.Equal(12000,savedTest.GetProperty("lifetimeHours").GetInt64());
            Assert.Equal(1234,savedTest.GetProperty("firstErrorLba").GetInt64());
            var planningReview = await client.GetFromJsonAsync<JsonElement>("/api/v1/planning/review");
            Assert.Contains("self-test failure", planningReview.GetRawText());
            var coverage = await client.GetFromJsonAsync<JsonElement>($"/api/v1/disks/{disk.Id}/evidence-coverage");
            Assert.Equal(270, coverage.GetProperty("staleAfterMinutes").GetInt32());
            Assert.Equal(17, coverage.GetProperty("items").GetArrayLength());
            var cycleEvidence = coverage.GetProperty("items").EnumerateArray().Single(x => x.GetProperty("metric").GetString() == "power_cycles");
            Assert.Equal(42, cycleEvidence.GetProperty("latestObserved").GetProperty("value").GetInt32());
            Assert.Equal("cycles", cycleEvidence.GetProperty("latestObserved").GetProperty("unit").GetString());
            var pendingCoverage = coverage.GetProperty("items").EnumerateArray().Single(x => x.GetProperty("metric").GetString() == "ata.pending_sectors");
            Assert.Equal("fresh", pendingCoverage.GetProperty("freshness").GetString());
            Assert.Equal(3, pendingCoverage.GetProperty("latestObserved").GetProperty("value").GetDouble());
            Assert.Equal(4, pendingCoverage.GetProperty("latestSubmission").GetProperty("value").GetDouble());
            Assert.EndsWith("Z", coverage.GetProperty("evaluatedUtc").GetString());
            Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/v1/disks/{Guid.NewGuid()}/evidence-coverage")).StatusCode);

            await using (var verification = new HomelabDb(options)) {
                Assert.Equal(9, await verification.Observations.CountAsync()); Assert.Equal(1, await verification.SmartReports.CountAsync());
                Assert.All(await verification.Observations.ToListAsync(), x => Assert.Equal(credentials.username, x.RecordedBy));
                Assert.Equal(4, (await verification.Observations.SingleAsync(x => x.Metric == "ata.pending_sectors" && x.Source == SmartReports.Source)).Value);
                Assert.Contains(": 3 sectors", (await verification.Findings.SingleAsync(x => x.Rule != SelfTestFindings.Rule)).Evidence);
                await verification.Database.ExecuteSqlRawAsync("CREATE TRIGGER fail_report BEFORE INSERT ON Observations WHEN NEW.Metric = 'temperature' BEGIN SELECT RAISE(ABORT, 'fixture rollback'); END;");
            }
            var failedRoot = JsonNode.Parse(reportJson)!;
            failedRoot["ata_smart_self_test_log"]!["standard"]!["table"]![0]!["lifetime_hours"] = 12001;
            failedRoot["temperature"]!["current"] = 33;
            failedRoot["ata_smart_attributes"]!["table"]![0]!["raw"]!["value"] = 9;
            failedRoot["local_time"]!["time_t"] = DateTimeOffset.UtcNow.AddSeconds(-10).ToUnixTimeSeconds();
            var failedInput = reportInput with { Json = failedRoot.ToJsonString() };
            var failedPreview = (await (await client.PostAsJsonAsync(reportPath + "/preview", failedInput)).Content.ReadFromJsonAsync<SmartReportPreview>())!;
            failedInput = failedInput with { ExpectedHash = failedPreview.ReportHash };
            Assert.Equal(HttpStatusCode.InternalServerError, (await client.PostAsJsonAsync(reportPath, failedInput)).StatusCode);
            await using (var verification = new HomelabDb(options)) {
                Assert.Equal(9, await verification.Observations.CountAsync()); Assert.Equal(1, await verification.SmartReports.CountAsync());
                Assert.Equal(1, await verification.Activity.CountAsync(x => x.Action == "SMART report imported"));
                Assert.Contains(": 3 sectors", (await verification.Findings.SingleAsync(x => x.Rule != SelfTestFindings.Rule)).Evidence);
                Assert.Single(await verification.SelfTestEvidence.ToListAsync());
                Assert.Single(await verification.Findings.Where(x => x.Rule == SelfTestFindings.Rule).ToListAsync());
                Assert.Single(await verification.Activity.Where(x => x.Action == "Imported self-test failure flagged").ToListAsync());
                await verification.Database.ExecuteSqlRawAsync("DROP TRIGGER fail_report;");
            }
            Guid selfTestFindingId;
            await using (var db = new HomelabDb(options)) selfTestFindingId = (await db.Findings.SingleAsync(x => x.Rule == SelfTestFindings.Rule)).Id;
            var resolvePath = $"/api/v1/findings/{selfTestFindingId}";
            var resolutionDetails = await client.GetFromJsonAsync<JsonElement>(resolvePath);
            var resolutionInput = new FindingResolutionInput(resolutionDetails.GetProperty("resolutionToken").GetString()!,"Reviewed retained failed-test report and independent recovery evidence.",true);
            Assert.Equal(HttpStatusCode.BadRequest,(await client.PostAsJsonAsync(resolvePath+"/resolve",resolutionInput with { Evidence=" " })).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest,(await client.PostAsJsonAsync(resolvePath+"/resolve",resolutionInput with { ReviewedCurrentEvidence=false })).StatusCode);
            Assert.Equal(HttpStatusCode.OK,(await client.PostAsJsonAsync(resolvePath+"/acknowledge",new {})).StatusCode);
            Assert.Equal(HttpStatusCode.Conflict,(await client.PostAsJsonAsync(resolvePath+"/resolve",resolutionInput)).StatusCode);
            resolutionDetails = await client.GetFromJsonAsync<JsonElement>(resolvePath);
            resolutionInput = resolutionInput with { ResolutionToken=resolutionDetails.GetProperty("resolutionToken").GetString()! };
            Assert.Equal(HttpStatusCode.OK,(await client.PostAsJsonAsync(resolvePath+"/resolve",resolutionInput)).StatusCode);
            Assert.Equal(HttpStatusCode.Conflict,(await client.PostAsJsonAsync(resolvePath+"/resolve",resolutionInput)).StatusCode);
            await using (var db = new HomelabDb(options)) {
                var resolved = await db.Findings.FindAsync(selfTestFindingId);
                Assert.Equal("resolved",resolved!.State);Assert.Equal(resolutionInput.Evidence,resolved.ResolutionEvidence);Assert.Null(resolved.ReviewUtc);Assert.Null(resolved.SnoozedUntilUtc);
                Assert.Equal(9,await db.Observations.CountAsync());Assert.Single(await db.SelfTestEvidence.ToListAsync());
                var audit = await db.Activity.SingleAsync(x=>x.Action=="Finding manually resolved");Assert.Equal(credentials.username,audit.Actor);Assert.Contains(resolutionInput.Evidence,audit.Detail);
                Assert.False((await db.Connections.FindAsync(nas.Id))!.Enabled);
            }
            var nvmeRoot = JsonNode.Parse(reportJson)!;
            nvmeRoot["device"]!["protocol"] = "NVMe"; nvmeRoot["serial_number"] = otherDisk.Serial;
            nvmeRoot["nvme_smart_health_information_log"] = new JsonObject { ["media_errors"]=0, ["critical_warning"]=8, ["available_spare"]=97, ["available_spare_threshold"]=10, ["percentage_used"]=130 };
            nvmeRoot["smart_status"] = new JsonObject { ["passed"] = true };
            var nvmePath = $"/api/v1/disks/{otherDisk.Id}/smart-reports";
            var nvmeInput = new SmartReportInput(nvmeRoot.ToJsonString(), "nvme-continuity");
            var nvmePreview = (await (await client.PostAsJsonAsync(nvmePath + "/preview",nvmeInput)).Content.ReadFromJsonAsync<SmartReportPreview>())!;
            Assert.Equal(8,nvmePreview.Readings.Length); Assert.EndsWith("-v5",nvmePreview.SourceSchema);
            await using (var legacy = new HomelabDb(options)) {
                legacy.SmartReports.Add(new() { DiskId=otherDisk.Id, ReportHash=nvmePreview.LegacyReportHash!, Actor="legacy fixture", CuratedJson="{}" });
                await legacy.SaveChangesAsync();
            }
            nvmeInput = nvmeInput with {ExpectedHash=nvmePreview.ReportHash, ConfirmedSerial=otherDisk.Serial};
            Assert.Equal(HttpStatusCode.Conflict,(await client.PostAsJsonAsync(nvmePath,nvmeInput)).StatusCode);
            await using (var verification = new HomelabDb(options)) Assert.Equal(0,await verification.Observations.CountAsync(x=>x.DiskId==otherDisk.Id));
            await using (var legacy = new HomelabDb(options)) {
                (await legacy.SmartReports.SingleAsync(x => x.DiskId == otherDisk.Id)).ReportHash = nvmePreview.PreviousReportHash!;
                await legacy.SaveChangesAsync();
            }
            Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync(nvmePath, nvmeInput)).StatusCode);
            await using (var verification = new HomelabDb(options)) Assert.Equal(0, await verification.Observations.CountAsync(x => x.DiskId == otherDisk.Id));
            await using (var legacy = new HomelabDb(options)) {
                (await legacy.SmartReports.SingleAsync(x => x.DiskId == otherDisk.Id)).ReportHash = nvmePreview.BeforeOverallReportHash!;
                await legacy.SaveChangesAsync();
            }
            Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync(nvmePath, nvmeInput)).StatusCode);
            await using (var verification = new HomelabDb(options)) Assert.Equal(0, await verification.Observations.CountAsync(x => x.DiskId == otherDisk.Id));
            await using (var legacy = new HomelabDb(options)) {
                (await legacy.SmartReports.SingleAsync(x => x.DiskId == otherDisk.Id)).ReportHash = nvmePreview.BeforeDeviceInfoHash!;
                await legacy.SaveChangesAsync();
            }
            Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync(nvmePath, nvmeInput)).StatusCode);
            await using (var verification = new HomelabDb(options)) Assert.Equal(0, await verification.Observations.CountAsync(x => x.DiskId == otherDisk.Id));
            var nvmeTime = nvmeRoot["local_time"]!["time_t"]!.GetValue<long>();
            nvmeRoot["firmware_version"] = "FW01";
            nvmeRoot["nvme_version"] = new JsonObject { ["string"] = "1.4" };
            for(var sequence=1;sequence<=3;sequence++) {
                nvmeRoot["local_time"]!["time_t"] = nvmeTime + sequence;
                nvmeRoot["smart_status"]!["passed"] = sequence != 1;
                nvmeRoot["nvme_smart_health_information_log"]!["critical_warning"] = sequence == 1 ? 8 : 0;
                nvmeInput = nvmeInput with {Json=nvmeRoot.ToJsonString()};
                nvmePreview = (await (await client.PostAsJsonAsync(nvmePath + "/preview",nvmeInput)).Content.ReadFromJsonAsync<SmartReportPreview>())!;
                nvmeInput = nvmeInput with {ExpectedHash=nvmePreview.ReportHash};
                Assert.Equal(HttpStatusCode.OK,(await client.PostAsJsonAsync(nvmePath,nvmeInput)).StatusCode);
                await using var verification = new HomelabDb(options);
                var savedOverall = System.Text.Json.JsonSerializer.Deserialize<SmartReportPreview>(
                    (await verification.SmartReports.OrderByDescending(x => x.ImportedUtc).FirstAsync(x => x.DiskId == otherDisk.Id)).CuratedJson);
                Assert.Equal(sequence != 1, savedOverall!.OverallSmart!.Passed);
                Assert.Equal("FW01",savedOverall.DeviceInfo!.Firmware);
                Assert.Equal("1.4",savedOverall.DeviceInfo.Interface);
                Assert.Equal("nvme_version.string",savedOverall.DeviceInfo.InterfaceField);
                Assert.Single(await verification.Findings.Where(x => x.DiskId == otherDisk.Id && x.Rule == "smart-overall-failed-v1" && x.State == "open").ToListAsync());
                var warning = await verification.Findings.SingleAsync(x=>x.DiskId==otherDisk.Id && x.Rule=="nvme-warning-v1");
                Assert.Equal(sequence == 3 ? "resolved" : "open",warning.State);
                Assert.Equal("critical",warning.Severity);
                Assert.Equal(1,await verification.Findings.CountAsync(x=>x.DiskId==otherDisk.Id && x.Rule=="nvme-endurance-v1" && x.State=="open"));
            }
            var scsiRoot = nvmeRoot.DeepClone();
            scsiRoot["device"]!["protocol"] = "SCSI";
            scsiRoot["scsi_model_name"] = otherDisk.Model;
            scsiRoot["scsi_grown_defect_list"] = 12;
            for (var sequence = 1; sequence <= 2; sequence++) {
                scsiRoot["local_time"]!["time_t"] = nvmeTime + 10 + sequence;
                scsiRoot["scsi_grown_defect_list"] = 11 + sequence;
                scsiRoot["scsi_error_counter_log"] = new JsonObject { ["read"] = new JsonObject { ["total_uncorrected_errors"] = sequence } };
                var input = new SmartReportInput(scsiRoot.ToJsonString(), "scsi-continuity");
                var preview = (await (await client.PostAsJsonAsync(nvmePath + "/preview", input)).Content.ReadFromJsonAsync<SmartReportPreview>())!;
                Assert.Equal("SCSI", preview.Protocol);
                if (sequence == 1) {
                    await using var legacy = new HomelabDb(options);
                    var oldReport = new SmartReport { DiskId = otherDisk.Id, ReportHash = preview.ScsiV1ReportHash!, Actor = "legacy SCSI", CuratedJson = "{}" };
                    legacy.SmartReports.Add(oldReport); await legacy.SaveChangesAsync();
                    var duplicate = input with { ExpectedHash = preview.ReportHash, ConfirmedSerial = otherDisk.Serial };
                    Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync(nvmePath, duplicate)).StatusCode);
                    Assert.Empty(await legacy.Observations.Where(x => x.DiskId == otherDisk.Id && x.Metric == "scsi.read_uncorrected_errors").ToListAsync());
                    legacy.SmartReports.Remove(oldReport); await legacy.SaveChangesAsync();
                }
                input = input with { ExpectedHash = preview.ReportHash, ConfirmedSerial = otherDisk.Serial };
                Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync(nvmePath, input)).StatusCode);
                Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync(nvmePath, input)).StatusCode);
                await using var check = new HomelabDb(options);
                Assert.Equal(sequence, await check.Observations.CountAsync(x => x.DiskId == otherDisk.Id && x.Metric == "scsi.grown_defects"));
                Assert.Equal(sequence - 1, await check.Findings.CountAsync(x => x.DiskId == otherDisk.Id && x.Rule == "scsi-grown-defects-increase-v1"));
                Assert.Equal(sequence - 1, await check.Findings.CountAsync(x => x.DiskId == otherDisk.Id && x.Rule == "scsi-uncorrected-increase-v1:scsi.read_uncorrected_errors"));
            }
            var tempSettings = new OperatingSettings { TemperatureReviewCelsius = 45 };
            Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync("/api/v1/settings", tempSettings)).StatusCode);
            tempSettings.TemperaturePolicyReason = "API fixture local review policy";
            Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync("/api/v1/settings", tempSettings)).StatusCode);
            var hotReading = new Observation { Metric = "temperature", Unit = "°C", Value = 55, Quality = "observed",
                ObservedUtc = DateTime.UtcNow.AddSeconds(-1), Source = "fixture", SourceSchema = "temperature-test",
                SourceKey = "temperature-policy-api", ResetEpoch = "" };
            Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync($"/api/v1/disks/{disk.Id}/observations", hotReading)).StatusCode);
            await using (var check = new HomelabDb(options)) {
                var policyFinding = await check.Findings.SingleAsync(x => x.DiskId == disk.Id && x.Rule == "local-temperature-policy-v1");
                Assert.Contains("API fixture local review policy", policyFinding.Evidence);
                Assert.Equal(45, (await check.Settings.SingleAsync()).TemperatureReviewCelsius);
            }
            tempSettings.TemperatureReviewCelsius = null;
            Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync("/api/v1/settings", tempSettings)).StatusCode);
            var hardwareResponse=await client.PostAsJsonAsync("/api/v1/inventory/assets",new InventoryAsset{Name="Shared HBA fixture",Type="controller"});
            Assert.Equal(HttpStatusCode.OK,hardwareResponse.StatusCode);var hardware=(await hardwareResponse.Content.ReadFromJsonAsync<InventoryAsset>())!;
            var hardwarePath=$"/api/v1/disks/{disk.Id}/hardware-paths";
            var hardwareInput=new DrivePathInput(Guid.NewGuid(),hardware.Id,hardware.UpdatedUtc,disk.Serial,true,"Private hardware cable-label evidence",DateTime.UtcNow.AddDays(30));
            Assert.Equal(HttpStatusCode.OK,(await client.PostAsJsonAsync(hardwarePath,hardwareInput)).StatusCode);
            Assert.Equal(HttpStatusCode.OK,(await client.PostAsJsonAsync(hardwarePath,hardwareInput)).StatusCode);
            Assert.Equal(HttpStatusCode.Conflict,(await client.PostAsJsonAsync(hardwarePath,hardwareInput with{Evidence="changed"})).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest,(await client.PostAsJsonAsync(hardwarePath,hardwareInput with{RequestId=Guid.NewGuid(),ConfirmedIdentity="wrong"})).StatusCode);
            Assert.Equal(HttpStatusCode.Conflict,(await client.PostAsJsonAsync(hardwarePath,hardwareInput with{RequestId=Guid.NewGuid(),AssetUpdatedUtc=hardware.UpdatedUtc.AddSeconds(-1)})).StatusCode);
            var peerInput=hardwareInput with{RequestId=Guid.NewGuid(),ConfirmedIdentity=otherDisk.Serial};
            Assert.Equal(HttpStatusCode.OK,(await client.PostAsJsonAsync($"/api/v1/disks/{otherDisk.Id}/hardware-paths",peerInput)).StatusCode);
            var shared=await client.GetFromJsonAsync<JsonElement>(hardwarePath);
            var peer=Assert.Single(shared.GetProperty("peers").EnumerateArray());Assert.Equal(otherDisk.Id,peer.GetProperty("diskId").GetGuid());Assert.NotEmpty(peer.GetProperty("findings").EnumerateArray());
            Assert.Equal("evidence-owner",shared.GetProperty("items")[0].GetProperty("record").GetProperty("author").GetString());
            Assert.DoesNotContain("Private hardware cable-label evidence",await client.GetStringAsync("/api/v1/inventory/export?redacted=true"));
            Assert.Contains("Private hardware cable-label evidence",await client.GetStringAsync("/api/v1/inventory/export?redacted=false"));
            Assert.Equal(HttpStatusCode.Conflict,(await client.PostAsJsonAsync(hardwarePath,hardwareInput with{RequestId=Guid.NewGuid(),Attached=false})).StatusCode);
            var headId=shared.GetProperty("items")[0].GetProperty("record").GetProperty("id").GetInt64();
            Assert.Equal(HttpStatusCode.OK,(await client.PostAsJsonAsync(hardwarePath,hardwareInput with{RequestId=Guid.NewGuid(),Attached=false,PreviousReviewId=headId})).StatusCode);
            shared=await client.GetFromJsonAsync<JsonElement>(hardwarePath);Assert.Empty(shared.GetProperty("peers").EnumerateArray());
            Assert.Equal("ended",shared.GetProperty("items")[0].GetProperty("status").GetString());Assert.Equal("superseded",shared.GetProperty("items")[1].GetProperty("status").GetString());
            await using(var check=new HomelabDb(options)){Assert.Equal(3,await check.DrivePaths.CountAsync());Assert.Equal(3,await check.Activity.CountAsync(x=>x.Action=="Drive hardware relationship recorded"));}
            var identityPath=$"/api/v1/disks/{disk.Id}/identity-history";
            var originalIdentity=await client.GetFromJsonAsync<JsonElement>(identityPath);
            var diskIdentityReview=await client.GetFromJsonAsync<JsonElement>(identityPath+$"/review?otherId={otherDisk.Id}");
            var mergeInput=new IdentityMergeInput(otherDisk.Id,Guid.NewGuid(),diskIdentityReview.GetProperty("reviewToken").GetString()!,disk.Serial,otherDisk.Serial,"Synthetic identity investigation; no physical action",true);
            Assert.Equal(HttpStatusCode.BadRequest,(await client.PostAsJsonAsync(identityPath+"/merge",mergeInput with{ConfirmedSamePhysicalDrive=false})).StatusCode);
            Assert.Equal(HttpStatusCode.Conflict,(await client.PostAsJsonAsync(identityPath+"/merge",mergeInput with{ReviewToken="stale"})).StatusCode);
            Dictionary<long,Guid?> observationOwners;
            await using(var check=new HomelabDb(options)){observationOwners=await check.Observations.ToDictionaryAsync(x=>x.Id,x=>x.DiskId);}
            var mergedResponse=await client.PostAsJsonAsync(identityPath+"/merge",mergeInput);Assert.Equal(HttpStatusCode.OK,mergedResponse.StatusCode);
            var merged=(await mergedResponse.Content.ReadFromJsonAsync<IdentityHistoryMerge>())!;
            Assert.Equal(HttpStatusCode.OK,(await client.PostAsJsonAsync(identityPath+"/merge",mergeInput)).StatusCode);
            Assert.Equal(HttpStatusCode.Conflict,(await client.PostAsJsonAsync(identityPath+"/merge",mergeInput with{Evidence="different"})).StatusCode);
            Assert.Equal(HttpStatusCode.Conflict,(await client.PostAsJsonAsync(identityPath+"/merge",mergeInput with{FirstIdentity="wrong"})).StatusCode);
            var grouped=await client.GetFromJsonAsync<JsonElement>(identityPath);Assert.Equal(2,grouped.GetProperty("members").GetArrayLength());
            Assert.Contains(grouped.GetProperty("observations").EnumerateArray(),x=>x.GetProperty("diskId").GetGuid()==otherDisk.Id);
            Assert.Contains(grouped.GetProperty("observations").EnumerateArray(),x=>x.GetProperty("diskId").GetGuid()==disk.Id);
            Assert.Equal(HttpStatusCode.Conflict,(await client.GetAsync(identityPath+$"/review?otherId={otherDisk.Id}")).StatusCode);
            var splitPath=identityPath+$"/{merged.Id}/reverse";
            Assert.Equal(HttpStatusCode.Conflict,(await client.PostAsJsonAsync(splitPath,new IdentitySplitInput("stale","Correct mistaken grouping"))).StatusCode);
            var splitInput=new IdentitySplitInput(grouped.GetProperty("reviewToken").GetString()!,"Correct mistaken grouping");
            Assert.Equal(HttpStatusCode.OK,(await client.PostAsJsonAsync(splitPath,splitInput)).StatusCode);
            Assert.Equal(HttpStatusCode.OK,(await client.PostAsJsonAsync(splitPath,splitInput)).StatusCode);
            var splitPage=await client.GetFromJsonAsync<JsonElement>(identityPath);Assert.Equal(1,splitPage.GetProperty("members").GetArrayLength());Assert.All(splitPage.GetProperty("observations").EnumerateArray(),x=>Assert.Equal(disk.Id,x.GetProperty("diskId").GetGuid()));
            await using(var check=new HomelabDb(options)){var after=await check.Observations.ToDictionaryAsync(x=>x.Id,x=>x.DiskId);Assert.Equal(observationOwners.OrderBy(x=>x.Key),after.OrderBy(x=>x.Key));Assert.Equal(1,await check.IdentityMerges.CountAsync());Assert.Equal(1,await check.Activity.CountAsync(x=>x.Action=="Identity histories manually merged"));Assert.Equal(1,await check.Activity.CountAsync(x=>x.Action=="Identity history merge reversed"));}
            Assert.DoesNotContain(mergeInput.Evidence,await client.GetStringAsync("/api/v1/inventory/export?redacted=true"));
            Assert.Contains(mergeInput.Evidence,await client.GetStringAsync("/api/v1/inventory/export?redacted=false"));
            var discoveryPath=$"/api/v1/connections/{nas.Id}/identity-review";
            await using(var check=new HomelabDb(options)) {
                var profile=await check.Connections.SingleAsync(x=>x.Id==nas.Id);profile.Enabled=true;profile.NextAttemptUtc=DateTime.UtcNow.AddYears(1);
                profile.DiscoveryIdentityJson=DiscoveryAssignments.Capture(profile.ConfigurationRevision,DateTime.UtcNow,[new("DISCOVERY-NEW","fixture-model","5000c50099887766","sdc")]);await check.SaveChangesAsync();
            }
            var discovery=await client.GetFromJsonAsync<JsonElement>(discoveryPath);Assert.True(discovery.GetProperty("usable").GetBoolean());
            var assignmentInput=new DiscoveryAssignmentInput(Guid.NewGuid(),discovery.GetProperty("reviewToken").GetString()!,"5000c50099887766","5000c50099887766",null,"Private independent label review",DateTime.UtcNow.AddDays(30),true);
            Assert.Equal(HttpStatusCode.BadRequest,(await client.PostAsJsonAsync(discoveryPath,assignmentInput with{ConfirmedPhysicalIdentity=false})).StatusCode);
            Assert.Equal(HttpStatusCode.Conflict,(await client.PostAsJsonAsync(discoveryPath,assignmentInput with{ReviewToken="stale"})).StatusCode);
            var assignedResponse=await client.PostAsJsonAsync(discoveryPath,assignmentInput);Assert.Equal(HttpStatusCode.OK,assignedResponse.StatusCode);var assigned=(await assignedResponse.Content.ReadFromJsonAsync<DiscoveryAssignment>())!;
            Assert.Equal(HttpStatusCode.OK,(await client.PostAsJsonAsync(discoveryPath,assignmentInput)).StatusCode);Assert.Equal(HttpStatusCode.Conflict,(await client.PostAsJsonAsync(discoveryPath,assignmentInput with{Evidence="different"})).StatusCode);
            var revokePath=discoveryPath+$"/{assigned.Id}/revoke";Assert.Equal(HttpStatusCode.OK,(await client.PostAsJsonAsync(revokePath,new DiscoveryRevokeInput("Physical identity needs another review"))).StatusCode);
            Assert.Equal(HttpStatusCode.OK,(await client.PostAsJsonAsync(revokePath,new DiscoveryRevokeInput("Physical identity needs another review"))).StatusCode);
            Assert.DoesNotContain(assignmentInput.Evidence,await client.GetStringAsync("/api/v1/inventory/export?redacted=true"));Assert.Contains(assignmentInput.Evidence,await client.GetStringAsync("/api/v1/inventory/export?redacted=false"));
            await using(var check=new HomelabDb(options)){var profile=await check.Connections.SingleAsync(x=>x.Id==nas.Id);profile.Enabled=false;await check.SaveChangesAsync();var assignedDisk=await check.Disks.SingleAsync(x=>x.Id==assigned.DiskId);Assert.Equal("ambiguous",assignedDisk.MappingConfidence);Assert.False(assignedDisk.Present);Assert.False(assignedDisk.BayVerified);Assert.Equal(1,await check.DiscoveryAssignments.CountAsync());}
            // 001: exercise the actual storage routes and database, including rejected
            // drafts, local-only writes, identity confirmation and replacement dating.
            var storageDisk = new Disk { ConnectionId=nas.Id, Serial="STORAGE-ONE", Model="WDC model", IdentityKey="storage-one", Alias="sde" };
            var storageNew = new Disk { ConnectionId=nas.Id, Serial="STORAGE-TWO", Model="WDC model", IdentityKey="storage-two", Alias="sdf" };
            await using(var check=new HomelabDb(options)){check.Disks.AddRange(storageDisk,storageNew);await check.SaveChangesAsync();}
            var storageBase=$"/api/v1/storage/drives/{storageDisk.Id}";
            var draft=new StorageProfileInput(null,"Owner name",123.45m,DateTime.UtcNow.AddYears(-1),DateTime.UtcNow.AddMonths(-6),DateTime.UtcNow.AddYears(2),true,"Local note");
            Assert.Equal(HttpStatusCode.BadRequest,(await client.PutAsJsonAsync(storageBase+"/profile",draft with {PurchaseCost=-1})).StatusCode);
            Assert.Equal(HttpStatusCode.OK,(await client.PutAsJsonAsync(storageBase+"/profile",draft)).StatusCode);
            Assert.Equal(HttpStatusCode.Conflict,(await client.PutAsJsonAsync(storageBase+"/profile",draft with {Notes="stale draft"})).StatusCode);
            Assert.Equal(HttpStatusCode.OK,(await client.PutAsJsonAsync(storageBase+"/nickname",new StorageNicknameInput("Renamed drive"))).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest,(await client.PutAsJsonAsync(storageBase+"/location",new StorageLocationInput("Bay 3","WRONG","Checked label"))).StatusCode);
            Assert.Equal(HttpStatusCode.OK,(await client.PutAsJsonAsync(storageBase+"/location",new StorageLocationInput("Bay 3","STORAGE-ONE","Checked label"))).StatusCode);
            var note=new StorageNoteInput(Guid.NewGuid(),"Inspected physical bay label.");
            Assert.Equal(HttpStatusCode.OK,(await client.PostAsJsonAsync(storageBase+"/notes",note)).StatusCode);
            Assert.Equal(HttpStatusCode.OK,(await client.PostAsJsonAsync(storageBase+"/notes",note)).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound,(await client.GetAsync(storageBase+"/trends")).StatusCode);
            var storagePageData=await client.GetFromJsonAsync<StoragePageData>("/api/v1/storage/overview");Assert.False(storagePageData!.SmartEnabled);
            var savedDrive=storagePageData.Servers.Single(x=>x.Id==nas.Id).LooseDrives.Single(x=>x.Disk.Id==storageDisk.Id);
            Assert.Equal("Renamed drive",savedDrive.Title);Assert.True(savedDrive.BayConfirmed);Assert.Equal(123.45m,savedDrive.Acquisition!.PurchaseCost);Assert.Equal("Local note",savedDrive.Disk.Notes);
            var storageHistory=await client.GetFromJsonAsync<StorageHistoryItem[]>(storageBase+"/history");Assert.Single(storageHistory!,x=>x.Text==note.Text);
            var replacementTime=DateTime.UtcNow;
            Assert.Equal(HttpStatusCode.OK,(await client.PostAsJsonAsync("/api/v1/replacements",new ReplacementInput(storageDisk.Id,storageNew.Id,replacementTime,"documented","","Membership unknown","Owner replacement","Physical labels compared","STORAGE-ONE","STORAGE-TWO"))).StatusCode);
            await using(var check=new HomelabDb(options)){Assert.Equal(replacementTime,(await check.DriveAcquisitions.SingleAsync(x=>x.DiskId==storageNew.Id)).InstalledUtc);}
            var recordedReplacements = await client.GetFromJsonAsync<DiskReplacement[]>($"/api/v1/disks/{storageDisk.Id}/replacements");
            Assert.Single(recordedReplacements!, x => x.NewDiskId == storageNew.Id && x.OccurredUtc == replacementTime);
            var beforeSave = DateTime.UtcNow;
            var currentReplacement = await client.PostAsJsonAsync("/api/v1/replacements", new
            {
                oldDiskId = storageDisk.Id, newDiskId = storageNew.Id, useServerTime = true,
                dateConfidence = "documented", poolGuid = "", membershipEvidence = "Membership unknown",
                reason = "Owner replacement", evidence = "Physical labels compared",
                confirmedOldIdentity = "STORAGE-ONE", confirmedNewIdentity = "STORAGE-TWO"
            });
            Assert.Equal(HttpStatusCode.OK, currentReplacement.StatusCode);
            var savedReplacement = (await currentReplacement.Content.ReadFromJsonAsync<DiskReplacement>())!;
            Assert.InRange(savedReplacement.OccurredUtc, beforeSave, DateTime.UtcNow);
            Assert.Equal(savedReplacement.RecordedUtc, savedReplacement.OccurredUtc);
            await using (var check = new HomelabDb(options))
                Assert.Equal(savedReplacement.OccurredUtc, (await check.DriveAcquisitions.SingleAsync(x => x.DiskId == storageNew.Id)).InstalledUtc);
            var replacedHistory = await client.GetFromJsonAsync<StorageHistoryItem[]>(storageBase+"/history");
            Assert.Contains(replacedHistory!, x => x.Kind == "replacement" && x.Text == "Replaced by STORAGE-TWO");
            Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/v1/logout", new { })).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized,(await client.GetAsync("/api/v1/storage/overview")).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync($"/api/v1/disks/{disk.Id}/observations")).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync($"/api/v1/disks/{disk.Id}/observation-history?metric=temperature&days=7")).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync($"/api/v1/disks/{disk.Id}/maintenance")).StatusCode);
        } finally {
            if (process is not null) {
                if (processStarted && !process.HasExited) { process.Kill(entireProcessTree: true); await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10)); }
                process.Dispose();
            }
            Directory.Delete(folder, recursive: true);
        }
    }
    private static string FindRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "Homelab.sln"))) return directory.FullName;
        throw new DirectoryNotFoundException("Homelab.sln was not found above the test output directory.");
    }
}
