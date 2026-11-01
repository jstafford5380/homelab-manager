using System.Text.Json;
using Homelab.Api;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
namespace Homelab.Tests;

public sealed class IndependentSourceTests
{
    [Theory]
    [InlineData("disk.query", "inventory")]
    [InlineData("pool.query", "pools")]
    [InlineData("alert.list", "alerts")]
    public async Task DeniedSourceRetainsEvidenceWhileOtherSourcesAdvance(string method, string capabilityName)
    {
        await using var f = new Fixture(); await f.Collect();
        var caps = await f.Db.Capabilities.ToDictionaryAsync(x => x.Name);
        var old = DateTime.UtcNow.AddHours(-1);
        foreach (var cap in caps.Values) cap.LastSuccessUtc = old;
        var disk = await f.Db.Disks.SingleAsync(); disk.LastSeenUtc = old;
        var topology = f.Nas.TopologyJson; var poolCount = await f.Db.PoolSnapshots.CountAsync();
        var native = await f.Db.NativeAlerts.SingleAsync(); native.LastSeenUtc = old;
        f.Nas.NextAttemptUtc = null; await f.Db.SaveChangesAsync();
        f.Clients.Denied.Add(method);
        await f.Collect();
        Assert.Equal("denied", caps[capabilityName].Status); Assert.Equal(old, caps[capabilityName].LastSuccessUtc);
        foreach (var name in new[] { "inventory", "pools", "alerts" }.Where(x => x != capabilityName)) {
            Assert.Equal("supported", caps[name].Status); Assert.True(caps[name].LastSuccessUtc > old);
        }
        Assert.Equal(0, f.Nas.ConsecutiveFailures); Assert.Contains("Partial", f.Nas.Status);
        Assert.True(disk.Present);
        if (method == "disk.query") Assert.Equal(old, disk.LastSeenUtc);
        if (method == "pool.query") { Assert.Equal(topology, f.Nas.TopologyJson); Assert.Equal(poolCount, await f.Db.PoolSnapshots.CountAsync()); }
        if (method == "alert.list") { Assert.Equal(old, native.LastSeenUtc); Assert.Null(native.AbsentSinceUtc); }
        Assert.Equal("partial", (await f.Db.CollectionRuns.OrderByDescending(x => x.Id).FirstAsync()).Result);
    }

    [Theory]
    [InlineData("disk.query", "inventory", "[null]")]
    [InlineData("disk.query", "inventory", "[{}]")]
    [InlineData("disk.query", "inventory", "[{\"serial\":3,\"model\":\"model\",\"name\":\"sda\"}]")]
    [InlineData("pool.query", "pools", "[{\"guid\":\"pool\",\"status\":\"ONLINE\"},{\"guid\":\"pool\",\"status\":\"ONLINE\"}]")]
    [InlineData("pool.query", "pools", "[{\"status\":\"ONLINE\"}]")]
    [InlineData("pool.query", "pools", "[{\"guid\":\"pool\",\"status\":\"ONLINE\",\"topology\":{\"data\":[],\"data\":[]}}]")]
    [InlineData("alert.list", "alerts", "[{\"formatted\":\"Missing id\"}]")]
    [InlineData("alert.list", "alerts", "[{\"id\":\"alert\"},{\"id\":\"alert\"}]")]
    public async Task MalformedSourceCannotEraseInventoryClearAlertsOrResolvePoolFinding(string method, string name, string payload)
    {
        await using var f = new Fixture(); f.Clients.Pools = "[{\"guid\":\"pool\",\"name\":\"tank\",\"status\":\"DEGRADED\"}]"; await f.Collect();
        var finding = await f.Db.Findings.SingleAsync(); finding.HealthySamples = 1;
        var capability = await f.Db.Capabilities.SingleAsync(x => x.Name == name); var success = capability.LastSuccessUtc;
        f.Nas.NextAttemptUtc = null; await f.Db.SaveChangesAsync();
        f.Clients.OverrideMethod = method; f.Clients.OverridePayload = payload;
        await f.Collect();
        Assert.Equal("unavailable", capability.Status); Assert.Equal(success, capability.LastSuccessUtc);
        Assert.True((await f.Db.Disks.SingleAsync()).Present); Assert.Null((await f.Db.NativeAlerts.SingleAsync()).AbsentSinceUtc);
        Assert.Equal("open", finding.State);
        if (method == "pool.query") Assert.Equal(1, finding.HealthySamples);
        Assert.Equal("partial", (await f.Db.CollectionRuns.OrderByDescending(x => x.Id).FirstAsync()).Result);
    }

