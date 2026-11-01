using System.Security.Cryptography.X509Certificates;
using Microsoft.EntityFrameworkCore;
namespace Homelab.Api;
public static class ConnectionEndpoints
{
    public static string? ValidateApiKey(string? key) => string.IsNullOrWhiteSpace(key) || key.Length > 8192 || key.Any(char.IsControl)
        ? "Enter an API key of 1–8192 characters without control characters." : null;
    public static string? Validate(ConnectionSettings input)
    {
        if (string.IsNullOrWhiteSpace(input.Name) || input.Name.Length > 200 || input.Name.Any(char.IsControl) || string.IsNullOrWhiteSpace(input.ApiUri) || input.ApiUri.Length > 2048 || input.ApiUri != input.ApiUri.Trim() || input.ApiUri.Any(char.IsControl) || !Uri.TryCreate(input.ApiUri, UriKind.Absolute, out var uri) || uri.Scheme != "wss" || uri.Port is < 1 or > 65535 || uri.AbsolutePath != "/websocket" || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Fragment)) return "Use a named secure TrueNAS 24.10 endpoint ending in /websocket without embedded credentials.";
        if (input.PollSeconds is < 30 or > 3600 || input.ConnectPort is < 1 or > 65535 || string.IsNullOrWhiteSpace(input.ConnectHost) != (input.ConnectPort is null)) return "Polling must be 30–3600 seconds; tunnel host and port must be set together.";
        if (input.ConnectHost is { Length: > 0 } host && (host.Length > 253 || host.Any(char.IsControl) || Uri.CheckHostName(host) == UriHostNameType.Unknown)) return "Tunnel host must be a hostname or IP address.";
        if (input.CertificateSha256 is { Length: > 0 } pin && (pin.Length != 64 || !pin.All(Uri.IsHexDigit))) return "Certificate fingerprint must contain 64 hexadecimal SHA-256 characters.";
        if (!string.IsNullOrWhiteSpace(input.TrustedCaPem)) {
            if (input.TrustedCaPem.Length > 20000 || input.TrustedCaPem.Contains("PRIVATE KEY", StringComparison.Ordinal)) return "Upload only a public CA or self-signed certificate, never its private key.";
            try { using var cert = X509Certificate2.CreateFromPem(input.TrustedCaPem); } catch { return "Invalid PEM certificate."; }
        }
        return null;
    }
    public static void MapConnections(this RouteGroupBuilder api)
    {
        api.MapGet("/connections", async (HomelabDb db) => await db.Connections.Select(c => new { c.Id, c.ConfigurationRevision, c.Name, c.ApiUri, c.ConnectHost, c.ConnectPort, c.PollSeconds, c.Enabled, c.TrustedCaPem, c.CertificateSha256, c.Status, c.Version, c.LastSuccessUtc, c.SystemSerial, c.ObservedSystemSerial, c.SystemIdentityState, c.SystemIdentityCheckedUtc }).ToListAsync());
        api.MapPut("/connections/{id:guid}/identity", async (Guid id, AcceptNasIdentity input, HomelabDb db, HttpContext ctx) => {
            var connection = await db.Connections.FindAsync(id); if (connection is null) return Results.NotFound();
            if (input.ConfigurationRevision != connection.ConfigurationRevision)
                return Results.Conflict(new { error = "This connection changed since the identity review was loaded. Reload it and verify the current endpoint, certificate and serials." });
            if (NasIdentity.ValidateAcceptance(connection, input) is {} error) return Results.BadRequest(new { error });
            NasIdentity.Accept(db, connection, input, ctx.User.Identity!.Name!);
            await db.SaveChangesAsync(); return Results.Ok();
        });
        api.MapPut("/connections/{id:guid}", async (Guid id, ConnectionSettings input, HomelabDb db, HttpContext ctx) => {
            var connection = await db.Connections.FindAsync(id); if (connection is null) return Results.NotFound();
            if (Validate(input) is {} error) return Results.BadRequest(new { error });
            if (input.ConfigurationRevision != connection.ConfigurationRevision)
                return Results.Conflict(new { error = "This connection changed since it was loaded. Reload the saved connection and review it before saving." });
            if (input.Enabled && string.IsNullOrWhiteSpace(connection.ProtectedApiKey)) return Results.BadRequest(new { error = "Re-enroll a credential before enabling collection." });
            var contextChanged = connection.ApiUri != input.ApiUri || connection.ConnectHost != input.ConnectHost ||
                connection.ConnectPort != input.ConnectPort || connection.TrustedCaPem != input.TrustedCaPem ||
                connection.CertificateSha256 != (string.IsNullOrWhiteSpace(input.CertificateSha256) ? null : input.CertificateSha256);
            if (contextChanged) await NasIdentity.InvalidateConnectionContextAsync(db, connection);
            connection.Name = input.Name; connection.ApiUri = input.ApiUri; connection.ConnectHost = input.ConnectHost; connection.ConnectPort = input.ConnectPort;
            connection.PollSeconds = input.PollSeconds; connection.Enabled = input.Enabled; connection.TrustedCaPem = input.TrustedCaPem; connection.CertificateSha256 = string.IsNullOrWhiteSpace(input.CertificateSha256) ? null : input.CertificateSha256;
            connection.ConfigurationRevision = Guid.NewGuid(); connection.NextAttemptUtc = null; connection.Status = input.Enabled ? "Configuration changed — awaiting validation" : "Collection paused by owner";
            db.Activity.Add(new() { Actor = ctx.User.Identity!.Name!, Action = "NAS connection/trust configuration changed", Detail = id.ToString() }); await db.SaveChangesAsync(); return Results.Ok();
        });
    }
}
public record ConnectionSettings(string Name, string ApiUri, string? ConnectHost, int? ConnectPort, int PollSeconds, bool Enabled, string? TrustedCaPem, string? CertificateSha256, Guid? ConfigurationRevision = null);
