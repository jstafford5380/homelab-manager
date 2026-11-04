using Homelab.Api;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using System.Net;
namespace Homelab.Tests;
public sealed class NotificationTests
{
    [Theory] [InlineData(true)] [InlineData(false)]
    public async Task TelegramUsesExistingOutboxAndMasksPrivateEvidence(bool mask)
    {
        using var fixture = new Fixture(); await using var db = fixture.Db;
        fixture.Channel.Kind = "telegram";
        fixture.Channel.ProtectedUrl = fixture.Protect(System.Text.Json.JsonSerializer.Serialize(new TelegramDestination("123456:abcdefghijklmnopqrstuvwxyz", "-123456")));
        fixture.Channel.MaskIdentifiers = mask; fixture.Sender.Status = 200;
        db.Findings.Add(new() { ConnectionId = fixture.Nas.Id, Title = "PRIVATE-TITLE", Evidence = "PRIVATE-EVIDENCE", Severity = "critical" });
        await db.SaveChangesAsync();
        await fixture.Engine.QueueAsync(db, fixture.Channel, DateTime.UtcNow, default);
        await fixture.Engine.DeliverOneAsync(db, fixture.Channel, DateTime.UtcNow.AddSeconds(1), default);
        Assert.Equal("accepted", (await db.Deliveries.SingleAsync()).Status);
        var payload = fixture.Sender.Payloads.Single();
        Assert.Equal(!mask, payload.Contains("PRIVATE-TITLE"));
        Assert.DoesNotContain("PRIVATE-EVIDENCE", payload);
        Assert.DoesNotContain("abcdefghijklmnopqrstuvwxyz", payload);
        using var json = System.Text.Json.JsonDocument.Parse(payload);
        Assert.Equal("-123456", json.RootElement.GetProperty("chat_id").GetString());
        Assert.Equal("api.telegram.org", fixture.Sender.Uris.Single().Host);
    }
    [Fact] public async Task DailyQuietHoursDeferNoncriticalQueueAndPendingSendsButAllowCritical()
    {
        using var fixture = new Fixture(); await using var db = fixture.Db;
        db.Settings.Add(new() { Timezone = "UTC" });
        fixture.Channel.QuietStart = "22:00"; fixture.Channel.QuietEnd = "07:00";
        db.Findings.Add(new() { ConnectionId = fixture.Nas.Id, Title = "Review", Severity = "urgent" });
        await db.SaveChangesAsync();
        var day = DateTime.UtcNow.Date.AddDays(1);
        await fixture.Engine.QueueAsync(db, fixture.Channel, day.AddHours(21), default);
        var queued = await db.Deliveries.SingleAsync();
        await fixture.Engine.DeliverOneAsync(db, fixture.Channel, day.AddHours(23), default);
        Assert.Equal(0, fixture.Sender.Calls); Assert.Equal(0, queued.Attempts);
        db.Findings.Add(new() { ConnectionId = fixture.Nas.Id, Title = "Critical", Severity = "critical" });
        await db.SaveChangesAsync();
        await fixture.Engine.QueueAsync(db, fixture.Channel, day.AddHours(23), default);
        await fixture.Engine.DeliverOneAsync(db, fixture.Channel, day.AddHours(23), default);
        Assert.Equal(1, fixture.Sender.Calls);
        await fixture.Engine.DeliverOneAsync(db, fixture.Channel, day.AddDays(1).AddHours(7), default);
        Assert.Equal(2, fixture.Sender.Calls);
    }
    [Fact] public async Task QueueDeduplicatesAndRetryStopsAfterFiveAttempts()
    {
        using var fixture = new Fixture(); await using var db = fixture.Db;
        var finding = new Finding { ConnectionId=fixture.Nas.Id, Title = "private-serial-ABC", Evidence = "private-path", Severity = "urgent" };
        db.Findings.Add(finding); await db.SaveChangesAsync();
        var now = DateTime.UtcNow;
        await fixture.Engine.QueueAsync(db, fixture.Channel, now, default); await fixture.Engine.QueueAsync(db, fixture.Channel, now, default);
        var delivery = await db.Deliveries.SingleAsync();
        Assert.DoesNotContain("private-serial", delivery.PayloadJson); Assert.DoesNotContain("private-path", delivery.PayloadJson);
        for (var attempt = 0; attempt < 6; attempt++) await fixture.Engine.DeliverOneAsync(db, fixture.Channel, now.AddHours(attempt), default);
        Assert.Equal(5, fixture.Sender.Calls); Assert.Equal("failed", delivery.Status); Assert.Equal(5, await db.DeliveryAttempts.CountAsync());
    }
    [Theory] [InlineData("ambiguous", "unverified or masked")] [InlineData("inferred", "Bay 7")]
    public async Task HistoricalBayConfirmationCannotOverrideCurrentIdentityAmbiguity(string confidence, string expected)
    {
        using var fixture = new Fixture(); await using var db = fixture.Db;
        fixture.Channel.MaskIdentifiers = false;
        var disk = new Disk { ConnectionId=fixture.Nas.Id, Serial = "DRIVE", Bay = "Bay 7", BayVerified = true, MappingConfidence = confidence };
        db.Disks.Add(disk); db.Findings.Add(new() { DiskId = disk.Id, ConnectionId = disk.ConnectionId, Title = "Drive evidence" });
        await db.SaveChangesAsync(); await fixture.Engine.QueueAsync(db, fixture.Channel, DateTime.UtcNow, default);
        using var payload = System.Text.Json.JsonDocument.Parse((await db.Deliveries.SingleAsync()).PayloadJson);
        Assert.Equal(expected, payload.RootElement.GetProperty("bay").GetString());
        Assert.True(disk.BayVerified); // Retain the original physical-label evidence.
        Assert.Equal("Bay 7", disk.Bay);
    }

