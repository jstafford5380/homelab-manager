
namespace Homelab.Api;
public record ReportReading(string Metric, double? Value, string Unit, string Quality, string? MissingReason);
