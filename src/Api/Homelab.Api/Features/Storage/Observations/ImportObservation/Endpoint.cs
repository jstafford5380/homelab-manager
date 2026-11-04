using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using FastEndpoints;

namespace Homelab.Api.Features.Storage.Observations.ImportObservation;
public sealed class Endpoint(HomelabDb db) : Endpoint<Request, Homelab.Api.Observation>
{
    public override void Configure()
    {
        Post("/api/v1/disks/{id:guid}/observations");
        Policies(ApiAccess.Write);
        Summary(s => s.Summary = "Import Observation");
        Description(b =>
        {
            b.WithName("StorageObservationsImportObservation");
            b.WithTags("Storage.Drives");
            b.Accepts<Request>("application/json");
            b.Produces<Homelab.Api.Observation>(200);
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
        var disk = await db.Disks.FindAsync(id);
        if (disk is null)
        {
            await Send.ResultAsync(Results.NotFound());
            return;
        }

        if (ObservationValidation.Validate(input, DateTime.UtcNow)is { } error)
        {
            await Send.ResultAsync(Results.BadRequest(new { error }));
            return;
        }

        if (await db.Observations.AnyAsync(x => x.ConnectionId == disk.ConnectionId && x.SourceKey == input.SourceKey))
        {
            await Send.ResultAsync(Results.Conflict(new { error = "This evidence has already been imported." }));
            return;
        }

        await ObservationIngestion.StageAsync(db, disk, input, ctx.User.Identity!.Name!);
        await db.SaveChangesAsync();
        await Send.ResultAsync(Results.Ok(input));
        return;
    }
}
