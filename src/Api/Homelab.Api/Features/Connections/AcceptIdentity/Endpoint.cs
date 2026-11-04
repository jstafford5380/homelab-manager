using Microsoft.EntityFrameworkCore;
using FastEndpoints;

namespace Homelab.Api.Features.Connections.AcceptIdentity;
public sealed class Endpoint(HomelabDb db) : Endpoint<Request, EmptyResponse>
{
    public override void Configure()
    {
        Put("/api/v1/connections/{id:guid}/identity");
        Policies(ApiAccess.Owner);
        Summary(s => s.Summary = "Accept Identity");
        Description(b =>
        {
            b.WithName("ConnectionsAcceptIdentity");
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
        var ctx = HttpContext;
        var connection = await db.Connections.FindAsync(id);
        if (connection is null)
        {
            await Send.ResultAsync(Results.NotFound());
            return;
        }

        if (input.ConfigurationRevision != connection.ConfigurationRevision)
        {
            await Send.ResultAsync(Results.Conflict(new { error = "This connection changed since the identity review was loaded. Reload it and verify the current endpoint, certificate and serials." }));
            return;
        }

        if (NasIdentity.ValidateAcceptance(connection, input)is { } error)
        {
            await Send.ResultAsync(Results.BadRequest(new { error }));
            return;
        }

        NasIdentity.Accept(db, connection, input, ctx.User.Identity!.Name!);
        await db.SaveChangesAsync();
        await Send.ResultAsync(Results.Ok());
        return;
    }
}
