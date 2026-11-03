
namespace Homelab.Api;
public record CredentialRotation(string ApiKey, Guid? ConfigurationRevision = null);
