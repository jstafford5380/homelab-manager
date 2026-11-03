namespace Homelab.Api.Features.Storage.Drives.GetEvidenceCoverage;
public sealed record Response(DateTime EvaluatedUtc, int StaleAfterMinutes, MetricEvidence[] Items);
