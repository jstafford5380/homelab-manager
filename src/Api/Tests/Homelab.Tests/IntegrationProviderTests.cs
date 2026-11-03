using System.Text.Json;
using Xunit;

namespace Homelab.Api.Tests;

public sealed class IntegrationProviderTests
{
    [Fact]
    public async Task RegistryResolvesByIdAndRejectsUnknownOrIncompatibleProviders()
    {
        var storage = new TrueNasProvider(new NasReadClientFactory());
        var hardware = new SynologyHardwareProvider(new SnmpReader());
        var registry = new ProviderRegistry([storage, hardware]);

        Assert.Same(storage, registry.Resolve<IStorageProvider>("truenas"));
        Assert.Same(hardware, registry.Resolve<IHardwareProvider>("synology"));
        Assert.Throws<InvalidOperationException>(() => registry.Resolve<IStorageProvider>("synology"));
        Assert.Throws<InvalidOperationException>(() => registry.Resolve<IStorageProvider>("unknown"));

        var sample = await hardware.ReadAsync("nas", "test-only", default);
        Assert.NotNull(sample);
        Assert.Equal("DS920+", Assert.Single(sample.Inventory.Components, c => c.Kind == "nas-unit").Name);
        Assert.Equal(12, sample.Reading?.CpuPercent);
    }

    [Fact]
    public async Task StorageAdapterPreservesSourceOutcomesAndTranslatesEvidence()
    {
        var fixture = new StorageClient();
        var provider = new TrueNasProvider(new StorageFactory(fixture));
        await using var session = provider.CreateSession();

        var system = await session.ReadSystemAsync(default);
        Assert.True(system.SupportedRelease);
        Assert.Equal("NAS-123", system.Serial);
        Assert.Equal("CPU model", Assert.Single(system.Hardware.Components).Name);

        var empty = await session.ReadDisksAsync(default);
        Assert.Equal("supported", empty.Status);
        Assert.Empty(empty.Data!);

        fixture.Disks = JsonSerializer.SerializeToElement(new[] { new { name = "sda", serial = "DISK-1", model = "Drive", size = 1024 } });
        var disk = Assert.Single((await session.ReadDisksAsync(default)).Data!);
        Assert.Equal(new IdentityCandidate("DISK-1", "Drive", "", "sda"), disk.Identity);
        Assert.Equal(1024, disk.SizeBytes);

        var unsupported = await session.ReadPoolsAsync(default);
        Assert.Equal("unsupported", unsupported.Status);
        Assert.Null(unsupported.Data);

        var invalid = await session.ReadAlertsAsync(default);
        Assert.Equal("unavailable", invalid.Status);
        Assert.Null(invalid.Data);
    }

    private sealed class SnmpReader : ISynologyHardwareReader
    {
        public Task<IReadOnlyDictionary<string, string>> ReadAsync(string host, string community, CancellationToken ct) =>
            Task.FromResult<IReadOnlyDictionary<string, string>>(new Dictionary<string, string>
            {
                ["model"] = "DS920+",
                ["cpuIdle"] = "88"
            });
    }

    private sealed class StorageFactory(StorageClient client) : INasReadClientFactory
    {
        public INasReadClient Create() => client;
    }

    private sealed class StorageClient : INasReadClient
    {
        public JsonElement Disks { get; set; } = JsonSerializer.SerializeToElement(Array.Empty<object>());

        public Task ConnectAsync(NasConnection profile, string key, CancellationToken ct) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        public Task<JsonElement> ReadAsync(string method, CancellationToken ct) => method switch
        {
            "system.info" => Task.FromResult(JsonSerializer.SerializeToElement(new { version = "24.10.2", system_serial = "NAS-123", model = "CPU model" })),
            "disk.query" => Task.FromResult(Disks),
            "pool.query" => Task.FromException<JsonElement>(new NasReadException("unsupported")),
            "alert.list" => Task.FromResult(JsonSerializer.SerializeToElement(new[] { new { id = 5 } })),
            _ => throw new InvalidOperationException(method)
        };
    }
}
