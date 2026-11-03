namespace Homelab.Api;
public record HardwareCoverage(int Samples, int ExpectedSamples, DateTime? FirstSampleUtc, DateTime? CollectionStartedUtc, int Days, int CoveredDays, HardwareGap[] Gaps, bool Downsampled);
