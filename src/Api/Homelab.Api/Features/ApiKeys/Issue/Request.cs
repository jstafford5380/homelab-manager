namespace Homelab.Api.Features.ApiKeys.Issue;

public sealed class Request
{
    public string Name { get; set; } = "";
    public string Access { get; set; } = ApiAccess.ReadOnly;
    public int ExpiresInDays { get; set; } = 90;
}

public sealed record Response(ApiKeyInfo Key, string Secret);
