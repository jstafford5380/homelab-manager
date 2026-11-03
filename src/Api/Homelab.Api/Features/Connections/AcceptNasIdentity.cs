
namespace Homelab.Api;
public record AcceptNasIdentity(string PreviousSerial, string NewSerial, string Evidence, Guid? ConfigurationRevision = null);
