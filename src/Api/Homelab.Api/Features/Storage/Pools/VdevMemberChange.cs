
namespace Homelab.Api;
public record VdevMemberChange(string Guid, VdevCounterChange Read, VdevCounterChange Write, VdevCounterChange Checksum);
