
namespace Homelab.Api;
public record DiskMetadata(string? Bay, bool BayVerified, string? ConfirmedSerial, string? BayEvidence, DateTime? PurchasedUtc, string PurchaseDateConfidence, DateTime? WarrantyEndUtc, string Notes, string Lifecycle);
