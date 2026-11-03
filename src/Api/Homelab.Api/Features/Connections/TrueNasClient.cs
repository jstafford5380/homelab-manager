using System.Collections.Frozen;
using System.Collections.Concurrent;
using System.Net.Sockets;
using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Net.WebSockets;
using System.Text.Json;

namespace Homelab.Api;
public sealed class TrueNasClient : INasReadClient
{
    public static readonly FrozenSet<string> ReadMethods = new[]
    {
        "system.info",
        "disk.query",
        "pool.query",
        "alert.list",
        "disk.temperatures",
        "pool.dataset.query"
    }.ToFrozenSet(StringComparer.Ordinal);
    public static readonly FrozenSet<string> EventCollections = new[]
    {
        "alert.list",
        "pool.query",
        "disk.query"
    }.ToFrozenSet(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, string> subscriptions = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, string> subscriptionIds = new(StringComparer.Ordinal);
    private long eventSequence, inventoryEventSequence;
    public long EventSequence => Interlocked.Read(ref eventSequence);
    public long InventoryEventSequence => Interlocked.Read(ref inventoryEventSequence);

    private readonly ClientWebSocket socket = new();
    private HttpMessageInvoker? invoker;
    private readonly CancellationTokenSource lifetime = new();
    private readonly SemaphoreSlim calls = new(1, 1);
    private readonly SemaphoreSlim sends = new(1, 1);
    private Task? receiver;
    private PendingCall? pending;
    private int stopped;
    private sealed record PendingCall(string Id, TaskCompletionSource<JsonElement> Completion);
    public bool IsConnected => Volatile.Read(ref stopped) == 0 && socket.State == WebSocketState.Open;
    public string? ServerCertificateSha256 { get; private set; }

    public async Task ConnectAsync(NasConnection profile, string key, CancellationToken ct)
    {
        var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            UseProxy = false
        };
        handler.SslOptions.RemoteCertificateValidationCallback = (_, certificate, _, errors) =>
        {
            if (certificate is null || (errors & SslPolicyErrors.RemoteCertificateNameMismatch) != 0 || (errors & SslPolicyErrors.RemoteCertificateNotAvailable) != 0)
                return false;
            using var presented = new X509Certificate2(certificate);
            if (!ValidateCertificate(presented, errors, profile.TrustedCaPem, profile.CertificateSha256))
                return false;
            ServerCertificateSha256 = presented.GetCertHashString(HashAlgorithmName.SHA256);
            return true;
        };
        if (!string.IsNullOrWhiteSpace(profile.ConnectHost))
            handler.ConnectCallback = async (context, token) =>
            {
                var tcp = new Socket(SocketType.Stream, ProtocolType.Tcp);
                try
                {
                    await tcp.ConnectAsync(profile.ConnectHost, profile.ConnectPort ?? context.DnsEndPoint.Port, token);
                    return new NetworkStream(tcp, ownsSocket: true);
                }
                catch
                {
                    tcp.Dispose();
                    throw;
                }
            };
        invoker = new HttpMessageInvoker(handler);
        await socket.ConnectAsync(new Uri(profile.ApiUri), invoker, ct);
        await SendAsync(new { msg = "connect", version = "1", support = new[] { "1" } }, ct);
        var handshake = await ReceiveAsync(ct);
        if (handshake.GetProperty("msg").GetString() != "connected")
            throw new InvalidOperationException("Unsupported WebSocket handshake.");
        receiver = ReceiveLoopAsync();
        var authenticated = await CallInternalAsync("auth.login_with_api_key", new object[] { key }, ct);
        if (authenticated.ValueKind != JsonValueKind.True)
            throw new UnauthorizedAccessException("NAS authentication denied.");
    }

    public async Task<IReadOnlyDictionary<string, string>> SubscribeEventsAsync(CancellationToken ct)
    {
        foreach (var name in EventCollections.Order(StringComparer.Ordinal))
        {
            await calls.WaitAsync(ct);
            try
            {
                if (subscriptions.ContainsKey(name))
                    continue;
                if (!IsConnected)
                    throw new IOException("NAS session is disconnected.");
                var request = new PendingCall(Guid.NewGuid().ToString("N"), new(TaskCreationOptions.RunContinuationsAsynchronously));
                subscriptionIds[request.Id] = name;
                Volatile.Write(ref pending, request);
                try
                {
                    // Fixed broadcast collections only: no wildcard, event-source arguments or caller-selected name.
                    await SendAsync(new { msg = "sub", id = request.Id, name }, ct);
                    var frame = await request.Completion.Task.WaitAsync(ct);
                    if (frame.TryGetProperty("error", out var error) && error.ValueKind != JsonValueKind.Null)
                        throw NasReadException.FromError(error);
                    if (frame.GetProperty("msg").GetString() == "ready")
                        subscriptions.TryAdd(name, "supported");
                    else
                        subscriptions[name] = "unavailable";
                }
                catch (NasReadException ex)
                {
                    subscriptions[name] = ex.Status;
                }
                catch
                {
                    Stop();
                    throw;
                }
                finally
                {
                    Volatile.Write(ref pending, null);
                }
            }
            finally
            {
                calls.Release();
            }
        }

        return new Dictionary<string, string>(subscriptions);
    }

