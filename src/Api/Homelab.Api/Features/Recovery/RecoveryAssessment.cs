
namespace Homelab.Api;
public sealed record RecoveryAssessment(string Protection, string RecoveryPoint, string Restore, string Rto, DateTime? RepresentedPointUtc, string LatestAttempt);
