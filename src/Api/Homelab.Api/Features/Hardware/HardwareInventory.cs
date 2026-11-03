using Microsoft.EntityFrameworkCore;

namespace Homelab.Api;
public static class HardwareInventory
{
    public static bool MetricsEnabled(IConfiguration config) => config.GetValue<bool>("Homelab:HardwareMetricsEnabled");
    public static bool SynologyEnabled(IConfiguration config) => config.GetValue<bool>("Homelab:SynologyEnabled");
    internal static async Task<HardwareServer?> Server(Guid id, HomelabDb db, IConfiguration config) => await db.HardwareServers.SingleOrDefaultAsync(s => s.Id == id && (s.Platform != "synology" || SynologyEnabled(config)));
    public static async Task EnsureServers(HomelabDb db, CancellationToken ct)
    {
        var existing = await db.HardwareServers.Where(s => s.ConnectionId != null).Select(s => s.ConnectionId).ToArrayAsync(ct);
        foreach (var c in await db.Connections.Where(c => !existing.Contains(c.Id)).ToArrayAsync(ct))
            db.HardwareServers.Add(new() { Id = c.Id, ConnectionId = c.Id, Name = c.Name, ConfigurationRevision = c.ConfigurationRevision });
        await db.SaveChangesAsync(ct);
    }
}
