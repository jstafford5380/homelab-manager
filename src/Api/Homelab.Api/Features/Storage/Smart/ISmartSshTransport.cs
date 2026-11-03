
namespace Homelab.Api;
public interface ISmartSshTransport
{
    Task<SmartCommandResult> RunAsync(SmartSshServer server, string command, CancellationToken ct);
}