    [Fact] public async Task UntestedAndDisabledChannelsNeverQueueOrDeliver()
    {
        using var fixture = new Fixture(); await using var db = fixture.Db;
        db.Findings.Add(new(){ConnectionId=fixture.Nas.Id}); await db.SaveChangesAsync();
        fixture.Channel.TestedUtc = null; await db.SaveChangesAsync();
        await fixture.Engine.QueueAsync(db, fixture.Channel, DateTime.UtcNow, default);
        await fixture.Engine.DeliverOneAsync(db, fixture.Channel, DateTime.UtcNow, default);
        Assert.Empty(await db.Deliveries.ToListAsync()); Assert.Equal(0, fixture.Sender.Calls);
        fixture.Channel.TestedUtc = DateTime.UtcNow; fixture.Channel.Enabled = false;
        await fixture.Engine.QueueAsync(db, fixture.Channel, DateTime.UtcNow, default);
        Assert.Empty(await db.Deliveries.ToListAsync());
    }
    [Fact] public async Task CriticalBypassesMaintenanceAndAcceptanceIsRecorded()
    {
        using var fixture = new Fixture(); await using var db = fixture.Db;
        fixture.Channel.MaintenanceUntilUtc = DateTime.UtcNow.AddHours(1); fixture.Sender.Status = 202;
        db.Findings.AddRange(new Finding { ConnectionId=fixture.Nas.Id, Severity = "review" }, new Finding { ConnectionId=fixture.Nas.Id, Severity = "critical" }); await db.SaveChangesAsync();
        await fixture.Engine.QueueAsync(db, fixture.Channel, DateTime.UtcNow, default);
        Assert.Single(await db.Deliveries.ToListAsync());
        await fixture.Engine.DeliverOneAsync(db, fixture.Channel, DateTime.UtcNow.AddSeconds(1), default);
        Assert.Equal("accepted", (await db.Deliveries.SingleAsync()).Status);
    }
    [Theory] [InlineData(202)] [InlineData(503)]
    public async Task InFlightDeliveryReleasesWriterAndCannotReviveCancelledEvent(int status)
    {
        using var fixture = new Fixture(); await using var db = fixture.Db;
        fixture.Sender.Status = status; fixture.Sender.Gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        db.Findings.Add(new Finding { ConnectionId=fixture.Nas.Id, Severity = "critical" }); await db.SaveChangesAsync();
        await fixture.Engine.QueueAsync(db, fixture.Channel, DateTime.UtcNow, default);
        var delivery = await db.Deliveries.SingleAsync();
        var send = fixture.Engine.DeliverOneAsync(db, fixture.Channel, DateTime.UtcNow.AddSeconds(1), default);
        try {
            await fixture.Sender.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            using var lease = await fixture.Writes.EnterAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2));
            await using var concurrent = fixture.NewContext();
            var channel = await concurrent.NotificationChannels.SingleAsync();
            channel.Revision = Guid.NewGuid(); channel.TestedUtc = null; channel.Enabled = false;
            (await concurrent.Deliveries.SingleAsync()).Status = "cancelled-channel-changed";
            concurrent.Activity.Add(new() { Action = "Concurrent local write" }); await concurrent.SaveChangesAsync();
        } finally { fixture.Sender.Gate.TrySetResult(); await send.WaitAsync(TimeSpan.FromSeconds(5)); }
        Assert.Equal("cancelled-channel-changed", delivery.Status);
        Assert.Null(delivery.AcceptedUtc);
        var attempt = await db.DeliveryAttempts.SingleAsync();
        Assert.Equal(status, attempt.HttpStatus); Assert.Equal(status == 202 ? "accepted" : "rejected", attempt.Result);
        await using var check = fixture.NewContext();
        Assert.False((await check.NotificationChannels.SingleAsync()).Enabled);
        Assert.Null((await check.NotificationChannels.SingleAsync()).TestedUtc);
    }

    [Fact] public async Task InFlightTestCannotValidateChangedDestination()
    {
        using var fixture = new Fixture(); await using var db = fixture.Db;
        fixture.Sender.Status = 202; fixture.Sender.Gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var test = fixture.Engine.TestChannelAsync(db, default);
        try {
            await fixture.Sender.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            using var lease = await fixture.Writes.EnterAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2));
            await using var concurrent = fixture.NewContext();
            var channel = await concurrent.NotificationChannels.SingleAsync();
            channel.Revision = Guid.NewGuid(); channel.TestedUtc = null; channel.Enabled = false;
            await concurrent.SaveChangesAsync();
        } finally { fixture.Sender.Gate.TrySetResult(); }
        var result = await test.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(409, ((Microsoft.AspNetCore.Http.IStatusCodeHttpResult)result).StatusCode);
        Assert.Null(fixture.Channel.TestedUtc); Assert.False(fixture.Channel.Enabled);
        Assert.Single(await db.Activity.Where(x => x.Action == "Notification channel test").ToListAsync());
    }

    [Fact] public async Task ShutdownLeavesClaimDurableWithoutInventingAcceptance()
    {
        using var fixture = new Fixture(); await using var db = fixture.Db;
        fixture.Sender.Gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        db.Findings.Add(new Finding { ConnectionId=fixture.Nas.Id, Severity = "critical" }); await db.SaveChangesAsync();
        await fixture.Engine.QueueAsync(db, fixture.Channel, DateTime.UtcNow, default);
        using var cancellation = new CancellationTokenSource();
        var send = fixture.Engine.DeliverOneAsync(db, fixture.Channel, DateTime.UtcNow.AddSeconds(1), cancellation.Token);
        await fixture.Sender.Started.Task.WaitAsync(TimeSpan.FromSeconds(5)); cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => send.WaitAsync(TimeSpan.FromSeconds(5)));
        await using var check = fixture.NewContext();
        var claim = await check.Deliveries.SingleAsync();
        Assert.Equal(1, claim.Attempts); Assert.Equal("pending", claim.Status); Assert.Null(claim.AcceptedUtc);
        Assert.True(claim.NextAttemptUtc > DateTime.UtcNow); Assert.Empty(await check.DeliveryAttempts.ToListAsync());
        using var lease = await fixture.Writes.EnterAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2));
    }

    [Fact] public async Task RepeatedInterruptedClaimsStopAtFiveAcrossFreshContexts()
    {
        using var fixture = new Fixture(); await using var db = fixture.Db;
        db.Findings.Add(new Finding { ConnectionId=fixture.Nas.Id, Severity = "critical" }); await db.SaveChangesAsync();
        await fixture.Engine.QueueAsync(db, fixture.Channel, DateTime.UtcNow, default);
        var original = await db.Deliveries.AsNoTracking().SingleAsync();
        var now = DateTime.UtcNow.AddSeconds(1);
        for (var attempt = 1; attempt <= 5; attempt++) {
            await using var restarted = fixture.NewContext();
            var channel = await restarted.NotificationChannels.SingleAsync();
            fixture.Sender.Started = new(TaskCreationOptions.RunContinuationsAsynchronously);
            fixture.Sender.Gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
            using var cancellation = new CancellationTokenSource();
            var send = fixture.NewEngine().DeliverOneAsync(restarted, channel, now, cancellation.Token);
            await fixture.Sender.Started.Task.WaitAsync(TimeSpan.FromSeconds(5)); cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => send.WaitAsync(TimeSpan.FromSeconds(5)));
            var claim = await restarted.Deliveries.SingleAsync();
            Assert.Equal(attempt, claim.Attempts); Assert.Equal(original.Id, claim.Id); Assert.Equal(original.EventKey, claim.EventKey);
            Assert.Equal("pending", claim.Status); now = claim.NextAttemptUtc.AddSeconds(1);
        }
        fixture.Sender.Gate = null;
        await using var final = fixture.NewContext(); var finalChannel = await final.NotificationChannels.SingleAsync();
        await fixture.NewEngine().DeliverOneAsync(final, finalChannel, now, default);
        var exhausted = await final.Deliveries.SingleAsync();
        Assert.Equal("failed", exhausted.Status); Assert.Equal(5, exhausted.Attempts); Assert.Null(exhausted.AcceptedUtc);
        var unknown = await final.DeliveryAttempts.SingleAsync(); Assert.Equal("outcome-unknown", unknown.Result); Assert.Null(unknown.HttpStatus);
        await fixture.NewEngine().DeliverOneAsync(final, finalChannel, now.AddHours(1), default);
        Assert.Equal(5, fixture.Sender.Calls); Assert.Single(await final.DeliveryAttempts.ToListAsync());
        Assert.All(fixture.Sender.Payloads, payload => Assert.Equal(original.PayloadJson, payload));
    }

    [Fact] public async Task RestartAfterInterruptedSendRetainsEventAndHonorsClaimDeadline()
    {
        using var fixture = new Fixture(); await using var db = fixture.Db;
        db.Findings.Add(new Finding { ConnectionId=fixture.Nas.Id, Severity = "critical" }); await db.SaveChangesAsync();
        await fixture.Engine.QueueAsync(db, fixture.Channel, DateTime.UtcNow, default);
        fixture.Sender.Gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cancellation = new CancellationTokenSource();
        var now = DateTime.UtcNow.AddSeconds(1);
        var send = fixture.Engine.DeliverOneAsync(db, fixture.Channel, now, cancellation.Token);
        await fixture.Sender.Started.Task.WaitAsync(TimeSpan.FromSeconds(5)); cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => send);
        var original = await db.Deliveries.AsNoTracking().SingleAsync();
        fixture.Sender.Gate = null; fixture.Sender.Status = 202;
        await using var restarted = fixture.NewContext(); var channel = await restarted.NotificationChannels.SingleAsync();
        await fixture.NewEngine().QueueAsync(restarted, channel, now, default);
        await fixture.NewEngine().DeliverOneAsync(restarted, channel, now, default);
        Assert.Equal(1, fixture.Sender.Calls);
        await fixture.NewEngine().DeliverOneAsync(restarted, channel, original.NextAttemptUtc.AddSeconds(1), default);
        var accepted = await restarted.Deliveries.SingleAsync();
        Assert.Equal(original.Id, accepted.Id); Assert.Equal(original.EventKey, accepted.EventKey);
        Assert.Equal("accepted", accepted.Status); Assert.Equal(2, accepted.Attempts);
        Assert.Equal(2, fixture.Sender.Calls); Assert.Equal(fixture.Sender.Payloads[0], fixture.Sender.Payloads[1]);
    }

    [Fact] public async Task AcceptedTestValidatesCurrentDestinationWithoutEnablingIt()
    {
        using var fixture = new Fixture(); await using var db = fixture.Db;
        fixture.Channel.Enabled = false; fixture.Channel.TestedUtc = null; await db.SaveChangesAsync(); fixture.Sender.Status = 204;
        var result = await fixture.Engine.TestChannelAsync(db, default);
        Assert.Equal(200, ((Microsoft.AspNetCore.Http.IStatusCodeHttpResult)result).StatusCode);
        Assert.NotNull(fixture.Channel.TestedUtc); Assert.False(fixture.Channel.Enabled);
    }

    [Theory] [InlineData("127.0.0.1")] [InlineData("169.254.169.254")] [InlineData("::1")] [InlineData("::ffff:127.0.0.1")] [InlineData("224.0.0.1")]
    public void UnsafeAddressesAreRejected(string address) => Assert.True(WebhookSender.IsForbiddenAddress(IPAddress.Parse(address)));
    [Fact] public void OnlyExplicitHttpsHostsAreAccepted()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?> { ["Homelab:WebhookHosts:0"] = "notifier.example" }).Build();
        Assert.True(WebhookSender.IsAllowedUrl(new Uri("https://notifier.example/hook"), config));
        Assert.False(WebhookSender.IsAllowedUrl(new Uri("https://elsewhere.example/hook"), config));
        Assert.False(WebhookSender.IsAllowedUrl(new Uri("http://notifier.example/hook"), config));
        Assert.False(WebhookSender.IsAllowedUrl(new Uri("https://user:pass@notifier.example/hook"), config));
    }
    private sealed class FakeSender : IWebhookSender {
        public int Calls; public int Status = 503;
        public List<string> Payloads { get; } = [];
        public List<Uri> Uris { get; } = [];
        public TaskCompletionSource Started { get; set; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource? Gate { get; set; }
        public async Task<int> SendAsync(Uri uri, string payload, CancellationToken ct) { Calls++; Uris.Add(uri); Payloads.Add(payload); Started.TrySetResult(); if (Gate is not null) await Gate.Task.WaitAsync(ct); return Status; }
    }
    private sealed class Fixture : IDisposable
    {
        private readonly SqliteConnection connection = new("Data Source=:memory:");
        private readonly EphemeralDataProtectionProvider protection = new();
        public string Protect(string value) => protection.CreateProtector("webhook-url").Protect(value);
        public NotificationEngine NewEngine() => new(protection, Sender, Writes);
        public WriteCoordinator Writes { get; } = new();
        public HomelabDb NewContext() => new(new DbContextOptionsBuilder<HomelabDb>().UseSqlite(connection).Options);
        public HomelabDb Db { get; }
        public NotificationChannel Channel { get; }
        public NotificationEngine Engine { get; }
        public FakeSender Sender { get; } = new();
        public NasConnection Nas {get;}=new(){Enabled=false};
        public Fixture() {
            connection.Open(); Db = new(new DbContextOptionsBuilder<HomelabDb>().UseSqlite(connection).Options); Db.Database.EnsureCreated();
            Channel = new() { Enabled = true, TestedUtc = DateTime.UtcNow, MaskIdentifiers = true, ProtectedUrl = protection.CreateProtector("webhook-url").Protect("https://notifier.example/test") };
            Db.Connections.Add(Nas); Db.NotificationChannels.Add(Channel); Db.SaveChanges();
            Engine = new(protection, Sender, Writes);
        }
        public void Dispose() => connection.Dispose();
    }
}
