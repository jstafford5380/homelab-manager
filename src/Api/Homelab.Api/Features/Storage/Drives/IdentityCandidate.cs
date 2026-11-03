using Microsoft.EntityFrameworkCore;

namespace Homelab.Api;
public sealed record IdentityCandidate(string Serial, string Model, string? Wwn, string Alias)
{
    public static string? NormalizeWwn(string? value)
    {
        var normalized = (value ?? "").Trim().ToLowerInvariant().Replace("0x", "").Replace(":", "").Replace("-", "");
        return normalized.Length is 16 or 32 && normalized.All(Uri.IsHexDigit) && normalized.Any(c => c != '0') ? normalized : null;
    }

    public bool HasSerial => !string.IsNullOrWhiteSpace(Serial) && Serial.Trim().Any(c => c != '0');
    public string? Key => NormalizeWwn(Wwn)is { } wwn ? "wwn:" + wwn : HasSerial && !string.IsNullOrWhiteSpace(Model) ? Model.Trim() + ":" + Serial.Trim() : null;
}
