using System.Text.Json;
using Homelab.Api;
namespace Homelab.Tests;
public class TelegramNotificationTests
{
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
    [Fact] public void MaximumFieldsPreserveQualificationsAndExplainCoverageTruncation()
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
        Assert.Contains("First seen (UTC): " + at, text);
        Assert.Contains("In-app evidence path: /?finding=fixture", text);
        Assert.Contains("last successful fetch: " + at, text);
        Assert.Contains("last successful fetch: unknown", text);
        Assert.Contains("More coverage details in the app.", text);
        Assert.EndsWith("Acceptance does not prove this message was read.", text);
        Assert.Contains("Fetch time is not necessarily observation time.", text);
        Assert.DoesNotContain("\uFFFD", text);
    }
    [Fact] public void LongPlainTextIsBoundedAndCannotEnableMarkupOrPreviews()
    {
        var payload = TelegramNotifications.Payload(new("123456:abcdefghijklmnopqrstuvwxyz", "123"), JsonSerializer.Serialize(new { title = "<b>private</b>", evidence = new string('x', 10000), nextStep = "Review", FirstSeenUtc = DateTime.UtcNow, LastSeenUtc = DateTime.UtcNow }));
        using var json = JsonDocument.Parse(payload);
        var text = json.RootElement.GetProperty("text").GetString()!;
        Assert.True(text.Length <= 4096); Assert.Contains("<b>private</b>", text); Assert.Contains("Next step: Review", text); Assert.Contains("not a predicted failure date", text);
        Assert.False(json.RootElement.TryGetProperty("parse_mode", out _)); Assert.True(json.RootElement.GetProperty("link_preview_options").GetProperty("is_disabled").GetBoolean());
    }
}
