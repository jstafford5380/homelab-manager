
namespace Homelab.Api;
public record RedundancyAssessment(string PoolGuid, string Status, string Explanation, VdevLayout[] Groups);
