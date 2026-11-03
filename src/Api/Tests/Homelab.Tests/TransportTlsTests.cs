using Homelab.Api;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Net;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
namespace Homelab.Tests;
public class TransportTlsTests
{
    [Fact] public async Task TunnelPreservesLogicalHostnameAndOnlyApprovedRpcReachesServer()
    {
        await using var server = await FixtureServer.Start(); await using var client = new TrueNasClient(); using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await client.ConnectAsync(server.Profile(), "fixture-key", timeout.Token);
        Assert.Equal("nas.fixture", server.Host);
        await client.ReadAsync("disk.query", timeout.Token);
        await client.ReadAsync("disk.temperatures", timeout.Token);
        await client.ReadAsync("pool.dataset.query", timeout.Token);
        await client.ReadSmartAttributesAsync("sda", timeout.Token);
        Assert.Equal("[\"sda\"]", Assert.Single(server.SmartArguments));
        await Assert.ThrowsAsync<InvalidOperationException>(() => client.ReadSmartAttributesAsync("sda;reboot", timeout.Token));
        using var datasetArguments = JsonDocument.Parse(Assert.Single(server.DatasetArguments));
        var datasetOptions = datasetArguments.RootElement[1];
        Assert.Equal(257, datasetOptions.GetProperty("limit").GetInt32());
        Assert.Empty(datasetArguments.RootElement[0].EnumerateArray());
        Assert.Equal(new[] { "id", "name", "pool", "type", "used", "available", "usedbysnapshots", "quota", "refquota" }, datasetOptions.GetProperty("select").EnumerateArray().Select(x => x.GetString()));
        var extra = datasetOptions.GetProperty("extra"); Assert.True(extra.GetProperty("flat").GetBoolean()); Assert.False(extra.GetProperty("retrieve_children").GetBoolean());
        Assert.Equal(new[] { "used", "available", "usedbysnapshots", "quota", "refquota" }, extra.GetProperty("properties").EnumerateArray().Select(x => x.GetString()));
        Assert.Equal(3, extra.EnumerateObject().Count());
        Assert.Equal(3, datasetOptions.EnumerateObject().Count());
        using var cacheArguments = JsonDocument.Parse(Assert.Single(server.TemperatureArguments));
        Assert.Equal(0, cacheArguments.RootElement[0].GetArrayLength());
        Assert.True(cacheArguments.RootElement[1].GetProperty("only_cached").GetBoolean());
        Assert.Single(cacheArguments.RootElement[1].EnumerateObject());
        await Assert.ThrowsAsync<InvalidOperationException>(() => client.ReadAsync("pool.scrub", timeout.Token));
        Assert.Equal(new[] { "auth.login_with_api_key", "disk.query", "disk.temperatures", "pool.dataset.query", "disk.smart_attributes" }, server.Methods.ToArray());
        Assert.Equal(server.Certificate.GetCertHashString(HashAlgorithmName.SHA256), client.ServerCertificateSha256);
    }
    [Fact] public async Task IdleHeartbeatIsAnsweredAndConcurrentReadsStayCorrelated()
    {
        await using var server = await FixtureServer.Start(idlePing:true);
        await using var client = new TrueNasClient(); using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await client.ConnectAsync(server.Profile(),"fixture-key",timeout.Token);
        Assert.Equal("idle-heartbeat",await server.Pong.Task.WaitAsync(timeout.Token));
        Assert.True(client.IsConnected);
        var results = await Task.WhenAll(client.ReadAsync("disk.query",timeout.Token),client.ReadAsync("pool.query",timeout.Token));
        Assert.All(results,x=>Assert.Equal(JsonValueKind.Array,x.ValueKind));
        Assert.Equal(3,server.Methods.Count);
        Assert.Equal("auth.login_with_api_key",server.Methods.First());
        await client.DisposeAsync();Assert.False(client.IsConnected);
    }
    [Fact] public async Task IdleDisconnectPreventsFurtherReadsOnOldSession()
    {
        await using var server = await FixtureServer.Start(idlePing:true,disconnectAfterPong:true);
        await using var client = new TrueNasClient(); using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await client.ConnectAsync(server.Profile(),"fixture-key",timeout.Token);
        await server.Pong.Task.WaitAsync(timeout.Token);
        await Assert.ThrowsAnyAsync<Exception>(()=>client.ReadAsync("disk.query",timeout.Token));
        Assert.False(client.IsConnected);
    }
    [Fact] public async Task HealthySessionsAreReusedButRevisionCredentialAndPauseDiscardThem()
    {
        await using var server = await FixtureServer.Start(); await using var factory = new NasReadClientFactory();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));var profile=server.Profile();
        async Task Cycle(bool reconcile,string key="fixture-key") {
            await using var lease=factory.Create();await lease.ConnectAsync(profile,key,timeout.Token);
            Assert.Equal(reconcile,lease.RequiresInventoryReconciliation);
            await lease.ReadAsync("disk.query",timeout.Token);
            await Assert.ThrowsAsync<InvalidOperationException>(()=>lease.ReadAsync("pool.scrub",timeout.Token));
        }
        await Cycle(true);await Cycle(false);
        Assert.Equal(1,server.Methods.Count(x=>x=="auth.login_with_api_key"));
        profile.ConfigurationRevision=Guid.NewGuid();await Cycle(true);
        await Cycle(true,"rotated-key");await Cycle(false,"rotated-key");
        Assert.Equal(3,server.Methods.Count(x=>x=="auth.login_with_api_key"));
        await factory.RetainAsync(new Dictionary<Guid,Guid>(),timeout.Token);await Cycle(true,"rotated-key");
        Assert.Equal(4,server.Methods.Count(x=>x=="auth.login_with_api_key"));
        await factory.DisposeAsync();await using var stopped=factory.Create();
        await Assert.ThrowsAsync<ObjectDisposedException>(()=>stopped.ConnectAsync(profile,"fixture-key",timeout.Token));
    }
    [Fact] public async Task TimedOutReadDiscardsSessionAndNextLeaseReauthenticates()
    {
        await using var server=await FixtureServer.Start(stallDisk:true);await using var factory=new NasReadClientFactory();
        using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(10));var profile=server.Profile();
        await using (var first=factory.Create()) {
            await first.ConnectAsync(profile,"fixture-key",timeout.Token);
            using var readDeadline=new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
            await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>first.ReadAsync("disk.query",readDeadline.Token));
            Assert.False(first.IsConnected);
        }
        await using var next=factory.Create();await next.ConnectAsync(profile,"fixture-key",timeout.Token);
        Assert.True(next.RequiresInventoryReconciliation);await next.ReadAsync("pool.query",timeout.Token);
        Assert.Equal(2,server.Methods.Count(x=>x=="auth.login_with_api_key"));
    }
    [Fact] public async Task FixedSubscriptionsCoalesceEventsAndKeepDeniedCoverageSeparate()
    {
        await using var server=await FixtureServer.Start(eventBurst:true);await using var factory=new NasReadClientFactory();
        using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(10));var profile=server.Profile();
        await using (var first=factory.Create()) {
            await first.ConnectAsync(profile,"fixture-key",timeout.Token);
            var states=await first.SubscribeEventsAsync(timeout.Token);
            Assert.Equal("denied",states["disk.query"]);Assert.Equal("supported",states["pool.query"]);Assert.Equal("supported",states["alert.list"]);
            await first.ReadAsync("system.info",timeout.Token); // Wire barrier after the event burst.
            Assert.Equal(100,first.EventSequence);Assert.Equal(100,first.InventoryEventSequence);
        }
        Assert.Equal(new[]{profile.Id},factory.EventRefreshCandidates());
        await using (var next=factory.Create()) {
            await next.ConnectAsync(profile,"fixture-key",timeout.Token);Assert.True(next.RequiresInventoryReconciliation);
            await next.SubscribeEventsAsync(timeout.Token);await next.ReadAsync("pool.query",timeout.Token);
        }
        Assert.Empty(factory.EventRefreshCandidates());
        Assert.Equal(new[]{"alert.list","disk.query","pool.query"},server.Subscriptions.ToArray());
        Assert.Equal(1,server.Methods.Count(x=>x=="auth.login_with_api_key"));
    }
    [Fact] public async Task SubscriptionLossIsVisibleWithoutBreakingIndependentReads()
    {
        await using var server=await FixtureServer.Start(loseSubscription:true);await using var client=new TrueNasClient();
        using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(10));await client.ConnectAsync(server.Profile(),"fixture-key",timeout.Token);
        Assert.Equal("supported",(await client.SubscribeEventsAsync(timeout.Token))["alert.list"]);
        await client.ReadAsync("alert.list",timeout.Token);
        Assert.Equal("unavailable",(await client.SubscribeEventsAsync(timeout.Token))["alert.list"]);
        Assert.Equal(1,client.EventSequence);Assert.Equal(0,client.InventoryEventSequence);Assert.True(client.IsConnected);
        Assert.Equal(3,server.Subscriptions.Count);
    }
    [Theory] [InlineData("untrusted")] [InlineData("hostname")] [InlineData("changed-pin")]
    public async Task CertificateFailuresStopBeforeAuthentication(string failure)
    {
        await using var server = await FixtureServer.Start(); await using var client = new TrueNasClient(); using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var profile = server.Profile();
        if (failure == "untrusted") profile.TrustedCaPem = null;
        if (failure == "hostname") profile.ApiUri = "wss://wrong.fixture/websocket";
        if (failure == "changed-pin") profile.CertificateSha256 = new string('A', 64);
        await Assert.ThrowsAnyAsync<Exception>(() => client.ConnectAsync(profile, "fixture-key", timeout.Token));
        Assert.Empty(server.Methods);
    }
    private sealed class FixtureServer(WebApplication app, X509Certificate2 certificate, ConcurrentQueue<string> methods, ConcurrentQueue<string> temperatureArguments, ConcurrentQueue<string> datasetArguments, ConcurrentQueue<string> smartArguments, Func<string?> host, TaskCompletionSource<string?> pong, ConcurrentQueue<string> subscriptions) : IAsyncDisposable
    {
        public TaskCompletionSource<string?> Pong => pong;
        public ConcurrentQueue<string> Subscriptions => subscriptions;
        public X509Certificate2 Certificate => certificate;
        public ConcurrentQueue<string> Methods => methods;
        public ConcurrentQueue<string> TemperatureArguments => temperatureArguments;
        public ConcurrentQueue<string> DatasetArguments => datasetArguments;
        public ConcurrentQueue<string> SmartArguments => smartArguments;
        public string? Host => host();
        public NasConnection Profile() => new() { ApiUri = "wss://nas.fixture/websocket", ConnectHost = "127.0.0.1", ConnectPort = new Uri(app.Urls.Single()).Port, TrustedCaPem = certificate.ExportCertificatePem() };
        public static async Task<FixtureServer> Start(bool idlePing=false, bool disconnectAfterPong=false, bool stallDisk=false, bool eventBurst=false, bool loseSubscription=false)
        {
            using var rsa = RSA.Create(2048); var request = new CertificateRequest("CN=nas.fixture", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            var san = new SubjectAlternativeNameBuilder(); san.AddDnsName("nas.fixture"); request.CertificateExtensions.Add(san.Build());
            request.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
            using var created = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddDays(1));
            var certificate = X509CertificateLoader.LoadPkcs12(created.Export(X509ContentType.Pfx), null);
            var builder = WebApplication.CreateBuilder(); builder.Logging.ClearProviders();
            builder.WebHost.ConfigureKestrel(options => options.Listen(IPAddress.Loopback, 0, listener => listener.UseHttps(certificate)));
            var subscriptions = new ConcurrentQueue<string>();
            var pong = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
            var app = builder.Build(); app.UseWebSockets(); var methods = new ConcurrentQueue<string>(); var temperatureArguments = new ConcurrentQueue<string>(); var datasetArguments = new ConcurrentQueue<string>(); var smartArguments = new ConcurrentQueue<string>(); string? host = null;
            app.Map("/websocket", async context => {
                host = context.Request.Host.Host;
                if (!context.WebSockets.IsWebSocketRequest) { context.Response.StatusCode = 400; return; }
                using var socket = await context.WebSockets.AcceptWebSocketAsync(); var buffer = new byte[16384]; string? alertSubscriptionId=null;
                try {
                    while (socket.State == WebSocketState.Open) {
                        var result = await socket.ReceiveAsync(buffer.AsMemory(), context.RequestAborted); if (result.MessageType == WebSocketMessageType.Close) break;
                        using var doc = JsonDocument.Parse(buffer.AsMemory(0, result.Count)); var root = doc.RootElement;
                        if (root.GetProperty("msg").GetString() == "sub") {
                            var name=root.GetProperty("name").GetString()!;subscriptions.Enqueue(name);
                            var id=root.GetProperty("id").GetString();
                            if(name=="alert.list") alertSubscriptionId=id;
                            object subscriptionResponse=eventBurst && name=="disk.query" ? new {msg="result",id,error=new {error=13}} : new {msg="ready",subs=new[]{id}};
                            await socket.SendAsync(JsonSerializer.SerializeToUtf8Bytes(subscriptionResponse).AsMemory(),WebSocketMessageType.Text,true,context.RequestAborted);
                            if (eventBurst && name=="pool.query") {
                                for (var i=0;i<100;i++) await socket.SendAsync(JsonSerializer.SerializeToUtf8Bytes(new {msg="changed",collection="pool.query",id=i,fields=new {status="FAULTED"}}).AsMemory(),WebSocketMessageType.Text,true,context.RequestAborted);
                                await socket.SendAsync(JsonSerializer.SerializeToUtf8Bytes(new {msg="changed",collection="unsubscribed.private",id=1}).AsMemory(),WebSocketMessageType.Text,true,context.RequestAborted);
                            }
                            continue;
                        }
                        if (root.GetProperty("msg").GetString() == "pong") {
                            pong.TrySetResult(root.TryGetProperty("id",out var heartbeatId) ? heartbeatId.GetString() : null);
                            if (disconnectAfterPong) { await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure,"fixture disconnect",context.RequestAborted); break; }
                            continue;
                        }
                        object response;
                        if (root.GetProperty("msg").GetString() == "connect") response = new { msg = "connected", session = "fixture" };
                        else { var method = root.GetProperty("method").GetString()!; methods.Enqueue(method); if (method == "disk.smart_attributes") smartArguments.Enqueue(root.GetProperty("params").GetRawText()); if (method == "disk.temperatures") temperatureArguments.Enqueue(root.GetProperty("params").GetRawText()); if (method == "pool.dataset.query") datasetArguments.Enqueue(root.GetProperty("params").GetRawText()); response = new { msg = "result", id = root.GetProperty("id").GetString(), result = method == "auth.login_with_api_key" ? (object)true : Array.Empty<object>() }; }
                        if (loseSubscription && root.TryGetProperty("method",out var unsubscribedRead) && unsubscribedRead.GetString()=="alert.list")
                            await socket.SendAsync(JsonSerializer.SerializeToUtf8Bytes(new {msg="nosub",id=alertSubscriptionId}).AsMemory(),WebSocketMessageType.Text,true,context.RequestAborted);
                        if (stallDisk && root.TryGetProperty("method",out var stalled) && stalled.GetString() == "disk.query") continue;
                        await socket.SendAsync(JsonSerializer.SerializeToUtf8Bytes(response).AsMemory(), WebSocketMessageType.Text, true, context.RequestAborted);
                        if (idlePing && root.TryGetProperty("method",out var requestMethod) && requestMethod.GetString() == "auth.login_with_api_key") {
                            await socket.SendAsync(JsonSerializer.SerializeToUtf8Bytes(new {msg="result",id="not-a-pending-request",result="ignored"}).AsMemory(),WebSocketMessageType.Text,true,context.RequestAborted);
                            await socket.SendAsync(JsonSerializer.SerializeToUtf8Bytes(new {msg="ping",id="idle-heartbeat"}).AsMemory(),WebSocketMessageType.Text,true,context.RequestAborted);
                        }
                    }
                } catch (Exception) when (socket.State != WebSocketState.Open || context.RequestAborted.IsCancellationRequested) { }
            });
            await app.StartAsync(); return new(app, certificate, methods, temperatureArguments, datasetArguments, smartArguments, () => host, pong, subscriptions);
        }
        public async ValueTask DisposeAsync() { await app.StopAsync(); await app.DisposeAsync(); certificate.Dispose(); }
    }
}
