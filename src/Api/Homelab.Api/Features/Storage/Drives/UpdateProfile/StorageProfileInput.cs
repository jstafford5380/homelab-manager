
namespace Homelab.Api;
public record StorageProfileInput(Guid? Revision, string? Nickname, decimal? PurchaseCost, DateTime? PurchasedUtc, DateTime? InstalledUtc, DateTime? WarrantyEndUtc, bool DatesEstimated, string Notes, string Currency = "USD");
