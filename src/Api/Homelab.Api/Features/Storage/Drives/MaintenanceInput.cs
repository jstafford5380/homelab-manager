
namespace Homelab.Api;
public record MaintenanceInput(Guid RequestId, DateTime OccurredUtc, string DateConfidence, string Kind, string Outcome, string Summary, string Evidence, string ConfirmedIdentity);
