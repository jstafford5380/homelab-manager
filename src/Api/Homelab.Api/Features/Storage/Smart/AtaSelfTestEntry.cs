
namespace Homelab.Api;
public record AtaSelfTestEntry(int Position, int? TypeCode, string? TypeText, int? StatusCode, string? StatusText, string Result, int? RemainingPercent, long? LifetimeHours, long? FirstErrorLba, bool? ReportedPassed);