    public static bool ValidateCertificate(X509Certificate2 certificate, SslPolicyErrors errors, string? caPem, string? pinnedSha256)
    {
        if ((errors & (SslPolicyErrors.RemoteCertificateNameMismatch | SslPolicyErrors.RemoteCertificateNotAvailable)) != 0)
            return false;
        if (!string.IsNullOrWhiteSpace(pinnedSha256) && !certificate.GetCertHashString(HashAlgorithmName.SHA256).Equals(pinnedSha256.Replace(":", ""), StringComparison.OrdinalIgnoreCase))
            return false;
        if (string.IsNullOrWhiteSpace(caPem))
            return errors == SslPolicyErrors.None;
        using var root = X509Certificate2.CreateFromPem(caPem);
        using var chain = new X509Chain();
        chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
        chain.ChainPolicy.CustomTrustStore.Add(root);
        chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
        chain.ChainPolicy.DisableCertificateDownloads = true;
        chain.ChainPolicy.ApplicationPolicy.Add(new Oid("1.3.6.1.5.5.7.3.1"));
        return chain.Build(certificate);
    }

    public Task<JsonElement> ReadAsync(string method, CancellationToken ct)
    {
        if (!ReadMethods.Contains(method))
            throw new InvalidOperationException("Method is outside the read allowlist.");
        return CallInternalAsync(method, ReadParameters(method), ct);
    }

    public Task<JsonElement> ReadHardwareMetricsAsync(bool backfill, CancellationToken ct) => CallInternalAsync("reporting.netdata_get_data", HardwareParameters(backfill), ct);
    private static object[] HardwareParameters(bool backfill) => [new[]
    {
        new
        {
            name = "cpu"
        },
        new
        {
            name = "memory"
        },
        new
        {
            name = "arcsize"
        },
        new
        {
            name = "cputemp"
        }
    }, new
    {
        unit = backfill ? "DAY" : "HOUR",
        page = 1,
        aggregate = false
    }

    ];
    public Task<JsonElement> ReadSmartAttributesAsync(string disk, CancellationToken ct)
    {
        if (!ValidSmartDisk(disk))
            throw new InvalidOperationException("Invalid disk name.");
        return CallInternalAsync("disk.smart_attributes", [disk], ct);
    }

    public static bool ValidSmartDisk(string disk) => System.Text.RegularExpressions.Regex.IsMatch(disk, @"\A[A-Za-z0-9][A-Za-z0-9_.-]{0,127}\z", System.Text.RegularExpressions.RegexOptions.CultureInvariant);
    private static object[] ReadParameters(string method) => method switch
    {
        "disk.temperatures" => [Array.Empty<string>(), new Dictionary<string, bool>
        {
            ["only_cached"] = true
        }

        ],
        // No recursive children, snapshots, user properties, or caller-selected options.
        // One extra row detects an over-limit inventory; never present a truncated list as complete.
        "pool.dataset.query" => [Array.Empty<object>(), new
        {
            limit = 257,
            select = new[]
            {
                "id",
                "name",
                "pool",
                "type",
                "used",
                "available",
                "usedbysnapshots",
                "quota",
                "refquota"
            },
            extra = new
            {
                flat = true,
                retrieve_children = false,
                properties = new[]
                {
                    "used",
                    "available",
                    "usedbysnapshots",
                    "quota",
                    "refquota"
                }
            }
        }

        ],
        _ => []
    };
    // No public caller can choose temperature options. Verify again before transmission.
    public static bool AllowsReadParameters(string method, object[] parameters) => method == "reporting.netdata_get_data" ? new[]
    {
        false,
        true
    }.Any(backfill => JsonSerializer.Serialize(parameters) == JsonSerializer.Serialize(HardwareParameters(backfill))) : method == "disk.smart_attributes" ? parameters.Length == 1 && parameters[0] is string disk && ValidSmartDisk(disk) : ReadMethods.Contains(method) && JsonSerializer.Serialize(parameters) == JsonSerializer.Serialize(ReadParameters(method));
    private async Task<JsonElement> CallInternalAsync(string method, object[] parameters, CancellationToken ct)
    {
        // Enforce again at the last RPC boundary, including private callers.
        if (!ReadMethods.Contains(method) && method is not ("auth.login_with_api_key" or "disk.smart_attributes" or "reporting.netdata_get_data"))
            throw new InvalidOperationException("NAS mutation or unreviewed method rejected.");
        if (method != "auth.login_with_api_key" && !AllowsReadParameters(method, parameters))
            throw new InvalidOperationException("Unreviewed NAS read arguments rejected.");
        await calls.WaitAsync(ct);
        try
        {
            if (!IsConnected)
                throw new IOException("NAS session is disconnected.");
            var request = new PendingCall(Guid.NewGuid().ToString("N"), new(TaskCreationOptions.RunContinuationsAsynchronously));
            Volatile.Write(ref pending, request);
            try
            {
                await SendAsync(new { id = request.Id, msg = "method", method, @params = parameters }, ct);
                var frame = await request.Completion.Task.WaitAsync(ct);
                if (frame.TryGetProperty("error", out var error) && error.ValueKind != JsonValueKind.Null)
                    throw NasReadException.FromError(error);
                return frame.GetProperty("result").Clone();
            }
            catch (NasReadException)
            {
                throw;
            } // An RPC-specific denial does not corrupt the session.
            catch
            {
                Stop();
                throw;
            }
            finally
            {
                Volatile.Write(ref pending, null);
            }
        }
        finally
        {
            calls.Release();
        }
    }

