
namespace Homelab.Api;
public record AtaSelfTestLog(string Source, string Coverage, int? Revision, long? ReportedCount, string Quality, AtaSelfTestEntry[] Entries);