    [Theory]
    [InlineData(60)]
    [InlineData(3600)]
    public async Task AllDeniedSourcesKeepLastSuccessAndBackOff(int pollSeconds)
    {
        await using var f = new Fixture(); f.Nas.PollSeconds = pollSeconds; await f.Db.SaveChangesAsync(); await f.Collect(); var last = f.Nas.LastSuccessUtc;
        f.Nas.NextAttemptUtc = null; await f.Db.SaveChangesAsync();
        f.Clients.Denied.UnionWith(["disk.query", "pool.query", "alert.list"]);
        await f.Collect();
        Assert.Equal(last, f.Nas.LastSuccessUtc); Assert.Equal(1, f.Nas.ConsecutiveFailures);
        Assert.True(f.Nas.NextAttemptUtc >= f.Nas.LastAttemptUtc!.Value.AddSeconds(f.Nas.PollSeconds));
        Assert.Equal("unavailable", (await f.Db.CollectionRuns.OrderByDescending(x => x.Id).FirstAsync()).Result);
        Assert.All(await f.Db.Capabilities.Where(x => x.Name == "inventory" || x.Name == "pools" || x.Name == "alerts").ToListAsync(), x => Assert.Equal("denied", x.Status));
    }
    [Fact]
    public async Task SkippedInventoryDoesNotTurnOtherwiseFailedCycleIntoSuccess()
    {
        await using var f = new Fixture(); await f.Collect();
        var inventory = await f.Db.Capabilities.SingleAsync(x => x.Name == "inventory"); var last = f.Nas.LastSuccessUtc; var attempt = inventory.LastAttemptUtc;
        f.Clients.Denied.UnionWith(["pool.query", "alert.list"]);
        await f.Collect();
        Assert.Equal(last, f.Nas.LastSuccessUtc); Assert.Equal(attempt, inventory.LastAttemptUtc);
        Assert.Equal("supported", inventory.Status); Assert.Equal(1, f.Clients.Calls.Count(x => x == "disk.query"));
        Assert.Equal("unavailable", (await f.Db.CollectionRuns.OrderByDescending(x => x.Id).FirstAsync()).Result);
    }
    [Fact]
    public async Task DuplicateOnlinePoolRowsCannotResolveFaultAndTwoLaterValidReadsCan()
    {
        await using var f = new Fixture(); f.Clients.Pools = "[{\"guid\":\"pool\",\"status\":\"DEGRADED\"}]"; await f.Collect();
        var finding = await f.Db.Findings.SingleAsync();
        f.Clients.Pools = "[{\"guid\":\"pool\",\"status\":\"ONLINE\"},{\"guid\":\"pool\",\"status\":\"ONLINE\"}]"; await f.Collect();
        Assert.Equal("open", finding.State); Assert.Equal(0, finding.HealthySamples);
        f.Clients.Pools = "[{\"guid\":\"pool\",\"status\":\"ONLINE\"}]"; await f.Collect();
        Assert.Equal("open", finding.State); Assert.Equal(1, finding.HealthySamples);
        await f.Collect(); Assert.Equal("resolved", finding.State);
    }

