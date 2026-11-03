
namespace Homelab.Api;
public record PlanningReason(string Kind, string Detail, Guid? FindingId = null);
