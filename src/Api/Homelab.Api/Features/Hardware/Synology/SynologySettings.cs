using System.Net;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

namespace Homelab.Api;
public static class SynologySettings
{
    internal static async Task<IResult> Save(Guid? id, SynologyInput input, HomelabDb db, IConfiguration config, IDataProtectionProvider protection)
    {
        if (!HardwareInventory.SynologyEnabled(config))
            return Results.NotFound();
        if (string.IsNullOrWhiteSpace(input.Name) || input.Name.Length > 200 || !IPAddress.TryParse(input.Host, out _) || input.Community?.Length > 200)
            return Results.BadRequest(new { error = "Enter a name and server IP address; communities may contain up to 200 characters." });
        var s = id is { } sid ? await db.HardwareServers.SingleOrDefaultAsync(s => s.Id == sid && s.Platform == "synology") : new HardwareServer
        {
            Platform = "synology"
        };
        if (s is null)
            return Results.NotFound();
        if (input.Connect && string.IsNullOrWhiteSpace(input.Community) && s.ProtectedCommunity is null)
            return Results.BadRequest(new { error = "Enter a read-only SNMP community to connect." });
        s.Name = input.Name;
        s.Host = input.Host;
        s.Connected = input.Connect;
        s.ConfigurationRevision = Guid.NewGuid();
        s.NextAttemptUtc = null;
        s.CollectionStartedUtc = null;
        s.LastMetricsUtc = null;
        s.LastSuccessUtc = null;
        if (!string.IsNullOrWhiteSpace(input.Community))
            s.ProtectedCommunity = protection.CreateProtector("synology-snmp").Protect(input.Community);
        if (id is null)
            db.HardwareServers.Add(s);
        await db.SaveChangesAsync();
        return Results.Ok(new { s.Id });
    }
}
