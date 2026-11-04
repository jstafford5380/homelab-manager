using Microsoft.EntityFrameworkCore;
using FastEndpoints;

namespace Homelab.Api.Features.Connections.RotateCredentials;
public sealed class Endpoint(HomelabDb db, Microsoft.AspNetCore.DataProtection.IDataProtectionProvider protection) : Endpoint<Request, EmptyResponse>
{
    public override void Configure()
    {
        Put("/api/v1/connections/{id:guid}/credentials");
        Policies(ApiAccess.Owner);
        Summary(s => s.Summary = "Rotate Credentials");
        Description(b =>
        {
            b.WithName("ConnectionsRotateCredentials");
            b.WithTags("Connections");
            b.Accepts<Request>("application/json");
            b.Produces(200);
            b.Produces<ApiError>(400);
            b.Produces(404);
            b.Produces<ApiError>(409);
            b.Produces(401);
            b.Produces(403);
        }, clearDefaults: true);
    }

    public override async Task HandleAsync(Request request, CancellationToken ct)
    {
        var id = request.Id;
        var input = request.Input;
        var context = HttpContext;
        if (ConnectionRules.ValidateApiKey(input.ApiKey)is { } error)
        {
            await Send.ResultAsync(Results.BadRequest(new { error }));
            return;
        }

        var connection = await db.Connections.FindAsync(id);
        if (connection is null)
        {
            await Send.ResultAsync(Results.NotFound());
            return;
        }

        if (input.ConfigurationRevision != connection.ConfigurationRevision)
        {
            await Send.ResultAsync(Results.Conflict(new { error = "This connection changed since it was loaded. Reload the saved connection and review it before replacing credentials." }));
            return;
        }

        connection.ProtectedApiKey = Microsoft.AspNetCore.DataProtection.DataProtectionCommonExtensions.Protect(protection.CreateProtector("nas-credentials"), input.ApiKey);
        var history = await db.ConnectionCredentialHistory.FindAsync([id], ct);
        if (history is null)
            db.ConnectionCredentialHistory.Add(new() { ConnectionId = id, ReplacedUtc = DateTime.UtcNow });
        else
            history.ReplacedUtc = DateTime.UtcNow;
        connection.ConfigurationRevision = Guid.NewGuid();
        connection.NextAttemptUtc = null;
        connection.Enabled = true;
        connection.Status = "Credentials updated — awaiting collection";
        db.Activity.Add(new() { Actor = context.User.Identity!.Name!, Action = "NAS credential rotated", Detail = connection.Name });
        await db.SaveChangesAsync();
        await Send.ResultAsync(Results.Ok());
        return;
    }
}
