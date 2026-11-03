namespace Homelab.Api.Features.Notifications.GetSettings;
public sealed record ResponseChannel(string Kind, string Host, bool Enabled, bool MaskIdentifiers, DateTime? TestedUtc, DateTime? MaintenanceUntilUtc);
public sealed record ResponseDeliveriesItem(Guid Id, Guid FindingId, string Kind, string Status, int Attempts, DateTime CreatedUtc, DateTime? AcceptedUtc);
public sealed record Response(ResponseChannel? Channel, List<ResponseDeliveriesItem> Deliveries, List<DeliveryAttempt> Attempts);