    [Theory]
    [InlineData("CORRUPT_DATA", "data corruption")]
    [InlineData("CORRUPT_POOL", "metadata corruption")]
    public async Task IntegrityFindingSurvivesOnlineOkMissingAndDeniedReads(string code, string title)
    {
        await using var f = new Fixture();
        f.Clients.Pools = $$"""[{"guid":"pool","name":"tank","status":"ONLINE","status_code":"{{code}}","status_detail":"private file path must not be stored"}]""";
        await f.Collect(); await f.Collect();
        var finding = await f.Db.Findings.SingleAsync();
        Assert.Equal("critical", finding.Severity); Assert.Contains(title, finding.Title);
        Assert.Contains("ONLINE", finding.Evidence); Assert.Contains(code, finding.Evidence);
        Assert.Contains("Do not clear errors", finding.NextStep);
        Assert.All(await f.Db.PoolSnapshots.ToListAsync(), row => Assert.Equal(code, row.StatusCode));
        Assert.DoesNotContain("private file", finding.Evidence + f.Nas.TopologyJson);
        f.Clients.Pools = """[{"guid":"pool","name":"tank","status":"ONLINE","status_code":"OK"}]""";
        await f.Collect(); await f.Collect();
        Assert.Equal("open", finding.State); Assert.Equal(0, finding.HealthySamples);
        f.Clients.Pools = """[{"guid":"pool","name":"tank","status":"ONLINE"}]""";
        await f.Collect();
        f.Clients.Denied.Add("pool.query"); await f.Collect();
        Assert.Equal("open", finding.State); Assert.Single(await f.Db.Findings.ToListAsync());
        FindingLifecycle.Resolve(finding, DateTime.UtcNow, "Operator reviewed independent recovery evidence.");
        await f.Db.SaveChangesAsync();
        f.Clients.Denied.Clear();
        f.Clients.Pools = $$"""[{"guid":"pool","name":"tank","status":"ONLINE","status_code":"{{code}}"}]""";
        await f.Collect();
        Assert.Equal(2, await f.Db.Findings.CountAsync());
        Assert.Single(await f.Db.Findings.Where(x => x.State != "resolved").ToListAsync());
    }

    [Theory]
    [InlineData("SUSPENDED")]
    [InlineData("OFFLINE")]
    public async Task UnavailablePoolStatesRequireCriticalReview(string state)
    {
        await using var f = new Fixture();
        f.Clients.Pools = $$"""[{"guid":"pool","name":"tank","status":"{{state}}"}]""";
        await f.Collect();
        var finding = await f.Db.Findings.SingleAsync();
        Assert.Equal("critical", finding.Severity); Assert.Contains(state, finding.Title);
    }

    [Theory]
    [InlineData("OK")]
    [InlineData("FAILING_DEV")]
    [InlineData("FUTURE_CODE")]
    public async Task OtherStatusCodesAndScanErrorsAreNotRelabelledAsPermanentCorruption(string code)
    {
        await using var f = new Fixture();
        f.Clients.Pools = $$$"""[{"guid":"pool","name":"tank","status":"ONLINE","status_code":"{{{code}}}","scan":{"errors":10}}]""";
        await f.Collect();
        Assert.Empty(await f.Db.Findings.ToListAsync());
        var snapshot = await f.Db.PoolSnapshots.SingleAsync();
        Assert.Equal(code, snapshot.StatusCode); Assert.Equal(10, snapshot.ScanErrors);
    }

    [Fact]
    public async Task IntegrityFindingsAreIsolatedByConnectionEvenWhenPoolGuidsMatch()
    {
        await using var f = new Fixture();
        var other = new NasConnection(); f.Db.Connections.Add(other); await f.Db.SaveChangesAsync();
        await PoolIntegrity.RecordAsync(f.Db, new PoolSnapshot { ConnectionId = other.Id, PoolGuid = "pool",
            Name = "other", StatusCode = "CORRUPT_POOL", FetchedUtc = DateTime.UtcNow }, default);
        await f.Db.SaveChangesAsync();
        f.Clients.Pools = """[{"guid":"pool","name":"tank","status":"ONLINE","status_code":"CORRUPT_DATA"}]""";
        await f.Collect();
        var findings = await f.Db.Findings.ToListAsync();
        Assert.Equal(2, findings.Count);
        Assert.Contains(findings, x => x.ConnectionId == other.Id && x.Title.Contains("other"));
        Assert.Contains(findings, x => x.ConnectionId == f.Nas.Id && x.Title.Contains("tank"));
    }

