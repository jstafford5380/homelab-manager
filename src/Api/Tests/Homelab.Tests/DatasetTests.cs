using System.Reflection;
using System.Text.Json;
using Homelab.Api;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
namespace Homelab.Tests;
public sealed class DatasetTests
{
    [Theory]
    [InlineData("pool.dataset.create")]
    [InlineData("pool.dataset.update")]
    [InlineData("pool.dataset.delete")]
    [InlineData("pool.dataset.unlock")]
    public async Task MutationsAreRejectedBeforeNetwork(string method)
    {
        await using var client = new TrueNasClient();
        await Assert.ThrowsAsync<InvalidOperationException>(() => client.ReadAsync(method, default));
    }
    [Fact]
    public async Task PrivateRpcBoundaryRejectsUnboundedAndCallerChosenQueries()
    {
        await using var client = new TrueNasClient();
        var rpc = typeof(TrueNasClient).GetMethod("CallInternalAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
        foreach (object[] arguments in new object[][] { [], [Array.Empty<object>(), new { limit = 257 }], [Array.Empty<object>(), new { extra = new { retrieve_children = true } }] }) {
            var task = (Task<JsonElement>)rpc.Invoke(client, ["pool.dataset.query", arguments, CancellationToken.None])!;
            await Assert.ThrowsAsync<InvalidOperationException>(() => task);
        }
        Assert.True(TrueNasClient.AllowsReadParameters("pool.dataset.query", [Array.Empty<object>(), new {
            limit = 257, select = new[] { "id", "name", "pool", "type", "used", "available", "usedbysnapshots", "quota", "refquota" },
            extra = new { flat = true, retrieve_children = false, properties = new[] { "used", "available", "usedbysnapshots", "quota", "refquota" } }
        }]));
    }
    [Fact]
    public void CuratesUsageWithoutSecretsOrInventedIdentityAndPreservesUnknownVersusZero()
    {
        using var data = JsonDocument.Parse("""[{"id":"tank/photos","name":"tank/photos","pool":"tank","type":"FILESYSTEM","used":{"parsed":0},"available":{"parsed":1073741824},"usedbysnapshots":{"parsed":42},"quota":{"parsed":0},"refquota":{"parsed":null},"user_properties":{"secret":"do-not-store"},"children":[],"guid":{"parsed":"1234"}}]""");
        var snapshot = Datasets.Normalize(data.RootElement, DateTime.UtcNow);
        var item = Assert.Single(snapshot.Items); Assert.Equal(0, item.UsedBytes); Assert.Equal(1073741824, item.AvailableBytes);
        Assert.Equal(42, item.SnapshotUsedBytes); Assert.Equal(0, item.QuotaBytes); Assert.Null(item.RefQuotaBytes);
        var json = JsonSerializer.Serialize(snapshot); Assert.DoesNotContain("do-not-store", json); Assert.DoesNotContain("1234", json); Assert.DoesNotContain("children", json);
    }
    [Theory]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("[{\"id\":\"tank\",\"name\":\"tank\",\"pool\":\"other\",\"type\":\"FILESYSTEM\"}]")]
    [InlineData("[{\"id\":\"tank\",\"name\":\"tank\",\"pool\":\"tank\",\"type\":\"UNKNOWN\"}]")]
    [InlineData("[{\"id\":\"tank\",\"name\":\"tank\",\"name\":\"tank\",\"pool\":\"tank\",\"type\":\"FILESYSTEM\"}]")]
    public void InvalidShapeOrIdentityDoesNotReplaceLastInventory(string json)
    {
        using var data = JsonDocument.Parse(json);
        Assert.Throws<InvalidDataException>(() => Datasets.Normalize(data.RootElement, DateTime.UtcNow));
    }
    [Fact]
    public void BoundsAndDuplicateNamesAreRejectedWhileMissingNumbersRemainUnknown()
    {
        foreach (var names in new[] { Enumerable.Repeat("tank", 2), Enumerable.Range(0, 257).Select(i => "tank/d" + i) }) {
            using var data = JsonDocument.Parse(JsonSerializer.Serialize(names.Select(name => new { id = name, name, pool = "tank", type = "FILESYSTEM" })));
            Assert.Throws<InvalidDataException>(() => Datasets.Normalize(data.RootElement, DateTime.UtcNow));
        }
        using var invalid = JsonDocument.Parse("""[{"id":"tank/vm","name":"tank/vm","pool":"tank","type":"VOLUME","used":{"parsed":-1},"available":{"parsed":"1024"},"quota":{"parsed":9007199254740992},"usedbysnapshots":{"parsed":1.5}}]""");
        var item = Assert.Single(Datasets.Normalize(invalid.RootElement, DateTime.UtcNow).Items);
        Assert.Null(item.UsedBytes); Assert.Null(item.AvailableBytes); Assert.Null(item.QuotaBytes); Assert.Null(item.SnapshotUsedBytes); Assert.Null(item.RefQuotaBytes);
    }
    [Fact]
    public async Task DatasetReadIsOptInIndependentAndNeverBecomesPhysicalDiskEvidence()
    {
        using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        await using var db = new HomelabDb(new DbContextOptionsBuilder<HomelabDb>().UseSqlite(connection).Options); await db.Database.MigrateAsync();
        var protection = new EphemeralDataProtectionProvider();
        var nas = new NasConnection { ProtectedApiKey = protection.CreateProtector("nas-credentials").Protect("fixture") };
        var settings = new OperatingSettings(); db.Connections.Add(nas); db.Settings.Add(settings); await db.SaveChangesAsync();
        using var services = new ServiceCollection().BuildServiceProvider(); var factory = new Factory();
        var collector = new Collector(services.GetRequiredService<IServiceScopeFactory>(), protection, NullLogger<Collector>.Instance, new WriteCoordinator(), factory);
        await collector.CollectAsync(nas, db, default);
        Assert.DoesNotContain("pool.dataset.query", factory.Methods);
        Assert.Equal("disabled", (await db.Capabilities.SingleAsync(x => x.Name == "datasets")).Status);
        settings.ReadDatasets = true; await db.SaveChangesAsync();
        await collector.CollectAsync(nas, db, default);
        Assert.Contains("pool.dataset.query", factory.Methods);
        var capability = await db.Capabilities.SingleAsync(x => x.Name == "datasets"); Assert.Equal("supported", capability.Status);
        var saved = nas.DatasetsJson; Assert.Contains("tank", saved!); var lastSuccess = capability.LastSuccessUtc;
        Assert.Empty(await db.Observations.ToListAsync()); Assert.Empty(await db.Disks.ToListAsync());
        Assert.Equal("unsupported", (await db.Capabilities.SingleAsync(x => x.Name == "smart")).Status);
        factory.Denied = true;
        await collector.CollectAsync(nas, db, default);
        Assert.Equal("denied", capability.Status); Assert.Equal(lastSuccess, capability.LastSuccessUtc); Assert.Equal(saved, nas.DatasetsJson);
        Assert.Equal("supported", (await db.Capabilities.SingleAsync(x => x.Name == "pools")).Status);
        factory.Denied = false; factory.Cache = "{}";
        await collector.CollectAsync(nas, db, default); Assert.Equal("unavailable", capability.Status); Assert.Equal(saved, nas.DatasetsJson);
        factory.Cache = "[]";
        await collector.CollectAsync(nas, db, default); Assert.Equal("supported", capability.Status);
        Assert.Empty(JsonSerializer.Deserialize<DatasetSnapshot>(nas.DatasetsJson!)!.Items);
        Assert.Empty(await db.Findings.ToListAsync());
    }
    private sealed class Factory : INasReadClientFactory
    {
        public List<string> Methods { get; } = [];
        public bool Denied { get; set; }
        public string Cache { get; set; } = """[{"id":"tank","name":"tank","pool":"tank","type":"FILESYSTEM","used":{"parsed":0}}]""";
        public INasReadClient Create() => new Client(this);
        private sealed class Client(Factory factory) : INasReadClient
        {
            public Task ConnectAsync(NasConnection profile, string key, CancellationToken ct) => Task.CompletedTask;
            public Task<JsonElement> ReadAsync(string method, CancellationToken ct) {
                factory.Methods.Add(method);
                if (method == "pool.dataset.query" && factory.Denied) throw new NasReadException("denied");
                using var document = JsonDocument.Parse(method switch { "system.info" => "{\"version\":\"TrueNAS-SCALE-24.10.2\"}", "disk.query" or "pool.query" or "alert.list" => "[]", "pool.dataset.query" => factory.Cache, _ => throw new InvalidOperationException() });
                return Task.FromResult(document.RootElement.Clone());
            }
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }
}
