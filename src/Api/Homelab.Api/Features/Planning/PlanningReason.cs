namespace Homelab.Api;
public record PlanningReason(string Kind, string Detail, Guid? FindingId = null, string? Summary = null,
    DateTime? EndsUtc = null, DateTime? PurchasedUtc = null, string? Confidence = null);
