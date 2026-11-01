using System.Collections.Concurrent;
using System.Text.Json;
using System.Threading.Channels;
using Homelab.Api;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Homelab.Tests;

public sealed class CollectionConcurrencyTests
{
    [Fact] public async Task SlowNasDoesNotPreventAnotherNasFromCommitting()
    {
        await using var f = new Fixture(2);
        f.Clients.Block(f.Ids[0]);
        f.Clients.Block(f.Ids[1]);
        var batch = f.Collector.CollectDueAsync(default);
        try {
            // Establish overlap explicitly; worker scheduling order is not guaranteed.
            for (var i = 0; i < 2; i++)
                await f.Clients.Started.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10));
            f.Clients.Release(f.Ids[1]);
            var finished = await f.Clients.Disposed.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(f.Ids[1], finished);
            Assert.False(batch.IsCompleted);
            await using var scope = f.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<HomelabDb>();
            Assert.NotNull((await db.Connections.SingleAsync(x => x.Id == finished)).LastSuccessUtc);
            Assert.Null((await db.Connections.SingleAsync(x => x.Id == f.Ids[0])).LastSuccessUtc);
            // A local write is still possible while the other NAS awaits its network response.
            using var lease = await f.Writes.EnterAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2));
            db.Activity.Add(new() { Action = "Concurrent local note", Detail = "fixture" }); await db.SaveChangesAsync();
        } finally { f.Clients.ReleaseAll(); await batch.WaitAsync(TimeSpan.FromSeconds(10)); }
        Assert.Equal(2, f.Clients.Peak);
        Assert.All(f.Clients.ConnectionCounts.Values, count => Assert.Equal(1, count));
    }

    [Fact] public async Task BoundedWorkersRecheckPausedAndRescheduledQueuedProfiles()
    {
        await using var f = new Fixture(7);
        foreach (var id in f.Ids.Take(4)) f.Clients.Block(id);
        var batch = f.Collector.CollectDueAsync(default);
        try {
            var started = new List<Guid>();
            for (var i = 0; i < 4; i++) started.Add(await f.Clients.Started.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10)));
            Assert.Equal(f.Ids.Take(4).Order(), started.Order());
            Assert.Equal(4, f.Clients.Peak);
            await using (var scope = f.Services.CreateAsyncScope()) {
                var db = scope.ServiceProvider.GetRequiredService<HomelabDb>();
                using var lease = await f.Writes.EnterAsync();
                (await db.Connections.SingleAsync(x => x.Id == f.Ids[4])).Enabled = false;
                (await db.Connections.SingleAsync(x => x.Id == f.Ids[5])).NextAttemptUtc = DateTime.UtcNow.AddHours(1);
                db.Connections.Remove(await db.Connections.SingleAsync(x => x.Id == f.Ids[6]));
                await db.SaveChangesAsync();
            }
        } finally { f.Clients.ReleaseAll(); await batch.WaitAsync(TimeSpan.FromSeconds(10)); }
        Assert.Equal(4, f.Clients.ConnectionCounts.Count);
        Assert.All(f.Clients.ConnectionCounts.Values, count => Assert.Equal(1, count));
        await f.Collector.CollectDueAsync(default); // Backoff/cadence and queued edits are still respected.
        Assert.Equal(4, f.Clients.ConnectionCounts.Count);
        Assert.All(f.Clients.ConnectionCounts.Values, count => Assert.Equal(1, count));
    }

    [Fact] public async Task FailedNasStatusIsIsolatedFromSuccessfulNasAndRemainingQueue()
    {
        await using var f = new Fixture(6);
        f.Clients.FailId = f.Ids[0];
        await f.Collector.CollectDueAsync(default).WaitAsync(TimeSpan.FromSeconds(10));
        await using var scope = f.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<HomelabDb>();
        var failed = await db.Connections.SingleAsync(x => x.Id == f.Ids[0]);
        Assert.Null(failed.LastSuccessUtc); Assert.Equal(1, failed.ConsecutiveFailures);
        Assert.All(await db.Connections.Where(x => x.Id != failed.Id).ToListAsync(), x => {
            Assert.NotNull(x.LastSuccessUtc); Assert.Equal(0, x.ConsecutiveFailures);
        });
        Assert.All(await db.Capabilities.Where(x => x.ConnectionId != failed.Id && x.Name == "pools").ToListAsync(), x => Assert.Equal("supported", x.Status));
        Assert.Equal("unavailable", (await db.Capabilities.SingleAsync(x => x.ConnectionId == failed.Id && x.Name == "pools")).Status);
        Assert.Equal(6, f.Clients.ConnectionCounts.Count); Assert.InRange(f.Clients.Peak, 1, 4);
        Assert.All(f.Clients.ConnectionCounts.Values, count => Assert.Equal(1, count));
    }

    [Fact] public async Task ShutdownCancelsActiveReadsAndDoesNotStartQueuedConnections()
    {
        await using var f = new Fixture(6);
        foreach (var id in f.Ids) f.Clients.Block(id);
        using var cancellation = new CancellationTokenSource();
        var batch = f.Collector.CollectDueAsync(cancellation.Token);
        try {
            for (var i = 0; i < 4; i++) await f.Clients.Started.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10));
        } finally { cancellation.Cancel(); }
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => batch.WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.Equal(4, f.Clients.ConnectionCounts.Count); Assert.Equal(0, f.Clients.Active);
        await using var scope = f.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<HomelabDb>();
        Assert.All(await db.Connections.ToListAsync(), x => { Assert.Null(x.LastSuccessUtc); Assert.Equal(0, x.ConsecutiveFailures); });
    }

    [Fact] public async Task EventRefreshHonorsMinimumIntervalPauseAndFailureBackoff()
    {
        await using var f=new Fixture(4);f.Clients.EventCandidates=f.Ids;
        await using (var scope=f.Services.CreateAsyncScope()) {
            var db=scope.ServiceProvider.GetRequiredService<HomelabDb>();var profiles=await db.Connections.OrderBy(x=>x.Id).ToListAsync();
            foreach (var profile in profiles) { profile.NextAttemptUtc=DateTime.UtcNow.AddHours(1);profile.LastAttemptUtc=DateTime.UtcNow.AddMinutes(-1); }
            profiles[1].LastAttemptUtc=DateTime.UtcNow;profiles[2].ConsecutiveFailures=1;profiles[3].Enabled=false;await db.SaveChangesAsync();
        }
        await f.Collector.CollectDueAsync(default);
        Assert.Single(f.Clients.ConnectionCounts);Assert.Equal(1,f.Clients.ConnectionCounts[f.Ids[0]]);
        await f.Collector.CollectDueAsync(default);Assert.Equal(1,f.Clients.ConnectionCounts[f.Ids[0]]);
        await using var checkScope=f.Services.CreateAsyncScope();var check=checkScope.ServiceProvider.GetRequiredService<HomelabDb>();
        Assert.Equal("denied",(await check.Capabilities.SingleAsync(x=>x.ConnectionId==f.Ids[0] && x.Name=="events:disk.query")).Status);
        Assert.Equal("supported",(await check.Capabilities.SingleAsync(x=>x.ConnectionId==f.Ids[0] && x.Name=="inventory")).Status);
        Assert.Equal("supported",(await check.Capabilities.SingleAsync(x=>x.ConnectionId==f.Ids[0] && x.Name=="events:alert.list")).Status);
    }
    private sealed class Fixture : IAsyncDisposable
    {
        private readonly string folder = Path.Combine(Path.GetTempPath(), "homelab-concurrency-" + Guid.NewGuid());
        public ServiceProvider Services { get; }
        public WriteCoordinator Writes { get; } = new();
        public Clients Clients { get; } = new();
        public Collector Collector { get; }
        public Guid[] Ids { get; }
        public Fixture(int count)
        {
            Directory.CreateDirectory(folder);
            Services = new ServiceCollection().AddDbContext<HomelabDb>(o => o.UseSqlite($"Data Source={Path.Combine(folder, "test.db")};Pooling=False")).BuildServiceProvider();
            var protection = new EphemeralDataProtectionProvider();
            using var scope = Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<HomelabDb>();
            db.Database.EnsureCreated(); db.Database.ExecuteSqlRaw("PRAGMA journal_mode=WAL;");
            Ids = Enumerable.Range(1, count).Select(i => Guid.Parse($"00000000-0000-0000-0000-{i:D12}")).ToArray();
            db.Connections.AddRange(Ids.Select(id => new NasConnection { Id = id, ProtectedApiKey = protection.CreateProtector("nas-credentials").Protect("fixture"), Enabled = true }));
            db.SaveChanges();
            Collector = new(Services.GetRequiredService<IServiceScopeFactory>(), protection, NullLogger<Collector>.Instance, Writes, Clients);
        }
        public async ValueTask DisposeAsync() { Clients.ReleaseAll(); Collector.Dispose(); await Services.DisposeAsync(); Directory.Delete(folder, true); }
    }
    private sealed class Clients : INasReadClientFactory
    {
        private readonly ConcurrentDictionary<Guid, TaskCompletionSource> gates = new();
        public ConcurrentDictionary<Guid, int> ConnectionCounts { get; } = new();
        public Channel<Guid> Started { get; } = Channel.CreateUnbounded<Guid>();
        public Channel<Guid> Disposed { get; } = Channel.CreateUnbounded<Guid>();
        public Guid? FailId { get; set; }
        public Guid[] EventCandidates { get; set; } = [];
        public Guid[] EventRefreshCandidates() => EventCandidates;
        private int active, peak;
        public int Active => active;
        public int Peak => peak;
        public void Block(Guid id) => gates[id] = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public void Release(Guid id) => gates[id].TrySetResult();
        public void ReleaseAll() { foreach (var gate in gates.Values) gate.TrySetResult(); }
        public INasReadClient Create() => new Client(this);
        private sealed class Client(Clients owner) : INasReadClient
        {
            private Guid? id;
            public async Task ConnectAsync(NasConnection profile, string key, CancellationToken ct)
            {
                id = profile.Id; owner.ConnectionCounts.AddOrUpdate(profile.Id, 1, (_, n) => n + 1);
                var count = Interlocked.Increment(ref owner.active);
                int old;
                do { old = owner.peak; if (count <= old) break; } while (Interlocked.CompareExchange(ref owner.peak, count, old) != old);
                owner.Started.Writer.TryWrite(profile.Id);
                if (owner.gates.TryGetValue(profile.Id, out var gate)) await gate.Task.WaitAsync(ct);
                if (profile.Id == owner.FailId) throw new IOException("Fixture transport unavailable");
            }
            public Task<IReadOnlyDictionary<string,string>> SubscribeEventsAsync(CancellationToken ct) => Task.FromResult<IReadOnlyDictionary<string,string>>(new Dictionary<string,string> { ["disk.query"]="denied",["alert.list"]="supported",["pool.query"]="supported" });
            public Task<JsonElement> ReadAsync(string method, CancellationToken ct)
            {
                ct.ThrowIfCancellationRequested();
                using var doc = JsonDocument.Parse(method == "system.info" ? "{\"version\":\"TrueNAS-SCALE-24.10.2\"}" : "[]");
                return Task.FromResult(doc.RootElement.Clone());
            }
            public ValueTask DisposeAsync() {
                if (id is {} value) { Interlocked.Decrement(ref owner.active); owner.Disposed.Writer.TryWrite(value); id = null; }
                return ValueTask.CompletedTask;
            }
        }
    }
}
