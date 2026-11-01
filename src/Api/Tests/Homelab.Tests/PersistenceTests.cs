using Homelab.Api;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using System.Text.Json;
namespace Homelab.Tests;

public sealed class PersistenceTests
{
    [Fact] public void FreshDatabaseMigratesAndRetainsRecordsOnRestart()
    {
        using var fixture = new DatabaseFixture();
        using (var db = fixture.Open()) {
            DatabaseLifecycle.Initialize(db, fixture.Path);
            db.Plans.Add(new() { Title = "Spare budget", Budget = 300 }); db.SaveChanges();
            Assert.Equal(db.Database.GetMigrations(), db.Database.GetAppliedMigrations());
        }
        using var reopened = fixture.Open(); DatabaseLifecycle.Initialize(reopened, fixture.Path);
        Assert.Equal("Spare budget", reopened.Plans.Single().Title);
    }
    [Fact] public void LegacyDatabaseIsPreservedRatherThanOverwritten()
    {
        using var fixture = new DatabaseFixture(); using var db = fixture.Open();
        db.Database.EnsureCreated(); db.Plans.Add(new() { Title = "Keep me" }); db.SaveChanges();
        Assert.Throws<InvalidOperationException>(() => DatabaseLifecycle.Initialize(db, fixture.Path));
        Assert.Equal("Keep me", db.Plans.Single().Title);
    }
    [Fact] public async Task FailedPoolReadDoesNotCommitDiskChangesOrFreshenLastSuccess()
    {
        using var fixture = new DatabaseFixture(); using var db = fixture.Open();
        DatabaseLifecycle.Initialize(db, fixture.Path);
        var protection = new EphemeralDataProtectionProvider();
        var last = DateTime.UtcNow.AddHours(-1);
        var nas = new NasConnection { Name = "Fixture", LastSuccessUtc = last, ProtectedApiKey = protection.CreateProtector("nas-credentials").Protect("fixture-only") };
        var disk = new Disk { ConnectionId = nas.Id, Serial = "OLD", Model = "model", IdentityKey = "model:OLD", Alias = "sda" };
        db.Connections.Add(nas); db.Disks.Add(disk); await db.SaveChangesAsync();
        using var services = new ServiceCollection().BuildServiceProvider();
        var collector = new Collector(services.GetRequiredService<IServiceScopeFactory>(), protection, NullLogger<Collector>.Instance, new WriteCoordinator(), new FailingClientFactory());
        await collector.CollectAsync(nas, db, CancellationToken.None);
        db.ChangeTracker.Clear();
        var persisted = await db.Disks.SingleAsync();
        Assert.True(persisted.Present); Assert.Equal("sda", persisted.Alias);
        Assert.Equal(last, (await db.Connections.SingleAsync()).LastSuccessUtc);
        Assert.Empty(await db.Findings.ToListAsync());
    }
    [Fact] public async Task WriterLeaseReleasesAfterExceptionAndIsExclusive()
    {
        var gate = new WriteCoordinator();
        var first = await gate.EnterAsync(); var second = gate.EnterAsync().AsTask();
        Assert.False(second.IsCompleted); first.Dispose();
        using var acquired = await second.WaitAsync(TimeSpan.FromSeconds(2));
    }
    private sealed class FailingClientFactory : INasReadClientFactory { public INasReadClient Create() => new FailingClient(); }
    private sealed class FailingClient : INasReadClient
    {
        public Task ConnectAsync(NasConnection profile, string key, CancellationToken ct) => Task.CompletedTask;
        public Task<JsonElement> ReadAsync(string method, CancellationToken ct) => method switch {
            "system.info" => Task.FromResult(JsonDocument.Parse("{\"version\":\"TrueNAS-SCALE-24.10.2\"}").RootElement.Clone()),
            "disk.query" => Task.FromResult(JsonDocument.Parse("[{\"serial\":\"NEW\",\"model\":\"model\",\"name\":\"sdb\"}]").RootElement.Clone()),
            _ => throw new IOException("Simulated read failure")
        };
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
    private sealed class DatabaseFixture : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        public DatabaseFixture() => Directory.CreateDirectory(Path);
        public HomelabDb Open() => new(new DbContextOptionsBuilder<HomelabDb>().UseSqlite(new SqliteConnectionStringBuilder { DataSource = System.IO.Path.Combine(Path, "test.db"), Pooling = false }.ToString()).Options);
        public void Dispose() => Directory.Delete(Path, true);
    }
}
