
namespace Homelab.Api;
public record ApiPowerOnSnapshot(Guid Revision, DateTime FetchedUtc, ApiPowerOnReading[] Readings);
