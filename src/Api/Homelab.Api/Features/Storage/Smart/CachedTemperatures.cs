using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;

namespace Homelab.Api;
public static partial class CachedTemperatures
{
    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9_.-]{0,127}$", RegexOptions.CultureInvariant)]
    internal static partial Regex DeviceName();
    public static CachedTemperatureSnapshot Normalize(JsonElement data, DateTime fetchedUtc)
    {
        if (data.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("Unexpected temperature cache shape.");
        var readings = new List<CachedTemperature>();
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in data.EnumerateObject())
        {
            if (readings.Count >= 256 || !DeviceName().IsMatch(entry.Name) || !names.Add(entry.Name))
                throw new InvalidDataException("Unexpected temperature cache entries.");
            var value = entry.Value.ValueKind == JsonValueKind.Number && entry.Value.TryGetInt32(out var n) && n >= -273 && n <= 1000 ? (int? )n : null;
            readings.Add(new(entry.Name, value, value is not null ? null : entry.Value.ValueKind == JsonValueKind.Null ? "No temperature in the cache." : "Reading does not match the supported integer Celsius cache schema/range."));
        }

        return new(fetchedUtc, readings.OrderBy(x => x.DeviceName, StringComparer.Ordinal).ToArray());
    }
}
