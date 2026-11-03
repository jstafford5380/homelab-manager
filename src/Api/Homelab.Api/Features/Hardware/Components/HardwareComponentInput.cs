
namespace Homelab.Api;
public record HardwareComponentInput(Guid? Revision, string Kind, string Name, string Model, string Spec, string Notes, decimal? PricePaid, DateTime? PurchasedUtc, DateTime? InstalledUtc, DateTime? WarrantyEndUtc, bool DatesEstimated, HardwareSlot[]? Slots = null, double? SolderedBytes = null, double? MaxSupportedBytes = null);
