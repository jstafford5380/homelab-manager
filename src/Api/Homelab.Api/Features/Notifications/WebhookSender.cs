using System.Net;
using System.Net.Sockets;
using System.Text;
using Microsoft.EntityFrameworkCore;

namespace Homelab.Api;
public sealed class WebhookSender(IConfiguration config, IServiceScopeFactory scopes) : IWebhookSender
{
    public static bool IsForbiddenAddress(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6)
            address = address.MapToIPv4();
        var bytes = address.GetAddressBytes();
        return IPAddress.IsLoopback(address) || address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any) || address.IsIPv6LinkLocal || address.IsIPv6Multicast || address.AddressFamily == AddressFamily.InterNetwork && (bytes[0] == 169 && bytes[1] == 254 || bytes[0] >= 224 || bytes[0] == 0);
    }

    public static bool IsAllowedUrl(Uri uri, IConfiguration config) => uri.Scheme == "https" && string.IsNullOrEmpty(uri.UserInfo) && string.IsNullOrEmpty(uri.Fragment) && config.GetSection("Homelab:WebhookHosts").Get<string[]>()?.Contains(uri.IdnHost, StringComparer.OrdinalIgnoreCase) == true;
    public async Task<int> SendAsync(Uri uri, string payload, CancellationToken ct)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(10));
        ct = deadline.Token;
        var telegram = TelegramNotifications.IsEndpoint(uri);
        if (!telegram && !IsAllowedUrl(uri, config))
            throw new InvalidOperationException("Webhook host is not enrolled in server configuration.");
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HomelabDb>();
        var nasHosts = (await db.Connections.AsNoTracking().ToListAsync(ct)).SelectMany(n => new[] { new Uri(n.ApiUri).IdnHost, n.ConnectHost }).Where(h => !string.IsNullOrWhiteSpace(h)).Distinct().ToArray();
        var nasAddresses = new HashSet<IPAddress>();
        foreach (var host in nasHosts)
            foreach (var address in await Dns.GetHostAddressesAsync(host!, ct))
                nasAddresses.Add(address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address);
        using var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            UseProxy = false
        };
        handler.ConnectCallback = async (context, token) =>
        {
            var addresses = await Dns.GetHostAddressesAsync(context.DnsEndPoint.Host, token);
            if (addresses.Length == 0 || addresses.Any(a => IsForbiddenAddress(a) || nasAddresses.Contains(a.IsIPv4MappedToIPv6 ? a.MapToIPv4() : a)))
                throw new InvalidOperationException("Webhook address is not allowed.");
            var socket = new Socket(SocketType.Stream, ProtocolType.Tcp);
            try
            {
                await socket.ConnectAsync(addresses, context.DnsEndPoint.Port, token);
                return new NetworkStream(socket, true);
            }
            catch
            {
                socket.Dispose();
                throw;
            }
        };
        using var client = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(10)
        };
        using var message = new HttpRequestMessage(HttpMethod.Post, uri)
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };
        using var suppressTelemetry = OpenTelemetry.SuppressInstrumentationScope.Begin();
        using var response = await client.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, ct);
        if (telegram && response.IsSuccessStatusCode)
        {
            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var body = new MemoryStream();
            var buffer = new byte[4096];
            int count;
            while ((count = await stream.ReadAsync(buffer, ct)) > 0)
            {
                if (body.Length + count > 65536)
                    throw new InvalidDataException("Notifier response exceeded its bound.");
                body.Write(buffer, 0, count);
            }

            if (!TelegramNotifications.Accepted(Encoding.UTF8.GetString(body.ToArray())))
                throw new InvalidDataException("Telegram did not confirm acceptance.");
        }

        return (int)response.StatusCode;
    }
}
