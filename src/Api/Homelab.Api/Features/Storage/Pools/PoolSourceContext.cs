
namespace Homelab.Api;
public record PoolSourceContext(string Schema, string? Serial, string? Version, Guid ConfigurationRevision, DateTime ReadStartedUtc, DateTime ReadFinishedUtc, double? UptimeSeconds);
