
namespace Homelab.Api;
public record AtaReportAttribute(int Id, string? Name, string? RawNumeric, string? RawText, int? NormalizedValue, int? Worst, int? Threshold, string? WhenFailed);
