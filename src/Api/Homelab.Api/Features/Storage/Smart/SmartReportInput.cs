
namespace Homelab.Api;
public record SmartReportInput(string Json, string ContinuityGroup = "", bool ConfirmAtaCounterUnits = false, string? ExpectedHash = null, string? ConfirmedSerial = null);
