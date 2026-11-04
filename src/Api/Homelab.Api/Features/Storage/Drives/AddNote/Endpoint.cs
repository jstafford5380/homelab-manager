using Microsoft.EntityFrameworkCore;
using FastEndpoints;

namespace Homelab.Api.Features.Storage.Drives.AddNote;
public sealed class Endpoint(HomelabDb db) : Endpoint<Request, EmptyResponse>
{
    public override void Configure()
    {
        Post("/api/v1/storage/drives/{id:guid}/notes");
        Policies(ApiAccess.Write);
        Summary(s => s.Summary = "Add Note");
        Description(b =>
        {
            b.WithName("StorageDrivesAddNote");
            b.WithTags("Storage");
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
        var disk = await db.Disks.FindAsync(id);
        if (disk is null)
        {
            await Send.ResultAsync(Results.NotFound());
            return;
        }

        if (input.RequestId == Guid.Empty || string.IsNullOrWhiteSpace(input.Text) || input.Text.Length > 2000)
        {
            await Send.ResultAsync(Results.BadRequest(new { error = "Enter a note of up to 2,000 characters." }));
            return;
        }

        var existing = await db.Maintenance.SingleOrDefaultAsync(x => x.DiskId == id && x.RequestId == input.RequestId);
        if (existing is not null)
        {
            await Send.ResultAsync(existing.Summary == input.Text ? Results.Ok() : Results.Conflict());
            return;
        }

        db.Maintenance.Add(new() { DiskId = id, RequestId = input.RequestId, Summary = input.Text, OccurredUtc = DateTime.UtcNow, RecordedUtc = DateTime.UtcNow, DateConfidence = "documented", Kind = "other", Outcome = "unknown", Evidence = "Owner note; no hardware action asserted.", ConfirmedIdentity = Homelab.Api.Replacements.Identity(disk), Author = context.User.Identity!.Name! });
        db.Activity.Add(new() { Actor = context.User.Identity!.Name!, Action = "Drive note added", Detail = id.ToString() });
        await db.SaveChangesAsync();
        await Send.ResultAsync(Results.Ok());
        return;
    }
}
