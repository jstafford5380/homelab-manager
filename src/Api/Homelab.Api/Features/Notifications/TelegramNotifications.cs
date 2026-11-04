using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Homelab.Api;
public static partial class TelegramNotifications
{
    [GeneratedRegex(@"\A[0-9]{1,20}:[A-Za-z0-9_-]{20,200}\z", RegexOptions.CultureInvariant)]
    private static partial Regex TokenPattern();
    [GeneratedRegex(@"\A(?:-?[1-9][0-9]{0,19}|@[A-Za-z][A-Za-z0-9_]{4,31})\z", RegexOptions.CultureInvariant)]
    private static partial Regex ChatPattern();
    public static bool IsValid(TelegramDestination value) => value.BotToken is not null && value.ChatId is not null && TokenPattern().IsMatch(value.BotToken) && ChatPattern().IsMatch(value.ChatId);
    public static Uri Endpoint(TelegramDestination value)
    {
        if (!IsValid(value))
            throw new InvalidDataException("Check the Telegram bot token and chat ID.");
        return new Uri("https://api.telegram.org/bot" + value.BotToken + "/sendMessage");
    }

    public static bool IsEndpoint(Uri uri)
    {
        const string prefix = "/bot", suffix = "/sendMessage";
        var path = uri.AbsolutePath;
        return uri.Scheme == "https" && uri.IdnHost == "api.telegram.org" && uri.Port == 443 && string.IsNullOrEmpty(uri.UserInfo) && string.IsNullOrEmpty(uri.Query) && string.IsNullOrEmpty(uri.Fragment) && path.StartsWith(prefix, StringComparison.Ordinal) && path.EndsWith(suffix, StringComparison.Ordinal) && path.Length > prefix.Length + suffix.Length && TokenPattern().IsMatch(path[prefix.Length..^suffix.Length]);
    }

    public static string Payload(TelegramDestination destination, string findingJson)
    {
        using var document = JsonDocument.Parse(findingJson);
        var root = document.RootElement;
        var text = new StringBuilder("Homelab Control\n");
        if (root.TryGetProperty("type", out var type) && type.GetString() == "homelab-test")
        {
            // Render the example through the real formatter so tests preview the
            // same layout as automatic notifications, without exposing NAS data.
            var preview = Payload(destination, JsonSerializer.Serialize(new
            {
                severity = "critical", state = "open", title = "Example: pool Main is degraded",
                nas = "Example NAS", nextStep = "Check the pool in TrueNAS and confirm your backups are current."
            }));
            using var sample = JsonDocument.Parse(preview);
            text.Clear().Append("🧪 Notification test\n\n")
                .Append(sample.RootElement.GetProperty("text").GetString())
                .Append("\n\nExample only — this does not report an actual NAS issue or change automatic alert settings.");
        }
        else
        {
            string Field(string key, int limit = 250) => root.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String
                ? Clip(Regex.Replace(value.GetString() ?? "", @"\s+", " ").Trim(), limit) : "";
            var resolved = Field("state") == "resolved";
            var heading = resolved ? "✅ Resolved" : Field("severity") switch
            {
                "critical" => "🔴 Critical · action needed",
                "urgent" => "🟠 Needs attention",
                _ => "🟡 Review needed"
            };
            text.Clear().Append(heading).Append(" · Homelab Control\n\n");
            text.Append(Field("title") is { Length: > 0 } title ? title : "NAS finding requires review").Append('\n');
            var nas = Field("nas", 120);
            var serial = Field("serial", 120);
            var bay = Field("bay", 80);
            if (nas.Length > 0) text.Append(nas).Append('\n');
            if (serial.Length > 0) text.Append("Drive ").Append(serial).Append('\n');
            if (bay.Length > 0 && bay != "unverified or masked") text.Append("Bay ").Append(bay).Append('\n');
            var next = resolved ? "This finding is marked resolved. Review its history in the app." : Field("nextStep", 400);
            if (next.Length == 0) next = "Review this finding and its evidence in Homelab Control.";
            text.Append("\n").Append(resolved ? "" : "Next step: ").Append(next).Append('\n');
            if (root.TryGetProperty("coverage", out var coverage) && coverage.ValueKind == JsonValueKind.Array)
            {
                var incomplete = coverage.EnumerateArray().Count(item => !item.TryGetProperty("status", out var status) || status.GetString() is not ("supported" or "disabled" or "unsupported"));
                if (incomplete > 0) text.Append("\nMonitoring data is incomplete or unavailable for ").Append(incomplete).Append(incomplete == 1 ? " source. Check coverage in the app.\n" : " sources. Check coverage in the app.\n");
            }
            var timestampKey = resolved ? "LastSeenUtc" : "FirstSeenUtc";
            if (DateTimeOffset.TryParse(Field(timestampKey), System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeUniversal, out var at))
                text.Append('\n').Append(resolved ? "Last observed " : "First observed ").Append(at.UtcDateTime.ToString("MMM d, yyyy 'at' HH:mm 'UTC'", System.Globalization.CultureInfo.InvariantCulture)).Append('\n');
            var path = Field("evidencePath", 150);
            text.Append("\nDetails and evidence in Homelab Control");
            if (path.Length > 0) text.Append('\n').Append(path);
        }

        return JsonSerializer.Serialize(new { chat_id = destination.ChatId, text = Clip(text.ToString(), 4000), link_preview_options = new { is_disabled = true } });
    }

    private static string Clip(string value, int limit)
    {
        if (value.Length <= limit)
            return value;
        var length = limit - 1;
        if (char.IsHighSurrogate(value[length - 1]))
            length--;
        return value[..length] + "…";
    }

    public static bool Accepted(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.ValueKind == JsonValueKind.Object && document.RootElement.TryGetProperty("ok", out var ok) && ok.ValueKind == JsonValueKind.True;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
