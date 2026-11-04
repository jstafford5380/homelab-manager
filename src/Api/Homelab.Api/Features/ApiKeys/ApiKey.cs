using System.Text.Json.Serialization;

namespace Homelab.Api;

public sealed class ApiKey
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public int OwnerId { get; set; }
    public string Name { get; set; } = "";
    public string Access { get; set; } = ApiAccess.ReadOnly;
    [JsonIgnore] public byte[] SecretHash { get; set; } = [];
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public DateTime ExpiresUtc { get; set; }
    public DateTime? LastUsedUtc { get; set; }
    public DateTime? RevokedUtc { get; set; }
}

public sealed record ApiKeyInfo(Guid Id, string Name, string Access, string Prefix, DateTime CreatedUtc,
    DateTime ExpiresUtc, DateTime? LastUsedUtc, DateTime? RevokedUtc)
{
    public static ApiKeyInfo From(ApiKey key) => new(key.Id, key.Name, key.Access, "hmk_" + key.Id.ToString("N"),
        key.CreatedUtc, key.ExpiresUtc, key.LastUsedUtc, key.RevokedUtc);
}
