using System.Text.Json;
using Homelab.Api;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Homelab.Tests;
public sealed class NasIdentityTests
{
    [Theory]
    [InlineData("24.10.2.4", true)] [InlineData("TrueNAS-SCALE-24.10.2.4", true)]
    [InlineData("TrueNAS-SCALE-24.10.0", true)] [InlineData("TrueNAS-SCALE-24.10.2.5", true)] [InlineData("24.10.2", true)]
    [InlineData("TrueNAS-SCALE-24.100.1", false)] [InlineData("TrueNAS-SCALE-25.10.1-note-24.10", false)]
    [InlineData("TrueNAS-SCALE-24.10-BETA.1", false)] [InlineData("TrueNAS-SCALE-24.10.2-NIGHTLY", false)]
    [InlineData("TrueNAS-SCALE-24.10.2\n", false)] [InlineData(null, false)]
    public void OnlyRecognizedStableReleaseFamilyIsAccepted(string? version, bool expected) => Assert.Equal(expected, NasIdentity.SupportsRelease(version));

    [Theory] [InlineData(null)] [InlineData("")] [InlineData("To Be Filled By O.E.M.")] [InlineData("Default String")]
    [InlineData("00000000")] [InlineData("FFFFFFFF")] [InlineData("123456789")] [InlineData("serial\nvalue")]
    public void PlaceholdersCannotBecomeHardwareIdentity(string? serial) => Assert.Null(NasIdentity.UsableSerial(serial));

