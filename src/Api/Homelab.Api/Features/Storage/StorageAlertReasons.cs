using System.Net;
using System.Text.RegularExpressions;

namespace Homelab.Api;

public static class StorageAlertReasons
{
    private static bool Names(string message, string identity) => identity.Length >= 3 &&
        Regex.IsMatch(message, "(?<![A-Za-z0-9_.-])" + Regex.Escape(identity) + "(?![A-Za-z0-9_.-])", RegexOptions.CultureInvariant);

    public static string? ForDrive(Disk disk, IReadOnlyList<Disk> disks, IEnumerable<NativeAlert> alerts, bool inventoryCurrent, bool alertsCurrent = true)
    {
        var serialUnique = disks.Count(d => d.Serial == disk.Serial) == 1 && disk.MappingConfidence != "ambiguous";
        var aliasCurrent = inventoryCurrent && disk.Present && disk.MappingConfidence != "ambiguous" &&
            disks.Count(d => d.Present && d.Alias == disk.Alias) == 1;
        var matched = alerts.Where(a => a.ConnectionId == disk.ConnectionId && !a.DismissedOnNas && a.AbsentSinceUtc is null)
            .Select(a => new { a.Class, Message = Regex.Replace(WebUtility.HtmlDecode(Regex.Replace(a.Message, "<[^>]*>", " ")), @"\s+", " ").Trim() })
            .Where(a => serialUnique && Names(a.Message, disk.Serial) || aliasCurrent && Names(a.Message, disk.Alias))
            .OrderByDescending(a => a.Class == "SMART").ThenBy(a => a.Message, StringComparer.Ordinal).ToArray();
        if (matched.Length == 0) return null;
        var explanation = (alertsCurrent ? "TrueNAS reports: " : "Last known TrueNAS alerts: ") + string.Join(" ", matched.Select(a => a.Message).Distinct(StringComparer.Ordinal));
        if (matched.Any(a => a.Class == "SMART"))
            explanation += " Reallocated-sector counts and the overall SMART result are shown only when separately reported.";
        return explanation;
    }
}
