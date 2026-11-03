namespace Homelab.Api;
public record ConnectionInput(string Name, string ApiUri, string ApiKey, string? ConnectHost, int? ConnectPort, string? TrustedCaPem);
