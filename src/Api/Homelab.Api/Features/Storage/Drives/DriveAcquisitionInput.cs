
namespace Homelab.Api;
public record DriveAcquisitionInput(Guid? Revision, DateTime? InstalledUtc, string InstallationConfidence, string InstallationEvidence, decimal? PurchaseCost, string Currency, string CostConfidence, string PurchaseEvidence);
