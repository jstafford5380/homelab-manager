using System.Reflection;
using System.Text.Json;
using Homelab.Api;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Homelab.Tests;
public class ApiPowerOnHoursTests
{
    private static JsonElement Json(string value) => JsonSerializer.Deserialize<JsonElement>(value);
    private const string Attributes = """[{"id":9,"name":"Power_On_Hours","value":99,"raw":{"value":12345,"string":"12345"}}]""";
    private const string Inventory = """[{"name":"sda","serial":"SERIAL","model":"MODEL"},{"name":"nvme0n1","serial":"NVME","model":"SSD"},{"name":"sdb","serial":"SAS","model":"SAS MODEL"}]""";

    [Fact]
    public void HoursRequireLabelledUnambiguousRawUnits()
    {
        Assert.Equal(12345, ApiPowerOnHours.Normalize(Json(Attributes)));
        Assert.Equal(0, ApiPowerOnHours.Normalize(Json(Attributes.Replace("12345", "0"))));
        foreach (var payload in new[] { "[]", Attributes.Replace("Power_On_Hours", "Power_On_Minutes"),
            Attributes.Replace("\"12345\"", "\"12345h+10m\""), Attributes.Replace("12345", "-1"),
            Attributes.Replace("12345", "9007199254740992"), Attributes.Replace("\"id\":9", "\"id\":\"9\""),
            "[" + Attributes[1..^1] + "," + Attributes[1..^1] + "]" })
            Assert.Null(ApiPowerOnHours.Normalize(Json(payload)));
        Assert.Throws<InvalidDataException>(() => ApiPowerOnHours.Normalize(Json("{}")));
    }
    [Fact]
    public async Task DedicatedReadCannotBeUsedAsAnArbitraryRpcOrWithOptions()
    {
        await using var client = new TrueNasClient();
        await Assert.ThrowsAsync<InvalidOperationException>(() => client.ReadAsync("disk.smart_attributes", default));
        Assert.True(TrueNasClient.AllowsReadParameters("disk.smart_attributes", ["sda"]));
        foreach (object[] args in new object[][] { [], ["sda", new { }], ["/dev/sda"], ["../sda"], ["sda\n"], ["sda;reboot"] }) {
            Assert.False(TrueNasClient.AllowsReadParameters("disk.smart_attributes", args));
            var method = typeof(TrueNasClient).GetMethod("CallInternalAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
            await Assert.ThrowsAsync<InvalidOperationException>(() => (Task<JsonElement>)method.Invoke(client, ["disk.smart_attributes", args, CancellationToken.None])!);
        }
    }
    [Fact]
    public async Task CollectSeparatesUnsupportedDeniedAndHotSwappedDrives()
    {
        var client = new Client(); var revision = Guid.NewGuid();
        var snapshot = await ApiPowerOnHours.CollectAsync(client, Json(Inventory), revision, default);
        Assert.Equal(12345, snapshot.Readings[0].Hours);
        Assert.Equal("unsupported", snapshot.Readings[1].Status);
        Assert.Equal("unsupported", snapshot.Readings[2].Status);
        Assert.DoesNotContain("nvme0n1", client.ReadDevices);
        var saved = JsonSerializer.Serialize(snapshot);
        client.Denied = true;
        snapshot = await ApiPowerOnHours.CollectAsync(client, Json(Inventory), revision, default, saved);
        Assert.Equal(12345, snapshot.Readings[0].Hours);
        Assert.NotNull(snapshot.Readings[0].ReadUtc);
        Assert.Equal("denied", snapshot.Readings[0].Status);
        client.After = Inventory.Replace("SERIAL", "REPLACEMENT");
        snapshot = await ApiPowerOnHours.CollectAsync(client, Json(Inventory), revision, default);
        Assert.DoesNotContain(snapshot.Readings, x => x.Device == "sda");
    }
    [Fact]
    public async Task CollectorIsOptInThrottledAndDisplaySurvivesWithoutSshFlag()
    {
        using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        await using var db = new HomelabDb(new DbContextOptionsBuilder<HomelabDb>().UseSqlite(connection).Options); await db.Database.MigrateAsync();
        var protection = new EphemeralDataProtectionProvider();
        var nas = new NasConnection { ProtectedApiKey = protection.CreateProtector("nas-credentials").Protect("fixture") };
        var settings = new OperatingSettings(); db.Connections.Add(nas); db.Settings.Add(settings); await db.SaveChangesAsync();
        using var services = new ServiceCollection().AddDbContext<HomelabDb>(o => o.UseSqlite(connection)).BuildServiceProvider(); var factory = new Factory();
        var collector = new Collector(services.GetRequiredService<IServiceScopeFactory>(), protection, NullLogger<Collector>.Instance, new WriteCoordinator(), factory);
        await collector.CollectAsync(nas, db, default);
        Assert.Empty(factory.Client.ReadDevices); Assert.Empty(await db.ApiPowerOnStates.ToArrayAsync());
        settings.ReadApiPowerOnHours = true; await db.SaveChangesAsync();
        await collector.CollectAsync(nas, db, default);
        Assert.Equal(new[] { "sda", "sdb" }, factory.Client.ReadDevices);
        var page = await StoragePage.ReadAsync(db, DateTime.UtcNow);
        Assert.False(page.SmartEnabled);
        var drives = page.Servers.Single().LooseDrives;
        Assert.Equal(12345, drives.Single(x => x.Disk.Serial == "SERIAL").ApiPowerOnHours!.Hours);
        Assert.Equal("unsupported", drives.Single(x => x.Disk.Serial == "SAS").ApiPowerOnHours!.Status);
        await collector.CollectAsync(nas, db, default);
        Assert.Equal(2, factory.Client.ReadDevices.Count);
        var manual = await collector.RunPowerOnHoursNowAsync(default);
        Assert.Equal(200, ((IStatusCodeHttpResult)manual).StatusCode);
        Assert.Equal(4, factory.Client.ReadDevices.Count);
        factory.Client.ReadStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        factory.Client.ContinueRead = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var running = collector.RunPowerOnHoursNowAsync(default);
        await factory.Client.ReadStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(409, ((IStatusCodeHttpResult)await collector.RunPowerOnHoursNowAsync(default)).StatusCode);
        factory.Client.ContinueRead.SetResult();
        Assert.Equal(200, ((IStatusCodeHttpResult)await running).StatusCode);
        Assert.Equal(6, factory.Client.ReadDevices.Count);
        settings.ReadApiPowerOnHours = false; await db.SaveChangesAsync();
        Assert.Equal(400, ((IStatusCodeHttpResult)await collector.RunPowerOnHoursNowAsync(default)).StatusCode);
        Assert.Equal(6, factory.Client.ReadDevices.Count);
        settings.ReadApiPowerOnHours = true; await db.SaveChangesAsync();
        Assert.Empty(await db.Observations.ToArrayAsync()); Assert.Empty(await db.Findings.ToArrayAsync());
        var json = (await db.ApiPowerOnStates.SingleAsync()).SnapshotJson;
        var disk = await db.Disks.SingleAsync(x => x.Serial == "SERIAL");
        Assert.Equal("stale", ApiPowerOnHours.ForDrive(nas, disk, true, DateTime.UtcNow.AddHours(19), json).Status);
        nas.ConfigurationRevision = Guid.NewGuid();
        Assert.Equal("unavailable", ApiPowerOnHours.ForDrive(nas, disk, true, DateTime.UtcNow, json).Status);
        Assert.True(ApiPowerOnHours.Due(nas, json, DateTime.UtcNow));
        Assert.Equal("disabled", ApiPowerOnHours.ForDrive(nas, disk, false, DateTime.UtcNow, json).Status);
    }
    private class Factory : INasReadClientFactory
    {
        public Client Client { get; } = new();
        public INasReadClient Create() => Client;
    }
    private class Client : INasReadClient
    {
        public string After = Inventory;
        public bool Denied;
        public List<string> ReadDevices = [];
        public TaskCompletionSource? ReadStarted, ContinueRead;
        public Task ConnectAsync(NasConnection profile, string key, CancellationToken ct) => Task.CompletedTask;
        public async Task<JsonElement> ReadSmartAttributesAsync(string device, CancellationToken ct)
        {
            ReadDevices.Add(device);
            ReadStarted?.TrySetResult();
            if (ContinueRead is not null) await ContinueRead.Task.WaitAsync(ct);
            if (Denied) throw new NasReadException("denied");
            if (device == "sdb") throw new NasReadException("unsupported");
            return Json(Attributes);
        }
        public Task<JsonElement> ReadAsync(string method, CancellationToken ct) => Task.FromResult(Json(method switch {
            "system.info" => """{"version":"TrueNAS-SCALE-24.10.2.4"}""", "disk.query" => After,
            "pool.query" or "alert.list" => "[]", _ => throw new InvalidOperationException()
        }));
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
