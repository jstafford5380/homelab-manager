using System.Text.Json;

namespace Homelab.Api;
public sealed class NasReadException(string status) : Exception("NAS read " + status)
{
    public string Status { get; } = status;

    public static NasReadException FromError(JsonElement error)
    {
        var code = error.ValueKind == JsonValueKind.Object && error.TryGetProperty("error", out var value) && value.TryGetInt32(out var number) ? number : 0;
        if (code == 14 && error.TryGetProperty("reason", out var reason) && reason.ValueKind == JsonValueKind.String && reason.GetString() == "Only ATA device support S.M.A.R.T. attributes")
            return new("unsupported");
        return new(code is 1 or 13 ? "denied" : code == 38 ? "unsupported" : "unavailable");
    }
}
