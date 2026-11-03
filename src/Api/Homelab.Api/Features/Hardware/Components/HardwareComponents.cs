using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using static Homelab.Api.HardwareInventory;

namespace Homelab.Api;
public static class HardwareComponents
{
    public static string? Validate(HardwareComponentInput? input, DateTime now)
    {
        if (input is null)
            return "Enter successor component details.";
        if (!HardwareMetrics.Kinds.Contains(input.Kind) || string.IsNullOrWhiteSpace(input.Name) || input.Name.Length > 200 || input.Model is null || input.Model.Length > 500 || input.Spec is null || input.Spec.Length > 2000 || input.Notes is null || input.Notes.Length > 10000)
            return "Choose a kind and name, with bounded details.";
        if (input.PricePaid is < 0 or > 1000000000 || input.PurchasedUtc > now || input.InstalledUtc > now || new[]
        {
            input.PurchasedUtc,
            input.InstalledUtc,
            input.WarrantyEndUtc
        }.Any(d => d is { } date && (date.Kind != DateTimeKind.Utc || date < DateTime.UnixEpoch)))
            return "Check the price and dates; purchase and installation must be in the past.";
        if (input.Slots?.Length > 64 || input.Slots?.Any(s => s is null || string.IsNullOrWhiteSpace(s.Name) || s.Name.Length > 100 || s.SizeBytes is { } b && (!double.IsFinite(b) || b <= 0 || b > 1e16)) == true || new[]
        {
            input.SolderedBytes,
            input.MaxSupportedBytes
        }.Any(v => v is { } n && (!double.IsFinite(n) || n < 0 || n > 1e16)))
            return "Check the memory slot sizes and maximum.";
        if (input.Kind != "memory" && (input.Slots?.Length > 0 || input.SolderedBytes is not null || input.MaxSupportedBytes is not null))
            return "Memory layout belongs to a memory component.";
        if (input.Kind != "ups" && (input.UpsDeviceKey is not null || input.UpsPowerFactor is not null || input.UpsNominalVa is not null) ||
            input.UpsDeviceKey is { } key && (key.Length != 64 || !key.All(Uri.IsHexDigit)) ||
            input.UpsPowerFactor is { } factor && (!double.IsFinite(factor) || factor <= 0 || factor > 1) ||
            input.UpsNominalVa is { } va && (!double.IsFinite(va) || va <= 0 || va > 1_000_000))
            return "Choose a UPS and enter a power factor greater than zero and at most 1, and a valid VA rating.";
        return null;
    }

    public static void Apply(HardwareComponent c, HardwareComponentInput input)
    {
        c.UpsDeviceKey = input.UpsDeviceKey;
        c.UpsPowerFactor = input.UpsPowerFactor;
        c.UpsNominalVa = input.UpsNominalVa;
        c.Name = input.Name.Trim();
        c.Model = input.Model.Trim();
        c.Spec = input.Spec.Trim();
        c.Notes = input.Notes;
        c.PricePaid = input.PricePaid;
        c.PurchasedUtc = input.PurchasedUtc;
        c.InstalledUtc = input.InstalledUtc;
        c.WarrantyEndUtc = input.WarrantyEndUtc;
        c.DatesEstimated = input.DatesEstimated;
        c.SlotsJson = JsonSerializer.Serialize(input.Slots ?? []);
        c.SolderedBytes = input.SolderedBytes;
        c.MaxSupportedBytes = input.MaxSupportedBytes;
        c.Revision = Guid.NewGuid();
    }

    internal static async Task<IResult> SaveComponent(Guid id, Guid? componentId, HardwareComponentInput input, HomelabDb db, IConfiguration config)
    {
        var server = await Server(id, db, config);
        if (server is null)
            return Results.NotFound();
        if (Validate(input, DateTime.UtcNow)is { } error)
            return Results.BadRequest(new { error });
        var c = componentId is { } cid ? await db.HardwareComponents.SingleOrDefaultAsync(c => c.Id == cid && c.ServerId == id && c.RetiredUtc == null) : new HardwareComponent
        {
            ServerId = id,
            Kind = input.Kind
        };
        if (c is null)
            return Results.NotFound();
        if (componentId is not null && c.Revision != input.Revision)
            return Results.Conflict(new { error = "This component changed. Reload before saving." });
        if (c.DetectedKey is not null && c.Kind != input.Kind)
            return Results.BadRequest(new { error = "Detected kinds cannot be changed." });
        if (input.UpsDeviceKey is { } key && key != c.UpsDeviceKey)
        {
            var connection = await db.Connections.AsNoTracking().SingleOrDefaultAsync(connection => connection.Id == server.ConnectionId);
            var snapshot = StoragePage.Read<UpsSnapshot>(server.UpsSnapshotJson);
            var detected = snapshot?.Devices?.SingleOrDefault(device => device.Key == key);
            if (connection is not { Enabled: true } || connection.ConfigurationRevision != server.ConfigurationRevision ||
                !MetricsEnabled(config) || server.LastSuccessUtc < DateTime.UtcNow.AddMinutes(-15) ||
                detected is null)
                return Results.Conflict(new { error = "The detected UPS is no longer available. Refresh Hardware before linking it." });
            if (!detected.HasSerial && !input.ConfirmUpsWithoutSerial)
                return Results.BadRequest(new { error = "Confirm the device without a serial before linking it." });
        }
        if (input.UpsDeviceKey is { } linkedKey && await db.HardwareComponents.AnyAsync(other =>
            other.ServerId == id && other.Id != c.Id && other.RetiredUtc == null && other.UpsDeviceKey == linkedKey))
            return Results.Conflict(new { error = "This UPS is already linked to another component." });
        c.Kind = input.Kind;
        Apply(c, input);
        if (componentId is null)
            db.HardwareComponents.Add(c);
        db.HardwareEvents.Add(new() { ComponentId = c.Id, Kind = componentId is null ? "entered" : "edited", Text = componentId is null ? "Component entered by you." : "Entered details updated by you." });
        await db.SaveChangesAsync();
        await HardwareCollection.RecordStatuses(db, id, DateTime.UtcNow, MetricsEnabled(config) || server.Platform == "synology", default);
        await db.SaveChangesAsync();
        return Results.Ok(new { id = c.Id });
    }
}
