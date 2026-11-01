using Homelab.Api;
using Microsoft.EntityFrameworkCore;
using Microsoft.Data.Sqlite;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using System.Text.Json;
namespace Homelab.Tests;
public sealed class CoverageTests
{
    [Fact] public void StalenessDoesNotErasePermissionFailure()
    {
        var now = DateTime.UtcNow;
        var capability = new Capability { Status = "supported", LastSuccessUtc = now.AddSeconds(-181), ExpectedSeconds = 60 };
        Assert.Equal("stale", capability.EffectiveStatus(now));
        capability.Status = "denied"; Assert.Equal("denied", capability.EffectiveStatus(now));
        capability.Status = "unsupported"; Assert.Equal("unsupported", capability.EffectiveStatus(now));
    }
    [Fact] public async Task DeniedAlertsDoNotHideSuccessfulPoolReadOrMissingSmart()
    {
        using var connection = new SqliteConnection("Data Source=:memory:"); connection.Open();
        await using var db = new HomelabDb(new DbContextOptionsBuilder<HomelabDb>().UseSqlite(connection).Options); db.Database.EnsureCreated();
        var protection = new EphemeralDataProtectionProvider();
        var nas = new NasConnection { ProtectedApiKey = protection.CreateProtector("nas-credentials").Protect("test") };
        db.Connections.Add(nas); await db.SaveChangesAsync();
        using var services = new ServiceCollection().BuildServiceProvider();
        var collector = new Collector(services.GetRequiredService<IServiceScopeFactory>(), protection, NullLogger<Collector>.Instance, new WriteCoordinator(), new FixtureFactory());
        await collector.CollectAsync(nas, db, default);
        var capabilities = await db.Capabilities.ToDictionaryAsync(x => x.Name);
        Assert.Equal("supported", capabilities["pools"].Status);
        Assert.Equal("denied", capabilities["alerts"].Status);
        Assert.Null(capabilities["alerts"].LastSuccessUtc);
        Assert.Equal("unsupported", capabilities["smart"].Status);
        Assert.Single(await db.CollectionRuns.ToListAsync());
        Assert.NotNull((await db.Connections.SingleAsync()).LastSuccessUtc);
        var savedPool = await db.PoolSnapshots.SingleAsync();
        Assert.Equal("18446744073709551615", savedPool.PoolGuid);
        Assert.Null(savedPool.FreeBytes); Assert.Null(savedPool.ScanErrors);
        var finding = await db.Findings.SingleAsync(); Assert.Equal("critical", finding.Severity);
    }
    [Theory] [InlineData(13, "denied")] [InlineData(1, "denied")] [InlineData(38, "unsupported")] [InlineData(999, "unavailable")]
    public void StructuredErrorsHaveSafeCapabilityClassification(int errno, string expected)
    {
        using var doc = JsonDocument.Parse("{\"error\":" + errno + ",\"reason\":\"potential secret that must not be persisted\"}");
        var exception = NasReadException.FromError(doc.RootElement);
        Assert.Equal(expected, exception.Status); Assert.DoesNotContain("potential secret", exception.Message);
    }
    private sealed class FixtureFactory : INasReadClientFactory { public INasReadClient Create() => new FixtureClient(); }
    private sealed class FixtureClient : INasReadClient
    {
        public Task ConnectAsync(NasConnection profile, string key, CancellationToken ct) => Task.CompletedTask;
        public Task<JsonElement> ReadAsync(string method, CancellationToken ct) {
            if (method == "alert.list") throw new NasReadException("denied");
            var json = method switch { "system.info" => "{\"version\":\"TrueNAS-SCALE-24.10.2\"}", "disk.query" => "[]", "pool.query" => "[{\"name\":\"tank\",\"guid\":\"18446744073709551615\",\"status\":\"DEGRADED\",\"topology\":{\"data\":[]}}]", _ => throw new InvalidOperationException() };
            using var doc = JsonDocument.Parse(json); return Task.FromResult(doc.RootElement.Clone());
        }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
