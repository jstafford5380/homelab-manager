using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
namespace Homelab.Api;

public record TelegramDestination(string BotToken, string ChatId);
public static partial class TelegramNotifications
{
    [GeneratedRegex(@"\A[0-9]{1,20}:[A-Za-z0-9_-]{20,200}\z", RegexOptions.CultureInvariant)]
    private static partial Regex TokenPattern();
    [GeneratedRegex(@"\A(?:-?[1-9][0-9]{0,19}|@[A-Za-z][A-Za-z0-9_]{4,31})\z", RegexOptions.CultureInvariant)]
    private static partial Regex ChatPattern();
    public static bool IsValid(TelegramDestination value) => value.BotToken is not null && value.ChatId is not null && TokenPattern().IsMatch(value.BotToken) && ChatPattern().IsMatch(value.ChatId);
    public static Uri Endpoint(TelegramDestination value)
    {
        if (!IsValid(value)) throw new InvalidDataException("Check the Telegram bot token and chat ID.");
        return new Uri("https://api.telegram.org/bot" + value.BotToken + "/sendMessage");
    }
    public static bool IsEndpoint(Uri uri)
    {
        const string prefix = "/bot", suffix = "/sendMessage";
        var path = uri.AbsolutePath;
        return uri.Scheme == "https" && uri.IdnHost == "api.telegram.org" && uri.Port == 443 &&
            string.IsNullOrEmpty(uri.UserInfo) && string.IsNullOrEmpty(uri.Query) && string.IsNullOrEmpty(uri.Fragment) &&
            path.StartsWith(prefix, StringComparison.Ordinal) && path.EndsWith(suffix, StringComparison.Ordinal) &&
            path.Length > prefix.Length + suffix.Length && TokenPattern().IsMatch(path[prefix.Length..^suffix.Length]);
    }
    public static string Payload(TelegramDestination destination, string findingJson)
    {
        using var document = JsonDocument.Parse(findingJson);
        var root = document.RootElement;
        var text = new StringBuilder("Homelab Control\n");
        if (root.TryGetProperty("type", out var type) && type.GetString() == "homelab-test")
            text.Append("Notification test. This test does not change automatic alert settings.");
        else
        {
            foreach (var (key, label, limit) in new[] {
                ("severity", "Severity", 40), ("state", "State", 40), ("title", "Finding", 250),
                ("nas", "NAS", 150), ("serial", "Drive serial", 150), ("bay", "Bay", 150),
                ("FirstSeenUtc", "First seen (UTC)", 50), ("LastSeenUtc", "Last seen (UTC)", 50),
                ("evidence", "Evidence", 1000), ("nextStep", "Next step", 500), ("evidencePath", "In-app evidence path", 150) })
            {
                if (!root.TryGetProperty(key, out var field) || field.ValueKind != JsonValueKind.String) continue;
                text.Append(label).Append(": ").Append(Clip(field.GetString()!, limit)).Append('\n');
            }
            var coverageText = new StringBuilder();
            if (root.TryGetProperty("coverage", out var coverage) && coverage.ValueKind == JsonValueKind.Array)
                foreach (var item in coverage.EnumerateArray().Take(12))
                    if (item.TryGetProperty("Name", out var name) && item.TryGetProperty("status", out var status)) {
                        coverageText.Append("Coverage: ").Append(Clip(name.GetString() ?? "unknown", 50)).Append(" — ").Append(Clip(status.GetString() ?? "unknown", 40));
                        var lastSuccess = item.TryGetProperty("LastSuccessUtc", out var last) && last.ValueKind == JsonValueKind.String ? last.GetString()! : "unknown";
                        coverageText.Append("; last successful fetch: ").Append(Clip(lastSuccess, 50)).Append('\n');
                    }
            var coverageSummary = coverageText.ToString();
            if (coverageSummary.Length > 600) coverageSummary = Clip(coverageSummary, 550) + "\nMore coverage details in the app.\n";
            text.Append(coverageSummary);
            // Reserve space for the qualification even when every source field is at its bound.
            var summary = Clip(text.ToString(), 3800);
            text.Clear().Append(summary).Append("\nObserved condition, not a predicted failure date. Fetch time is not necessarily observation time. Acceptance does not prove this message was read.");

        }
        return JsonSerializer.Serialize(new { chat_id = destination.ChatId, text = Clip(text.ToString(), 4000), link_preview_options = new { is_disabled = true } });
    }
    private static string Clip(string value, int limit)
    {
        if (value.Length <= limit) return value;
        var length = limit - 1;
        if (char.IsHighSurrogate(value[length - 1])) length--;
        return value[..length] + "…";
    }
    public static bool Accepted(string json)
    {
        try { using var document = JsonDocument.Parse(json); return document.RootElement.ValueKind == JsonValueKind.Object && document.RootElement.TryGetProperty("ok", out var ok) && ok.ValueKind == JsonValueKind.True; }
        catch (JsonException) { return false; }
    }
}
