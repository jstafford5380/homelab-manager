
namespace Homelab.Api;
public interface IWebhookSender
{
    Task<int> SendAsync(Uri uri, string payload, CancellationToken ct);
}
