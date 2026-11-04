using System.Globalization;

namespace Homelab.Api;
public static class NotificationQuietPeriod
{
    private static bool Parse(string? value, out TimeOnly time) => TimeOnly.TryParseExact(value, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out time);
    public static bool Valid(string? start, string? end) => start is null && end is null || Parse(start, out var from) && Parse(end, out var to) && from != to;
    public static bool Active(NotificationChannel channel, DateTime utc, string timezone)
    {
        if (!Parse(channel.QuietStart, out var start) || !Parse(channel.QuietEnd, out var end) || start == end) return false;
        var local = TimeOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(utc, TimeZoneInfo.FindSystemTimeZoneById(timezone)));
        return start < end ? local >= start && local < end : local >= start || local < end;
    }
}
