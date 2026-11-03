
namespace Homelab.Api;
public record MetricEvidence(string Metric, string Label, string Freshness, string Explanation, Observation? LatestSubmission, Observation? LatestObserved);
