using FastEndpoints;
using Microsoft.EntityFrameworkCore;

namespace Homelab.Api.Features.ApiKeys.List;

public sealed class Endpoint(HomelabDb db) : EndpointWithoutRequest<ApiKeyInfo[]>
{
    public override void Configure()
    {
        Get("/api/v1/api-keys");
        Policies(ApiAccess.Owner);
        Summary(s => s.Summary = "List API key metadata (no secrets)");
        Description(b => b.WithTags("API keys").Produces<ApiKeyInfo[]>(200).Produces(401).Produces(403));
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var keys = await db.ApiKeys.AsNoTracking().OrderByDescending(key => key.CreatedUtc).ToArrayAsync(ct);
        await Send.OkAsync(keys.Select(ApiKeyInfo.From).ToArray(), ct);
    }
}