    [Fact]
    public async Task CollectorRecordsOneScanAcrossRepeatedReportsAndPreservesItWhenSourceIsDenied()
    {
        await using var f = new Fixture();
        f.Clients.Pools = """[{"guid":"pool","name":"tank","status":"ONLINE","scan":{"function":"SCRUB","state":"SCANNING","start_time":{"$date":1700000000000},"percentage":20,"errors":3}}]""";
        await f.Collect(); await f.Collect();
        Assert.Equal(2,await f.Db.PoolSnapshots.CountAsync());
        Assert.Equal("SCANNING",(await f.Db.PoolScans.SingleAsync()).ScanState);
        Assert.Equal(3,(await f.Db.PoolScans.SingleAsync()).ScanErrorsPeak);
        Assert.StartsWith("scan-errors-v1:",(await f.Db.Findings.SingleAsync()).Rule);
        f.Clients.Denied.Add("pool.query"); await f.Collect();
        Assert.Single(await f.Db.PoolScans.ToListAsync());
    }

    [Fact]
    public async Task CollectorStagesMemberIncreaseWithSourceContextAndNoNasWrites()
    {
        await using var f=new Fixture();
        f.Clients.OverrideMethod="system.info";
        f.Clients.OverridePayload="{\"version\":\"24.10.2\",\"system_serial\":\"NAS-ONE\",\"uptime_seconds\":10000}";
        f.Clients.Pools="[{\"guid\":\"pool\",\"name\":\"tank\",\"status\":\"ONLINE\",\"topology\":{\"data\":[{\"guid\":\"member\",\"type\":\"DISK\",\"stats\":{\"read_errors\":0}}]}}]";
        await f.Collect();
        Assert.NotNull((await f.Db.PoolSnapshots.SingleAsync()).SourceContextJson);
        f.Clients.Pools=f.Clients.Pools.Replace("read_errors\":0","read_errors\":7");
        await f.Collect();
        var finding=await f.Db.Findings.SingleAsync();Assert.StartsWith(VdevFindings.RulePrefix,finding.Rule);Assert.Contains("read 0 → 7 (+7)",finding.Evidence);
        Assert.Null(finding.DiskId);Assert.Equal(2,await f.Db.PoolSnapshots.CountAsync());
        Assert.All(f.Clients.Calls,method=>Assert.Contains(method,new[]{"system.info","disk.query","pool.query","alert.list"}));
    }

    [Fact]
    public async Task SameCycleInventoryMapsMemberButSkippedInventoryDoesNotReuseAlias()
    {
        await using var f=new Fixture();
        f.Clients.Pools="[{\"guid\":\"pool\",\"name\":\"tank\",\"status\":\"ONLINE\",\"topology\":{\"data\":[{\"guid\":\"member\",\"type\":\"DISK\",\"status\":\"ONLINE\",\"path\":\"/dev/disk/by-partuuid/fixture\",\"device\":\"sda1\",\"disk\":\"sda\"}]}}]";
        await f.Collect();
        var first=await f.Db.PoolSnapshots.SingleAsync();var link=Assert.Single(VdevEvidence.Read(first.VdevEvidenceJson!).Nodes).DriveMapping!;
        Assert.Equal("inferred",link.Status);Assert.Equal((await f.Db.Disks.SingleAsync()).Id,link.DiskId);
        await f.Collect();var latest=await f.Db.PoolSnapshots.OrderByDescending(x=>x.Id).FirstAsync();
        Assert.Equal("unavailable",Assert.Single(VdevEvidence.Read(latest.VdevEvidenceJson!).Nodes).DriveMapping!.Status);
        Assert.Equal("inferred",Assert.Single(VdevEvidence.Read(first.VdevEvidenceJson!).Nodes).DriveMapping!.Status);
    }

