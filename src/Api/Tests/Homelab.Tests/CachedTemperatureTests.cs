using System.Reflection;
using System.Text.Json;
using Homelab.Api;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Homelab.Tests;
public sealed class CachedTemperatureTests
{
    [Theory]
    [InlineData("disk.smart_attributes")]
    [InlineData("disk.smartctl")]
    [InlineData("disk.temperature")]
    [InlineData("smart.test.manual_test")]
    public async Task FreshDriveQueriesAndTestsAreRejectedBeforeNetwork(string method)
    {
        await using var client = new TrueNasClient();
        await Assert.ThrowsAsync<InvalidOperationException>(() => client.ReadAsync(method, default));
    }
    [Fact]
    public async Task PrivateRpcBoundaryRejectsMissingOrUnsafeTemperatureOptions()
    {
        await using var client = new TrueNasClient();
        var rpc = typeof(TrueNasClient).GetMethod("CallInternalAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
        foreach (object[] arguments in new object[][] { [], [Array.Empty<string>(), new { only_cached = false }], [Array.Empty<string>(), new { only_cached = true, powermode = "NEVER" }], [new[] { "sda" }, new { only_cached = true }] }) {
            var task = (Task<JsonElement>)rpc.Invoke(client, ["disk.temperatures", arguments, CancellationToken.None])!;
            await Assert.ThrowsAsync<InvalidOperationException>(() => task);
        }
        Assert.True(TrueNasClient.AllowsReadParameters("disk.temperatures", [Array.Empty<string>(), new { only_cached = true }]));
        Assert.False(TrueNasClient.AllowsReadParameters("disk.query", ["unreviewed-option"]));
    }
    [Fact]
    public void CuratedReadingsPreserveUnknownsAndHaveNoInventedObservationTimeOrDiskId()
    {
        using var data = JsonDocument.Parse("""{"sda":32,"sdb":null,"sdc":"35","sdd":3.5,"sde":-10,"sdf":0,"sdg":999999}""");
        var snapshot = CachedTemperatures.Normalize(data.RootElement, DateTime.UtcNow);
        Assert.Equal(7, snapshot.Readings.Length);
        Assert.Equal(32, snapshot.Readings[0].Celsius); Assert.Null(snapshot.Readings[1].Celsius); Assert.Null(snapshot.Readings[2].Celsius); Assert.Null(snapshot.Readings[3].Celsius);
        Assert.Equal(-10, snapshot.Readings[4].Celsius); Assert.Equal(0, snapshot.Readings[5].Celsius); Assert.Null(snapshot.Readings[6].Celsius);
        Assert.All(snapshot.Readings.Where(x => x.Celsius is null), x => Assert.NotNull(x.MissingReason));
        var json = JsonSerializer.Serialize(snapshot); Assert.DoesNotContain("ObservedUtc", json); Assert.DoesNotContain("DiskId", json);
    }
    [Theory]
    [InlineData("[]")]
    [InlineData("null")]
    [InlineData("{\"sda\":30,\"sda\":40}")]
    [InlineData("{\"../sda\":30}")]
    public void AmbiguousOrUnexpectedPayloadsAreRejected(string json)
    {
        using var data = JsonDocument.Parse(json);
        Assert.Throws<InvalidDataException>(() => CachedTemperatures.Normalize(data.RootElement, DateTime.UtcNow));
    }
    [Fact]
    public void EmptyCacheIsNotZeroFilledAndOversizedCacheIsRejected()
    {
        using var empty = JsonDocument.Parse("{}"); Assert.Empty(CachedTemperatures.Normalize(empty.RootElement, DateTime.UtcNow).Readings);
        var json = JsonSerializer.Serialize(Enumerable.Range(0, 257).ToDictionary(i => "disk" + i, _ => 30));
        using var many = JsonDocument.Parse(json); Assert.Throws<InvalidDataException>(() => CachedTemperatures.Normalize(many.RootElement, DateTime.UtcNow));
    }
    [Fact]
    public async Task CacheReadIsOptInIndependentAndNeverBecomesPhysicalDiskEvidence()
    {
        using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        await using var db = new HomelabDb(new DbContextOptionsBuilder<HomelabDb>().UseSqlite(connection).Options); await db.Database.MigrateAsync();
        var protection = new EphemeralDataProtectionProvider();
        var nas = new NasConnection { ProtectedApiKey = protection.CreateProtector("nas-credentials").Protect("fixture") };
        var settings = new OperatingSettings(); db.Connections.Add(nas); db.Settings.Add(settings); await db.SaveChangesAsync();
        using var services = new ServiceCollection().BuildServiceProvider(); var factory = new Factory();
        var collector = new Collector(services.GetRequiredService<IServiceScopeFactory>(), protection, NullLogger<Collector>.Instance, new WriteCoordinator(), factory);
        await collector.CollectAsync(nas, db, default);
        Assert.DoesNotContain("disk.temperatures", factory.Methods);
        Assert.Equal("disabled", (await db.Capabilities.SingleAsync(x => x.Name == "cached_temperatures")).Status);
        settings.ReadCachedTemperatures = true; await db.SaveChangesAsync();
        await collector.CollectAsync(nas, db, default);
        Assert.Contains("disk.temperatures", factory.Methods);
        var capability = await db.Capabilities.SingleAsync(x => x.Name == "cached_temperatures"); Assert.Equal("supported", capability.Status);
        var saved = nas.CachedTemperaturesJson; Assert.Contains("sda", saved!); var lastSuccess = capability.LastSuccessUtc;
        Assert.Empty(await db.Observations.ToListAsync()); Assert.Empty(await db.Disks.ToListAsync());
        Assert.Equal("unsupported", (await db.Capabilities.SingleAsync(x => x.Name == "smart")).Status);
        factory.Denied = true;
        await collector.CollectAsync(nas, db, default);
        Assert.Equal("denied", capability.Status); Assert.Equal(lastSuccess, capability.LastSuccessUtc); Assert.Equal(saved, nas.CachedTemperaturesJson);
        Assert.Equal("supported", (await db.Capabilities.SingleAsync(x => x.Name == "pools")).Status);
        factory.Denied = false; factory.Cache = "[]";
        await collector.CollectAsync(nas, db, default); Assert.Equal("unavailable", capability.Status); Assert.Equal(saved, nas.CachedTemperaturesJson);
        factory.Cache = "{}";
        await collector.CollectAsync(nas, db, default); Assert.Equal("supported", capability.Status);
        Assert.Empty(JsonSerializer.Deserialize<CachedTemperatureSnapshot>(nas.CachedTemperaturesJson!)!.Readings);
        Assert.Empty(await db.Findings.ToListAsync());
    }
    private sealed class Factory : INasReadClientFactory
    {
        public List<string> Methods { get; } = [];
        public bool Denied { get; set; }
        public string Cache { get; set; } = "{\"sda\":32}";
        public INasReadClient Create() => new Client(this);
        private sealed class Client(Factory factory) : INasReadClient
        {
            public Task ConnectAsync(NasConnection profile, string key, CancellationToken ct) => Task.CompletedTask;
            public Task<JsonElement> ReadAsync(string method, CancellationToken ct) {
                factory.Methods.Add(method);
                if (method == "disk.temperatures" && factory.Denied) throw new NasReadException("denied");
                using var document = JsonDocument.Parse(method switch { "system.info" => "{\"version\":\"TrueNAS-SCALE-24.10.2\"}", "disk.query" or "pool.query" or "alert.list" => "[]", "disk.temperatures" => factory.Cache, _ => throw new InvalidOperationException() });
                return Task.FromResult(document.RootElement.Clone());
            }
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }
}
