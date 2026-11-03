using System.Net;
using Lextm.SharpSnmpLib;
using Lextm.SharpSnmpLib.Messaging;
using Microsoft.EntityFrameworkCore;

namespace Homelab.Api;
public sealed class SynologyHardwareReader : ISynologyHardwareReader
{
    // Fixed scalar GETs only. No walks, SETs, DSM login, shell, or caller-selected OIDs/ports.
    public static readonly IReadOnlyDictionary<string, string> Oids = new Dictionary<string, string>
    {
        ["model"] = "1.3.6.1.4.1.6574.1.5.1.0",
        ["version"] = "1.3.6.1.4.1.6574.1.5.3.0",
        ["temperature"] = "1.3.6.1.4.1.6574.1.2.0",
        ["systemFan"] = "1.3.6.1.4.1.6574.1.4.1.0",
        ["cpuFan"] = "1.3.6.1.4.1.6574.1.4.2.0",
        ["cpuIdle"] = "1.3.6.1.4.1.2021.11.11.0",
        ["totalKiB"] = "1.3.6.1.4.1.2021.4.5.0",
        ["freeKiB"] = "1.3.6.1.4.1.2021.4.6.0",
        ["buffersKiB"] = "1.3.6.1.4.1.2021.4.14.0",
        ["cacheKiB"] = "1.3.6.1.4.1.2021.4.15.0"
    };
    public async Task<IReadOnlyDictionary<string, string>> ReadAsync(string host, string community, CancellationToken ct)
    {
        if (!IPAddress.TryParse(host, out var ip))
            throw new InvalidDataException("Use the server IP address.");
        var rows = await Messenger.GetAsync(VersionCode.V2, new IPEndPoint(ip, 161), new OctetString(community), Oids.Values.Select(o => new Variable(new ObjectIdentifier(o))).ToList(), ct);
        return rows.Where(v => v.Data.TypeCode is SnmpType.Integer32 or SnmpType.OctetString or SnmpType.Gauge32).Where(v => Oids.Values.Contains(v.Id.ToString()) && v.Data.ToString().Length <= 500).GroupBy(v => v.Id.ToString()).Where(g => g.Count() == 1).ToDictionary(g => Oids.Single(p => p.Value == g.Key).Key, g => g.Single().Data.ToString());
    }
}
