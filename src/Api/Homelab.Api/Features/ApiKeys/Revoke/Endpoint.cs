using FastEndpoints;
using Microsoft.EntityFrameworkCore;

namespace Homelab.Api.Features.ApiKeys.Revoke;

public sealed class Request { public Guid Id { get; set; } }

public sealed class Endpoint(HomelabDb db, TimeProvider clock) : Endpoint<Request>
{
    public override void Configure()
    {
        Delete("/api/v1/api-keys/{id:guid}");
        Policies(ApiAccess.Owner);
        Summary(s => s.Summary = "Revoke an API key");
        Description(b => b.WithTags("API keys").Produces(204).Produces(404).Produces(401).Produces(403));
    }

    public override async Task HandleAsync(Request request, CancellationToken ct)
    {
        var key = await db.ApiKeys.SingleOrDefaultAsync(key => key.Id == request.Id, ct);
        if (key is null) { await Send.NotFoundAsync(ct); return; }
        if (key.RevokedUtc is null)
        {
            key.RevokedUtc = clock.GetUtcNow().UtcDateTime;
            db.Activity.Add(new() { Actor = User.Identity!.Name!, Action = "API key revoked", Detail = $"{key.Name} ({key.Id})" });
            await db.SaveChangesAsync(ct);
        }
        await Send.NoContentAsync(ct);
    }
}
