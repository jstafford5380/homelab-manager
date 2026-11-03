
namespace Homelab.Api;
public record RecoveryHistoryPage(DateTime AsOfUtc, string Kind, RecoveryHistoryItem[] Items, RecoveryHistoryCursor? Next);
