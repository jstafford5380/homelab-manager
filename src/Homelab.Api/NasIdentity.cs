using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;

namespace Homelab.Api;

public static class NasIdentity
{
    public static bool SupportsRelease(string? version) => version is { Length: <= 128 } &&
        Regex.IsMatch(version, @"\A(?:TrueNAS-SCALE-)?24\.10(?:\.[0-9]+){0,2}\z", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);

    // system.info exposes nullable DMI system_serial in the 24.10.2 contract. It is
    // supplementary hardware evidence, not a cryptographic or guaranteed unique identity.
    public static string? UsableSerial(string? raw)
    {
        var value = raw?.Trim();
        if (string.IsNullOrWhiteSpace(value) || value.Length > 200 || value.Any(char.IsControl)) return null;
        if (new[] { "unknown", "none", "n/a", "not applicable", "not specified", "default string", "system serial number", "to be filled by o.e.m.", "to be filled by oem", "123456789", "0123456789" }.Contains(value, StringComparer.OrdinalIgnoreCase)) return null;
        if (value.All(c => c is '0' or '-' or ' ') || value.All(c => c is 'f' or 'F')) return null;
        return value;
    }
    public static string? ReadSerial(JsonElement system) => system.ValueKind == JsonValueKind.Object && system.TryGetProperty("system_serial", out var value) && value.ValueKind == JsonValueKind.String ? UsableSerial(value.GetString()) : null;

    // Caller holds the local writer lease. This commits identity independently of
    // later inventory/pool reads so failures cannot silently discard a changed identity.
    public static async Task<bool> CheckAsync(HomelabDb db, NasConnection nas, string? serial, DateTime now, CancellationToken ct)
    {
        serial = UsableSerial(serial);
        nas.ObservedSystemSerial = serial; nas.SystemIdentityCheckedUtc = now;
        if (nas.SystemSerial is null && serial is not null) {
            nas.SystemSerial = serial; nas.SystemIdentityState = "observed";
            db.Activity.Add(new() { Action = "NAS system serial baseline recorded", Detail = nas.Id.ToString() });
        } else nas.SystemIdentityState = nas.SystemSerial is null ? "unavailable" : serial is null ? "missing" : nas.SystemSerial == serial ? "matched" : "changed";
        var blocked = nas.SystemIdentityState is "missing" or "changed";
        var capability = await db.Capabilities.SingleOrDefaultAsync(c => c.ConnectionId == nas.Id && c.Name == "system_identity", ct);
        if (capability is null) { capability = new() { ConnectionId = nas.Id, Name = "system_identity" }; db.Capabilities.Add(capability); }
        capability.LastAttemptUtc = now; capability.ExpectedSeconds = nas.PollSeconds;
        capability.Status = serial is not null && !blocked ? "supported" : "unavailable";
        capability.Detail = blocked ? "Reported system serial no longer matches the saved baseline. Collection paused before storage reads."
            : serial is null ? "No usable system serial is exposed. Hardware identity is unverified; certificate and hostname checks still apply."
            : "Reported system serial matches the saved baseline. DMI serials are not guaranteed unique; TLS identity checks remain required.";
        if (capability.Status == "supported") capability.LastSuccessUtc = now;
        var finding = await db.Findings.FirstOrDefaultAsync(f => f.ConnectionId == nas.Id && f.Rule == "system-identity-v1" && f.State != "resolved", ct);
        if (blocked) {
            nas.ConfigurationRevision = Guid.NewGuid();
            nas.Enabled = false; nas.Status = "System identity changed or missing — collection paused";
            if (finding is null) { finding = new() { ConnectionId = nas.Id, Rule = "system-identity-v1", Severity = "critical", Title = "NAS system identity needs review" }; db.Findings.Add(finding); }
            finding.Evidence = $"Saved system serial: {nas.SystemSerial}. Reported serial: {serial ?? "unavailable"}. Checked {now:O}. Storage observations were not collected from this response.";
            finding.NextStep = "Verify the endpoint, TLS certificate and physical NAS. Correct the connection before resuming. For a documented hardware change, explicitly accept the new serial with evidence in connection settings.";
            finding.EvidenceObservedUtc = now; finding.EvidenceSource = "system.info"; finding.EvidenceSchema = "truenas-24.10-system-v1"; finding.LastSeenUtc = now;
            db.Activity.Add(new() { Action = "NAS identity mismatch paused collection", Detail = nas.Id.ToString() });
        } else if (finding is not null && serial is not null) {
            FindingLifecycle.Resolve(finding, now, "A subsequent authenticated read reports the saved system serial. This resolves only the identity mismatch; other faults and recovery gaps remain independent.");
            db.Activity.Add(new() { Action = "NAS identity finding resolved by matching serial", Detail = finding.Id.ToString() });
        }
        return blocked;
    }

    // Saved observations remain history, but an endpoint/trust edit invalidates their current-context claim.
    public static async Task InvalidateConnectionContextAsync(HomelabDb db, NasConnection nas)
    {
        nas.ObservedSystemSerial = null;
        nas.SystemIdentityCheckedUtc = null;
        nas.SystemIdentityState = "awaiting validation";
        foreach (var capability in await db.Capabilities.Where(x => x.ConnectionId == nas.Id).ToListAsync()) {
            if (capability.Name is "smart" or "recovery" || capability.Status == "disabled") continue;
            capability.Status = "unavailable";
            capability.Detail = "Connection endpoint or certificate trust changed. Saved evidence is retained; current coverage awaits a new authenticated read.";
        }
    }

    public static void Accept(HomelabDb db, NasConnection connection, AcceptNasIdentity input, string actor)
    {
        if (ValidateAcceptance(connection, input) is {} error) throw new ArgumentException(error);
        var before = connection.SystemSerial;
        connection.SystemSerial = input.NewSerial; connection.SystemIdentityState = "awaiting validation";
        connection.Enabled = true; connection.ConfigurationRevision = Guid.NewGuid(); connection.NextAttemptUtc = null;
        connection.Status = "System identity baseline changed — awaiting validation";
        db.Activity.Add(new() { Actor = actor, Action = "NAS identity baseline explicitly changed", Detail = $"{connection.Id}: {before} → {input.NewSerial}. Verification: {input.Evidence}" });
    }

    public static string? ValidateAcceptance(NasConnection nas, AcceptNasIdentity input)
    {
        if (input.ConfigurationRevision != nas.ConfigurationRevision)
            return "This connection changed since the identity review was loaded. Reload it and verify the current endpoint, certificate and serials.";
        if (nas.SystemSerial is null || input.PreviousSerial != nas.SystemSerial || UsableSerial(input.NewSerial) is not {} next || next != input.NewSerial || next != nas.ObservedSystemSerial || next == nas.SystemSerial)
            return "Confirm the exact saved and newly reported serials. A missing or placeholder serial cannot be accepted as a replacement identity.";
        if (string.IsNullOrWhiteSpace(input.Evidence) || input.Evidence.Length > 4000) return "Record independent hardware/endpoint verification in up to 4,000 characters.";
        if (string.IsNullOrWhiteSpace(nas.ProtectedApiKey)) return "Re-enroll the API credential before resuming collection.";
        return null;
    }
}
public record AcceptNasIdentity(string PreviousSerial, string NewSerial, string Evidence, Guid? ConfigurationRevision = null);
