using System.Text.Json;
using Homelab.Api;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Homelab.Tests;

public sealed class CollectionScheduleTests
{
    [Fact] public async Task FrequentPoolPollsDoNotReadInventoryOrFreshenDiskEvidence()
    {
        await using var f = new Fixture(); await f.Collect();
        var inventory = await f.Db.Capabilities.SingleAsync(c => c.Name == "inventory");
        var success = inventory.LastSuccessUtc; var attempt = inventory.LastAttemptUtc;
        var lastSeen = (await f.Db.Disks.SingleAsync()).LastSeenUtc;
        await f.Collect();
        Assert.Equal(1, f.Clients.Calls.Count(x => x == "disk.query"));
        Assert.Equal(2, f.Clients.Calls.Count(x => x == "pool.query"));
        Assert.Equal(2, f.Clients.Calls.Count(x => x == "alert.list"));
        Assert.Equal(success, inventory.LastSuccessUtc); Assert.Equal(attempt, inventory.LastAttemptUtc);
        Assert.Equal(lastSeen, (await f.Db.Disks.SingleAsync()).LastSeenUtc);
        Assert.Equal(900, inventory.ExpectedSeconds);
    }
    [Fact] public async Task NewSessionReconcilesInventoryBeforeNormalCadenceIsDue()
    {
        await using var f = new Fixture(); await f.Collect();
        var disk = await f.Db.Disks.SingleAsync();var originalId=disk.Id;
        f.Clients.NewSession=true;f.Clients.DiskJson="""[{"serial":"SERIAL","model":"model","name":"sdc"}]""";
        await f.Collect();Assert.Equal(2,f.Clients.Calls.Count(x=>x=="disk.query"));Assert.Equal("sdc",disk.Alias);Assert.Equal(originalId,disk.Id);
        f.Clients.NewSession=false;await f.Collect();Assert.Equal(2,f.Clients.Calls.Count(x=>x=="disk.query"));
    }
    [Fact] public async Task DueInventoryPollReconcilesPathsAndAdvancesOnlyRealEvidence()
    {
        await using var f = new Fixture(); await f.Collect();
        var inventory = await f.Db.Capabilities.SingleAsync(c => c.Name == "inventory");
        inventory.LastSuccessUtc = DateTime.UtcNow.AddMinutes(-16); await f.Db.SaveChangesAsync();
        var diskId = (await f.Db.Disks.SingleAsync()).Id;
        f.Clients.DiskJson = """[{"serial":"SERIAL","model":"model","name":"sdb"}]""";
        await f.Collect();
        Assert.Equal(2, f.Clients.Calls.Count(x => x == "disk.query"));
        var disk = await f.Db.Disks.SingleAsync(); Assert.Equal(diskId, disk.Id); Assert.Equal("sdb", disk.Alias);
        Assert.True(inventory.LastSuccessUtc > DateTime.UtcNow.AddMinutes(-1));
    }
    [Fact] public async Task SettingsChangesAndConnectionEditsTakeEffectWithoutRestart()
    {
        await using var f = new Fixture(); await f.Collect();
        var inventory = await f.Db.Capabilities.SingleAsync(c => c.Name == "inventory");
        inventory.LastSuccessUtc = DateTime.UtcNow.AddMinutes(-2);
        f.Db.Settings.Add(new() { InventoryMinutes = 1 }); await f.Db.SaveChangesAsync();
        await f.Collect(); Assert.Equal(2, f.Clients.Calls.Count(x => x == "disk.query")); Assert.Equal(60, inventory.ExpectedSeconds);
        f.Nas.NextAttemptUtc = null; f.Nas.ConfigurationRevision = Guid.NewGuid(); await f.Db.SaveChangesAsync();
        await f.Collect(); Assert.Equal(3, f.Clients.Calls.Count(x => x == "disk.query"));
    }
    [Fact] public async Task EffectiveInventoryFreshnessCannotBeFasterThanConnectionPolling()
    {
        await using var f = new Fixture(); f.Nas.PollSeconds = 3600; await f.Db.SaveChangesAsync(); await f.Collect();
        var inventory = await f.Db.Capabilities.SingleAsync(c => c.Name == "inventory");
        Assert.Equal(3600, inventory.ExpectedSeconds);
        Assert.Equal("supported", inventory.EffectiveStatus(inventory.LastSuccessUtc!.Value.AddHours(2)));
        Assert.Equal("stale", inventory.EffectiveStatus(inventory.LastSuccessUtc.Value.AddHours(4)));
    }
    [Fact] public async Task FailedInventoryDoesNotMarkDrivesAbsentAndIsRetriedNextCycle()
    {
        await using var f = new Fixture(); await f.Collect();
        var inventory = await f.Db.Capabilities.SingleAsync(c => c.Name == "inventory");
        inventory.LastSuccessUtc = DateTime.UtcNow.AddHours(-1); await f.Db.SaveChangesAsync();
        var success = inventory.LastSuccessUtc;
        f.Clients.DenyInventory = true; await f.Collect();
        var failed = await f.Db.Capabilities.SingleAsync(c => c.Name == "inventory");
        Assert.Equal("denied", failed.Status); Assert.Equal(success, failed.LastSuccessUtc);
        Assert.Equal("supported", (await f.Db.Capabilities.SingleAsync(c => c.Name == "pools")).Status);
        Assert.True((await f.Db.Disks.SingleAsync()).Present);
        f.Clients.DenyInventory = false; await f.Collect();
        Assert.Equal(3, f.Clients.Calls.Count(x => x == "disk.query"));
        Assert.Equal("supported", (await f.Db.Capabilities.SingleAsync(c => c.Name == "inventory")).Status);
    }
    [Fact] public async Task MalformedInventoryDoesNotBecomeEmptySuccessfulSnapshot()
    {
        await using var f = new Fixture(); await f.Collect();
        var success = f.Nas.LastSuccessUtc;
        f.Nas.NextAttemptUtc = null; await f.Db.SaveChangesAsync(); f.Clients.DiskJson = "{}";
        await f.Collect();
        Assert.True((await f.Db.Disks.SingleAsync()).Present);
        Assert.True((await f.Db.Connections.SingleAsync()).LastSuccessUtc >= success);
        Assert.Equal("supported", (await f.Db.Capabilities.SingleAsync(c => c.Name == "pools")).Status);
        Assert.Equal("unavailable", (await f.Db.Capabilities.SingleAsync(c => c.Name == "inventory")).Status);
    }
    private sealed class Fixture : IAsyncDisposable
    {
        private readonly SqliteConnection connection = new("Data Source=:memory:");
        private readonly ServiceProvider services = new ServiceCollection().BuildServiceProvider();
        public HomelabDb Db { get; }
        public NasConnection Nas { get; }
        public FixtureClients Clients { get; } = new();
        private readonly Collector collector;
        public Fixture()
        {
            connection.Open(); Db = new(new DbContextOptionsBuilder<HomelabDb>().UseSqlite(connection).Options); Db.Database.EnsureCreated();
            var protection = new EphemeralDataProtectionProvider();
            Nas = new() { ProtectedApiKey = protection.CreateProtector("nas-credentials").Protect("fixture") }; Db.Connections.Add(Nas); Db.SaveChanges();
            collector = new(services.GetRequiredService<IServiceScopeFactory>(), protection, NullLogger<Collector>.Instance, new WriteCoordinator(), Clients);
        }
        public async Task Collect() => await collector.CollectAsync(await Db.Connections.SingleAsync(), Db, default);
        public async ValueTask DisposeAsync() { await Db.DisposeAsync(); await connection.DisposeAsync(); await services.DisposeAsync(); collector.Dispose(); }
    }
    private sealed class FixtureClients : INasReadClientFactory
    {
        public List<string> Calls { get; } = [];
        public string DiskJson { get; set; } = """[{"serial":"SERIAL","model":"model","name":"sda"}]""";
        public bool DenyInventory { get; set; }
        public bool NewSession { get; set; }
        public INasReadClient Create() => new Client(this);
        private sealed class Client(FixtureClients fixture) : INasReadClient
        {
            public bool RequiresInventoryReconciliation => fixture.NewSession;
            public Task ConnectAsync(NasConnection profile, string key, CancellationToken ct) => Task.CompletedTask;
            public Task<JsonElement> ReadAsync(string method, CancellationToken ct)
            {
                fixture.Calls.Add(method);
                if (method == "disk.query" && fixture.DenyInventory) throw new NasReadException("denied");
                using var doc = JsonDocument.Parse(method switch { "system.info" => """{"version":"TrueNAS-SCALE-24.10.2"}""", "disk.query" => fixture.DiskJson, "pool.query" or "alert.list" => "[]", _ => throw new InvalidOperationException() });
                return Task.FromResult(doc.RootElement.Clone());
            }
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }
}
