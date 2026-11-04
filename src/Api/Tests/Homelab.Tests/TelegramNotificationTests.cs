using System.Text.Json;
using Homelab.Api;
namespace Homelab.Tests;
public class TelegramNotificationTests
{
    [Fact] public void TestMessagePreviewsTheRealAlertLayoutWithClearlyFictionalData()
    {
        var payload = TelegramNotifications.Payload(new("123456:abcdefghijklmnopqrstuvwxyz", "123"), "{\"type\":\"homelab-test\"}");
        using var document = JsonDocument.Parse(payload);
        var text = document.RootElement.GetProperty("text").GetString()!;
        Assert.StartsWith("🧪 Notification test", text);
        Assert.Contains("🔴 Critical · action needed", text);
        Assert.Contains("Example: pool Main is degraded", text);
        Assert.Contains("Example NAS", text);
        Assert.Contains("Next step: Check the pool in TrueNAS", text);
        Assert.Contains("Example only", text);
        Assert.Contains("does not report an actual NAS issue", text);
        Assert.DoesNotContain("/?finding=", text);
    }
    [Theory]
    [InlineData("123456:abcdefghijklmnopqrstuvwxyz", "123456", true)]
    [InlineData("123456:abcdefghijklmnopqrstuvwxyz", "-100123456789", true)]
    [InlineData("123456:abcdefghijklmnopqrstuvwxyz", "@valid_channel", true)]
    [InlineData("123456:abcdefghijklmnopqrstuvwxyz/../deleteWebhook", "123", false)]
    [InlineData("123456:abcdefghijklmnopqrstuvwxyz?redirect=x", "123", false)]
    [InlineData("token", "123", false)]
    [InlineData("123456:abcdefghijklmnopqrstuvwxyz", "", false)]
    [InlineData("123456:abcdefghijklmnopqrstuvwxyz", "123\n", false)]
    public void DestinationValidation(string token, string chat, bool valid) => Assert.Equal(valid, TelegramNotifications.IsValid(new(token, chat)));
    [Fact] public void OnlyFixedTelegramSendMessageEndpointIsRecognized()
    {
        var target = TelegramNotifications.Endpoint(new("123456:abcdefghijklmnopqrstuvwxyz", "123"));
        Assert.True(TelegramNotifications.IsEndpoint(target));
        foreach (var url in new[] { "http://api.telegram.org/bot123456:abcdefghijklmnopqrstuvwxyz/sendMessage", "https://evil.example/bot123456:abcdefghijklmnopqrstuvwxyz/sendMessage", "https://api.telegram.org/bot123456:abcdefghijklmnopqrstuvwxyz/getUpdates", "https://api.telegram.org:444/bot123456:abcdefghijklmnopqrstuvwxyz/sendMessage", target + "?extra=1" })
            Assert.False(TelegramNotifications.IsEndpoint(new Uri(url)));
    }
    [Theory] [InlineData("{\"ok\":true}", true)] [InlineData("{\"ok\":false}", false)] [InlineData("{}", false)] [InlineData("not json", false)]
    public void TelegramMustExplicitlyAccept(string json, bool accepted) => Assert.Equal(accepted, TelegramNotifications.Accepted(json));
    [Fact] public void MessagesSummarizeInsteadOfDumpingEvidenceAndCoverage()
    {
        var longValue = string.Concat(Enumerable.Repeat("😀", 5000));
        var at = "2026-11-01T12:00:00Z";
        var payload = TelegramNotifications.Payload(new("123456:abcdefghijklmnopqrstuvwxyz", "123"), JsonSerializer.Serialize(new {
            severity = "critical", state = "open", title = longValue, nas = longValue, serial = longValue, bay = longValue,
            FirstSeenUtc = at, LastSeenUtc = at, evidence = longValue, nextStep = longValue, evidencePath = "/?finding=fixture",
            coverage = Enumerable.Range(0, 12).Select(i => new { Name = "source-" + i, status = "stale", LastSuccessUtc = i == 0 ? at : null })
        }));
        using var document = JsonDocument.Parse(payload);
        var text = document.RootElement.GetProperty("text").GetString()!;
        Assert.True(text.Length <= 4000);
        Assert.Contains("🔴 Critical · action needed", text);
        Assert.Contains("First observed Nov 1, 2026 at 12:00 UTC", text);
        Assert.Contains("/?finding=fixture", text);
        Assert.Contains("12 sources. Check coverage in the app.", text);
        Assert.DoesNotContain("last successful fetch", text);
        Assert.DoesNotContain("Coverage:", text);
        Assert.DoesNotContain("Acceptance", text);
        Assert.True(text.Length < 1800);
        Assert.DoesNotContain("\uFFFD", text);
    }
    [Fact] public void LongPlainTextIsBoundedAndCannotEnableMarkupOrPreviews()
    {
        var payload = TelegramNotifications.Payload(new("123456:abcdefghijklmnopqrstuvwxyz", "123"), JsonSerializer.Serialize(new { title = "<b>private</b>", evidence = new string('x', 10000), nextStep = "Review", FirstSeenUtc = DateTime.UtcNow, LastSeenUtc = DateTime.UtcNow }));
        using var json = JsonDocument.Parse(payload);
        var text = json.RootElement.GetProperty("text").GetString()!;
        Assert.True(text.Length <= 4096); Assert.Contains("<b>private</b>", text); Assert.Contains("Next step: Review", text); Assert.DoesNotContain(new string('x', 100), text);
        Assert.False(json.RootElement.TryGetProperty("parse_mode", out _)); Assert.True(json.RootElement.GetProperty("link_preview_options").GetProperty("is_disabled").GetBoolean());
    }
    [Fact] public void ResolvedMessagesDoNotRepeatOldActionsAndRespectMaskedIdentifiers()
    {
        var payload = TelegramNotifications.Payload(new("123456:abcdefghijklmnopqrstuvwxyz", "123"), JsonSerializer.Serialize(new {
            severity = "critical", state = "resolved", title = "NAS observation requires review", nas = (string?)null,
            serial = (string?)null, bay = "unverified or masked", nextStep = "Replace the drive", evidence = "Private evidence",
            coverage = new[] { new { status = "supported", Name = "inventory" }, new { status = "disabled", Name = "optional" }, new { status = "unsupported", Name = "optional-hardware" } }
        }));
        using var json = JsonDocument.Parse(payload);
        var text = json.RootElement.GetProperty("text").GetString()!;
        Assert.StartsWith("✅ Resolved", text);
        Assert.Contains("marked resolved", text);
        Assert.DoesNotContain("Replace the drive", text);
        Assert.DoesNotContain("Private evidence", text);
        Assert.DoesNotContain("unverified or masked", text);
        Assert.DoesNotContain("Monitoring data", text);
    }
}
