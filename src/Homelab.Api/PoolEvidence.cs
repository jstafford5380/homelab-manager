using System.Text.Json;
namespace Homelab.Api;

public static class PoolEvidence
{
    public static bool ContainsPool(string? json, string guid)
    {
        if (string.IsNullOrWhiteSpace(json) || json.Length > 4 * 1024 * 1024) return false;
        try {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 32 });
            return document.RootElement.ValueKind == JsonValueKind.Array && document.RootElement.EnumerateArray().Any(x =>
                x.ValueKind == JsonValueKind.Object && x.TryGetProperty("guid", out var value) && value.ValueKind == JsonValueKind.String && value.GetString() == guid);
        } catch (JsonException) { return false; }
    }
}