    [Fact] public async Task ChangedIdentityPausesBeforeStorageReadsAndKeepsPreviousEvidence()
    {
        await using var f = new Fixture(); await f.Collect();
        var initialRevision = f.Nas.ConfigurationRevision;
        var success = f.Nas.LastSuccessUtc; var disk = await f.Db.Disks.SingleAsync(); var seen = disk.LastSeenUtc;
        Assert.Equal("NAS-A", f.Nas.SystemSerial);
        f.Clients.Serial = "NAS-B"; f.Clients.Calls.Clear(); await f.Collect();
        Assert.Equal(["system.info"], f.Clients.Calls);
        Assert.NotEqual(initialRevision, f.Nas.ConfigurationRevision);
        Assert.False(f.Nas.Enabled); Assert.Equal("NAS-A", f.Nas.SystemSerial); Assert.Equal("NAS-B", f.Nas.ObservedSystemSerial);
        Assert.Equal(success, f.Nas.LastSuccessUtc); Assert.Equal(seen, (await f.Db.Disks.SingleAsync()).LastSeenUtc);
        Assert.Equal("critical", (await f.Db.Findings.SingleAsync()).Severity);
        Assert.Equal("unavailable", (await f.Db.Capabilities.SingleAsync(c => c.Name == "system_identity")).Status);
    }
    [Fact] public async Task MissingPreviouslyRecordedIdentityAlsoPausesWithoutInventingNewBaseline()
    {
        await using var f = new Fixture(); await f.Collect();
        f.Clients.Serial = null; f.Clients.Calls.Clear(); await f.Collect();
        Assert.False(f.Nas.Enabled); Assert.Equal("missing", f.Nas.SystemIdentityState); Assert.Equal("NAS-A", f.Nas.SystemSerial);
        Assert.Equal(["system.info"], f.Clients.Calls);
        Assert.NotNull(NasIdentity.ValidateAcceptance(f.Nas, new("NAS-A", "", "Independent evidence", f.Nas.ConfigurationRevision)));
    }
    [Fact] public async Task InitiallyUnavailableSerialRemainsAnExplicitCoverageGap()
    {
        await using var f = new Fixture(); f.Clients.Serial = "To Be Filled By O.E.M."; await f.Collect();
        Assert.True(f.Nas.Enabled); Assert.Null(f.Nas.SystemSerial);
        Assert.Equal("unavailable", (await f.Db.Capabilities.SingleAsync(c => c.Name == "system_identity")).Status);
        Assert.Single(await f.Db.Disks.ToListAsync()); Assert.Empty(await f.Db.Findings.ToListAsync());
    }
    [Fact] public async Task AcceptingDocumentedChangeRequiresConfirmationAndResolvesOnlyIdentityFindingAfterRead()
    {
        await using var f = new Fixture(); await f.Collect(); f.Clients.Serial = "NAS-B"; await f.Collect();
        f.Db.Findings.Add(new() { ConnectionId = f.Nas.Id, Rule = "media-present-v1:ata.pending_sectors", Title = "Retain disk fault" }); await f.Db.SaveChangesAsync();
        var revision = f.Nas.ConfigurationRevision;
        Assert.NotNull(NasIdentity.ValidateAcceptance(f.Nas, new("wrong", "NAS-B", "Confirmed new motherboard", f.Nas.ConfigurationRevision)));
        Assert.NotNull(NasIdentity.ValidateAcceptance(f.Nas, new("NAS-A", "wrong", "Confirmed new motherboard", f.Nas.ConfigurationRevision)));
        Assert.NotNull(NasIdentity.ValidateAcceptance(f.Nas, new("NAS-A", "NAS-B", "", f.Nas.ConfigurationRevision)));
        NasIdentity.Accept(f.Db, f.Nas, new("NAS-A", "NAS-B", "Physical label, motherboard maintenance record and endpoint checked", f.Nas.ConfigurationRevision), "owner"); await f.Db.SaveChangesAsync();
        Assert.NotEqual(revision, f.Nas.ConfigurationRevision); Assert.Equal("awaiting validation", f.Nas.SystemIdentityState);
        Assert.Equal("open", (await f.Db.Findings.SingleAsync(x => x.Rule == "system-identity-v1")).State);
        await f.Collect();
        Assert.True(f.Nas.Enabled); Assert.Equal("matched", f.Nas.SystemIdentityState);
        Assert.Equal("resolved", (await f.Db.Findings.SingleAsync(x => x.Rule == "system-identity-v1")).State);
        Assert.Equal("open", (await f.Db.Findings.SingleAsync(x => x.Rule != "system-identity-v1")).State);
        Assert.Contains(await f.Db.Activity.ToListAsync(), e => e.Actor == "owner" && e.Action == "NAS identity baseline explicitly changed");
    }
    [Fact] public async Task MerelyResumingCannotAcceptDifferentHardware()
    {
        await using var f = new Fixture(); await f.Collect(); f.Clients.Serial = "NAS-B"; await f.Collect();
        f.Nas.Enabled = true; f.Nas.NextAttemptUtc = null; await f.Db.SaveChangesAsync(); f.Clients.Calls.Clear(); await f.Collect();
        Assert.False(f.Nas.Enabled); Assert.Equal(["system.info"], f.Clients.Calls); Assert.Single(await f.Db.Findings.ToListAsync());
    }
    [Fact] public async Task UnsupportedReleaseStopsBeforeHardwareOrStorageEnrollment()
    {
        await using var f = new Fixture(); f.Clients.Version = "TrueNAS-SCALE-25.10.1-note-24.10"; await f.Collect();
        Assert.False(f.Nas.Enabled); Assert.Equal(["system.info"], f.Clients.Calls); Assert.Null(f.Nas.SystemSerial); Assert.Empty(await f.Db.Disks.ToListAsync());
    }
    [Fact] public async Task OldIdentityReviewCannotResumeAfterConnectionRevisionChanges()
    {
        await using var f = new Fixture(); await f.Collect(); f.Clients.Serial = "NAS-B"; await f.Collect();
        var input = new AcceptNasIdentity("NAS-A", "NAS-B", "Independent physical and endpoint review", f.Nas.ConfigurationRevision);
        Assert.Null(NasIdentity.ValidateAcceptance(f.Nas, input));
        f.Nas.ConfigurationRevision = Guid.NewGuid(); await f.Db.SaveChangesAsync();
        var events = await f.Db.Activity.CountAsync();
        Assert.Throws<ArgumentException>(() => NasIdentity.Accept(f.Db, f.Nas, input, "owner"));
        Assert.False(f.Nas.Enabled); Assert.Equal("NAS-A", f.Nas.SystemSerial);
        Assert.Equal(events, await f.Db.Activity.CountAsync()); Assert.DoesNotContain(f.Db.Activity.Local, x => f.Db.Entry(x).State == EntityState.Added);
        Assert.NotNull(NasIdentity.ValidateAcceptance(f.Nas, input with { ConfigurationRevision = null }));
    }
    [Fact] public async Task ChangedEndpointInvalidatesCurrentIdentityAndCoverageButKeepsHistory()
    {
        await using var f = new Fixture(); await f.Collect();
        f.Db.Findings.Add(new() { ConnectionId = f.Nas.Id, Rule = "historical-pool-fault", Title = "Retained evidence" });
        await f.Db.SaveChangesAsync();
        Assert.Equal("supported", (await f.Db.Capabilities.SingleAsync(c => c.Name == "inventory")).Status);
        var lastSuccess = f.Nas.LastSuccessUtc; var savedTopology = f.Nas.TopologyJson;
        var diskId = (await f.Db.Disks.SingleAsync()).Id; var finding = await f.Db.Findings.SingleAsync();
        await NasIdentity.InvalidateConnectionContextAsync(f.Db, f.Nas); await f.Db.SaveChangesAsync();
        Assert.Equal("NAS-A", f.Nas.SystemSerial); Assert.Null(f.Nas.ObservedSystemSerial); Assert.Null(f.Nas.SystemIdentityCheckedUtc);
        Assert.Equal("awaiting validation", f.Nas.SystemIdentityState); Assert.Equal(lastSuccess, f.Nas.LastSuccessUtc);
        Assert.Equal(savedTopology, f.Nas.TopologyJson); Assert.Equal(diskId, (await f.Db.Disks.SingleAsync()).Id);
        Assert.Equal("open", finding.State);
        Assert.All(await f.Db.Capabilities.Where(c => c.Name == "inventory" || c.Name == "system_identity").ToListAsync(), c => Assert.Equal("unavailable", c.Status));
        Assert.Equal("unsupported", (await f.Db.Capabilities.SingleAsync(c => c.Name == "smart")).Status);
        Assert.Equal("disabled", (await f.Db.Capabilities.SingleAsync(c => c.Name == "datasets")).Status);
        Assert.NotNull(NasIdentity.ValidateAcceptance(f.Nas, new("NAS-A", "NAS-B", "Old endpoint review", f.Nas.ConfigurationRevision)));
    }
    [Fact] public async Task InitialCertificateEnrollmentInvalidatesOldEditorsWithoutDiscardingItsOwnRead()
    {
        await using var f = new Fixture(); var before = f.Nas.ConfigurationRevision;
        f.Clients.Fingerprint = new string('A', 64); await f.Collect();
        Assert.NotEqual(before, f.Nas.ConfigurationRevision); Assert.Equal(f.Clients.Fingerprint, f.Nas.CertificateSha256);
        Assert.NotNull(f.Nas.LastSuccessUtc); Assert.Single(await f.Db.Disks.ToListAsync());
        var enrolled = f.Nas.ConfigurationRevision; await f.Collect();
        Assert.Equal(enrolled, f.Nas.ConfigurationRevision);
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
            connection.Open(); Db = new(new DbContextOptionsBuilder<HomelabDb>().UseSqlite(connection).Options); Db.Database.Migrate();
            var protection = new EphemeralDataProtectionProvider();
            Nas = new() { ProtectedApiKey = protection.CreateProtector("nas-credentials").Protect("fixture") }; Db.Connections.Add(Nas); Db.SaveChanges();
            collector = new(services.GetRequiredService<IServiceScopeFactory>(), protection, NullLogger<Collector>.Instance, new WriteCoordinator(), Clients);
        }
        public Task Collect() => collector.CollectAsync(Nas, Db, default);
        public async ValueTask DisposeAsync() { await Db.DisposeAsync(); await connection.DisposeAsync(); await services.DisposeAsync(); collector.Dispose(); }
    }
    private sealed class FixtureClients : INasReadClientFactory
    {
        public List<string> Calls { get; } = [];
        public string? Serial { get; set; } = "NAS-A";
        public string? Fingerprint { get; set; }
        public string Version { get; set; } = "TrueNAS-SCALE-24.10.2";
        public INasReadClient Create() => new Client(this);
        private sealed class Client(FixtureClients fixture) : INasReadClient
        {
            public string? ServerCertificateSha256 => fixture.Fingerprint;
            public Task ConnectAsync(NasConnection profile, string key, CancellationToken ct) => Task.CompletedTask;
            public Task<JsonElement> ReadAsync(string method, CancellationToken ct)
            {
                fixture.Calls.Add(method);
                using var doc = JsonDocument.Parse(method switch { "system.info" => JsonSerializer.Serialize(new { version = fixture.Version, system_serial = fixture.Serial }), "disk.query" => """[{"serial":"DRIVE","model":"model","name":"sda"}]""", "pool.query" or "alert.list" => "[]", _ => throw new InvalidOperationException() });
                return Task.FromResult(doc.RootElement.Clone());
            }
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }
}
