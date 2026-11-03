
namespace Homelab.Api;
public record VdevDailyNode(VdevNodeEvidence Node, VdevCounterRange Read, VdevCounterRange Write, VdevCounterRange Checksum);
