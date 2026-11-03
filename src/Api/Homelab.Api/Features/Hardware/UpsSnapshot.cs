using System.Text.Json;

namespace Homelab.Api;

public record UpsReading(double Value, DateTime ObservedUtc);

public record UpsSnapshot(string Status, UpsReading? Load = null, UpsReading? Charge = null, UpsReading? Runtime = null, double? NominalVa = null, UpsDevice[]? Devices = null)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public string Serialize() => JsonSerializer.Serialize(this, Json);

    public static UpsSnapshot Read(JsonElement response, DateTime now)
    {
        if (response.ValueKind != JsonValueKind.Array || response.GetArrayLength() > 3)
            return new("unavailable");

        var load = Latest(response, "upsload", now, null);
        var charge = Latest(response, "upscharge", now, 100);
        var runtime = Latest(response, "upsruntime", now, null);
        return new(load is null && charge is null && runtime is null ? "no-readings" : "reporting", load, charge, runtime);
    }

    private static UpsReading? Latest(JsonElement response, string name, DateTime now, double? max)
    {
        var graphs = response.EnumerateArray()
            .Where(graph => graph.ValueKind == JsonValueKind.Object && HardwareCollection.Text(graph, "name") == name)
            .ToArray();
        if (graphs.Length != 1 || !graphs[0].TryGetProperty("legend", out var legend) ||
            legend.ValueKind != JsonValueKind.Array || legend.GetArrayLength() != 2 ||
            legend[0].ValueKind != JsonValueKind.String || legend[0].GetString() != "time" ||
            !graphs[0].TryGetProperty("data", out var points) || points.ValueKind != JsonValueKind.Array ||
            points.GetArrayLength() > 3601)
            return null;

        var end = new DateTimeOffset(now).ToUnixTimeSeconds();
        long? latest = null;
        double? value = null;
        foreach (var row in points.EnumerateArray())
        {
            if (row.ValueKind != JsonValueKind.Array || row.GetArrayLength() != 2 ||
                row[0].ValueKind != JsonValueKind.Number || !row[0].TryGetInt64(out var seconds) ||
                seconds < end - 3600 || seconds > end || seconds < latest)
                continue;

            // TrueNAS asks Netdata to replace nulls with zero. A zero cannot establish a reading.
            var candidate = row[1].ValueKind == JsonValueKind.Number && row[1].TryGetDouble(out var number) &&
                double.IsFinite(number) && number > 0 && (max is null || number <= max) ? number : (double?)null;
            value = seconds == latest ? value == candidate ? value : null : candidate;
            latest = seconds;
        }
        return latest is { } at && value is { } reading ? new(reading, DateTimeOffset.FromUnixTimeSeconds(at).UtcDateTime) : null;
    }
}