    [Fact]
    public async Task ResilverCorrelationRequiresFreshMappingAndUsesOnlyExistingReadMethods()
    {
        await using var f = new Fixture(); await f.Collect();
        var disk = await f.Db.Disks.SingleAsync();
        f.Db.Findings.Add(new() { ConnectionId = f.Nas.Id, DiskId = disk.Id, Rule = "transport-increase-v1", Evidence = "CRC count increased.", EvidenceObservedUtc = DateTime.UtcNow.AddDays(-2) });
        f.Clients.Pools = JsonSerializer.Serialize(new[] { new { guid = "pool", name = "tank", status = "ONLINE",
            scan = new { function = "RESILVER", state = "SCANNING", start_time = new Dictionary<string, long> { ["$date"] = DateTimeOffset.UtcNow.AddHours(-1).ToUnixTimeMilliseconds() } },
            topology = new { data = new[] { new { guid = "member", type = "DISK", status = "ONLINE", path = "/dev/sda1", device = "sda1", disk = "sda" } } } } });
        await f.Db.SaveChangesAsync(); await f.Collect(); // Inventory not due: no historical alias reuse.
        Assert.Empty(await f.Db.Findings.Where(x => x.Rule.StartsWith(ResilverFindings.RulePrefix)).ToListAsync());
        var inventory = await f.Db.Capabilities.SingleAsync(x => x.Name == "inventory");
        inventory.LastSuccessUtc = DateTime.UtcNow.AddHours(-1); await f.Db.SaveChangesAsync(); await f.Collect();
        var finding = await f.Db.Findings.SingleAsync(x => x.Rule.StartsWith(ResilverFindings.RulePrefix));
        Assert.Equal(disk.Id, finding.DiskId); Assert.Contains("CRC count increased", finding.Evidence);
        Assert.Equal("urgent", finding.Severity);
        Assert.All(f.Clients.Calls, method => Assert.Contains(method, new[] { "system.info", "disk.query", "pool.query", "alert.list" }));
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly SqliteConnection connection = new("Data Source=:memory:");
        private readonly ServiceProvider services = new ServiceCollection().BuildServiceProvider();
        public HomelabDb Db { get; }
        public NasConnection Nas { get; }
        public Clients Clients { get; } = new();
        private readonly Collector collector;
        public Fixture() {
            connection.Open(); Db = new(new DbContextOptionsBuilder<HomelabDb>().UseSqlite(connection).Options); Db.Database.EnsureCreated();
            var protection = new EphemeralDataProtectionProvider(); Nas = new() { ProtectedApiKey = protection.CreateProtector("nas-credentials").Protect("fixture") };
            Db.Connections.Add(Nas); Db.SaveChanges(); collector = new(services.GetRequiredService<IServiceScopeFactory>(), protection, NullLogger<Collector>.Instance, new WriteCoordinator(), Clients);
        }
        public Task Collect() => collector.CollectAsync(Nas, Db, default);
        public async ValueTask DisposeAsync() { collector.Dispose(); await Db.DisposeAsync(); await connection.DisposeAsync(); await services.DisposeAsync(); }
    }
    private sealed class Clients : INasReadClientFactory
    {
        public HashSet<string> Denied { get; } = [];
        public List<string> Calls { get; } = [];
        public string Pools { get; set; } = "[{\"guid\":\"pool\",\"name\":\"tank\",\"status\":\"ONLINE\"}]";
        public string? OverrideMethod { get; set; }
        public string OverridePayload { get; set; } = "[]";
        public INasReadClient Create() => new Client(this);
        private sealed class Client(Clients fixture) : INasReadClient
        {
            public Task ConnectAsync(NasConnection profile, string key, CancellationToken ct) => Task.CompletedTask;
            public Task<JsonElement> ReadAsync(string method, CancellationToken ct) {
                fixture.Calls.Add(method); if (fixture.Denied.Contains(method)) throw new NasReadException("denied");
                using var document = JsonDocument.Parse(method == fixture.OverrideMethod ? fixture.OverridePayload : method switch {
                    "system.info" => "{\"version\":\"TrueNAS-SCALE-24.10.2\"}",
                    "disk.query" => "[{\"serial\":\"serial\",\"model\":\"model\",\"name\":\"sda\"}]",
                    "pool.query" => fixture.Pools,
                    "alert.list" => "[{\"id\":\"alert\",\"level\":\"WARNING\",\"klass\":\"fixture\",\"formatted\":\"Review existing evidence\",\"dismissed\":false}]",
                    _ => throw new InvalidOperationException()
                }); return Task.FromResult(document.RootElement.Clone());
            }
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }
}
