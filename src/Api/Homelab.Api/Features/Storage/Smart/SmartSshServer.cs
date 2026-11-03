
namespace Homelab.Api;
public sealed class SmartSshServer
{
    public Guid ConnectionId { get; set; }
    public string Host { get; set; } = "";
    public int Port { get; set; } = 22;
    public string User { get; set; } = "homelab-monitor";
    public string KeyPath { get; set; } = "";
    public string KnownHostsPath { get; set; } = "";
    public int IntervalHours { get; set; } = 6;
    public bool ConfirmAtaCounterUnits { get; set; }
}
