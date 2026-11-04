
namespace Homelab.Api;
public record ChannelInput(string? Url, bool Enabled, bool MaskIdentifiers, DateTime? MaintenanceUntilUtc, string? Kind = null, string? BotToken = null, string? ChatId = null, string? QuietStart = null, string? QuietEnd = null);
