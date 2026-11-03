namespace Homelab.Api;
public record HardwareServerView(Guid Id, string Name, string Host, string Platform, string? Version, string Freshness, DateTime? LastSuccessUtc, double? UptimeSeconds, int ComponentCount, int? WatchCount, int? NearLimitCount);
