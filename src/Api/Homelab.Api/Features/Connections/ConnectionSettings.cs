
namespace Homelab.Api;
public record ConnectionSettings(string Name, string ApiUri, string? ConnectHost, int? ConnectPort, int PollSeconds, bool Enabled, string? TrustedCaPem, string? CertificateSha256, Guid? ConfigurationRevision = null);