    private async Task ReceiveLoopAsync()
    {
        try
        {
            while (!lifetime.IsCancellationRequested)
            {
                var frame = await ReceiveAsync(lifetime.Token);
                if (frame.TryGetProperty("msg", out var msg) && msg.GetString() == "ping")
                {
                    // Echo a server correlation id when supplied; heartbeat is not a NAS method.
                    object pong = frame.TryGetProperty("id", out var pingId) && pingId.ValueKind == JsonValueKind.String ? new
                    {
                        msg = "pong",
                        id = pingId.GetString()
                    }

                    : new
                    {
                        msg = "pong"
                    };
                    await SendAsync(pong, lifetime.Token);
                    frame = default;
                    continue;
                }

                var kind = msg.ValueKind == JsonValueKind.String ? msg.GetString() : null;
                var request = Volatile.Read(ref pending);
                var frameId = frame.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String ? id.GetString() : null;
                if (kind == "nosub" && frameId is not null && subscriptionIds.TryGetValue(frameId, out var removed))
                {
                    subscriptions[removed] = "unavailable";
                    Interlocked.Increment(ref eventSequence);
                }

                if (kind is "added" or "changed" or "removed" && frame.TryGetProperty("collection", out var collection) && collection.ValueKind == JsonValueKind.String && subscriptions.TryGetValue(collection.GetString()!, out var status) && status == "supported")
                {
                    Interlocked.Increment(ref eventSequence);
                    if (collection.GetString()is "disk.query" or "pool.query")
                        Interlocked.Increment(ref inventoryEventSequence);
                }

                if (request is not null && ((kind is "result" or "nosub" && frameId == request.Id) || kind == "ready" && frame.TryGetProperty("subs", out var ready) && ready.ValueKind == JsonValueKind.Array && ready.EnumerateArray().Any(x => x.ValueKind == JsonValueKind.String && x.GetString() == request.Id)))
                {
                    if (kind == "ready" && subscriptionIds.TryGetValue(request.Id, out var subscribed))
                        subscriptions[subscribed] = "supported";
                    request.Completion.TrySetResult(frame);
                }

                frame = default;
                request = null; // Do not retain a large payload while idle.
            // Unsolicited frames are not queued or treated as collected evidence.
            }
        }
        catch (Exception)
        {
            Volatile.Read(ref pending)?.Completion.TrySetException(new IOException("NAS session disconnected while receiving a response."));
        }
        finally
        {
            Stop();
        }
    }

    private void Stop()
    {
        if (Interlocked.Exchange(ref stopped, 1) != 0)
            return;
        lifetime.Cancel();
        socket.Abort();
    }

    private async Task SendAsync(object frame, CancellationToken ct)
    {
        await sends.WaitAsync(ct);
        try
        {
            await socket.SendAsync(JsonSerializer.SerializeToUtf8Bytes(frame).AsMemory(), WebSocketMessageType.Text, true, ct);
        }
        finally
        {
            sends.Release();
        }
    }

    private async Task<JsonElement> ReceiveAsync(CancellationToken ct)
    {
        using var stream = new MemoryStream();
        var buffer = new byte[16384];
        while (true)
        {
            var result = await socket.ReceiveAsync(buffer.AsMemory(), ct);
            if (result.MessageType == WebSocketMessageType.Close)
                throw new IOException("NAS disconnected.");
            if (stream.Length + result.Count > 4 * 1024 * 1024)
                throw new InvalidOperationException("NAS payload exceeded limit.");
            stream.Write(buffer, 0, result.Count);
            if (result.EndOfMessage)
                break;
        }

        using var doc = JsonDocument.Parse(stream.ToArray());
        return doc.RootElement.Clone();
    }

    public async ValueTask DisposeAsync()
    {
        Stop();
        if (receiver is not null)
            await receiver;
        socket.Dispose();
        invoker?.Dispose();
        lifetime.Dispose();
    }
}
