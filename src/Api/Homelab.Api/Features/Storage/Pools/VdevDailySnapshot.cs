
namespace Homelab.Api;
public record VdevDailySnapshot(string Schema, string Status, string Explanation, long Samples, VdevDailyNode[] Nodes);
