using System.Security.Cryptography.X509Certificates;
using Microsoft.EntityFrameworkCore;

namespace Homelab.Api;
public static class ConnectionRules
{
    public static string? ValidateApiKey(string? key) => string.IsNullOrWhiteSpace(key) || key.Length > 8192 || key.Any(char.IsControl) ? "Enter an API key of 1–8192 characters without control characters." : null;
    public static string? Validate(ConnectionSettings input)
    {
        if (string.IsNullOrWhiteSpace(input.Name) || input.Name.Length > 200 || input.Name.Any(char.IsControl) || string.IsNullOrWhiteSpace(input.ApiUri) || input.ApiUri.Length > 2048 || input.ApiUri != input.ApiUri.Trim() || input.ApiUri.Any(char.IsControl) || !Uri.TryCreate(input.ApiUri, UriKind.Absolute, out var uri) || uri.Scheme != "wss" || uri.Port is < 1 or > 65535 || uri.AbsolutePath != "/websocket" || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Fragment))
            return "Use a named secure TrueNAS 24.10 endpoint ending in /websocket without embedded credentials.";
        if (input.PollSeconds is < 30 or > 3600 || input.ConnectPort is < 1 or > 65535 || string.IsNullOrWhiteSpace(input.ConnectHost) != (input.ConnectPort is null))
            return "Polling must be 30–3600 seconds; tunnel host and port must be set together.";
        if (input.ConnectHost is { Length: > 0 } host && (host.Length > 253 || host.Any(char.IsControl) || Uri.CheckHostName(host) == UriHostNameType.Unknown))
            return "Tunnel host must be a hostname or IP address.";
        if (input.CertificateSha256 is { Length: > 0 } pin && (pin.Length != 64 || !pin.All(Uri.IsHexDigit)))
            return "Certificate fingerprint must contain 64 hexadecimal SHA-256 characters.";
        if (!string.IsNullOrWhiteSpace(input.TrustedCaPem))
        {
            if (input.TrustedCaPem.Length > 20000 || input.TrustedCaPem.Contains("PRIVATE KEY", StringComparison.Ordinal))
                return "Upload only a public CA or self-signed certificate, never its private key.";
            try
            {
                using var cert = X509Certificate2.CreateFromPem(input.TrustedCaPem);
            }
            catch
            {
                return "Invalid PEM certificate.";
            }
        }

        return null;
    }
}
