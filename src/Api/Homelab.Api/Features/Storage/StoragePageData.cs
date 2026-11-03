
namespace Homelab.Api;
public record StoragePageData(bool SmartEnabled, int ServicePlanYears, StorageServer[] Servers, InvestmentTotal[]? Investment = null);
