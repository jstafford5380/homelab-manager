using Microsoft.EntityFrameworkCore;
using FastEndpoints;

namespace Homelab.Api.Features.Storage.Drives.VoidReplacement;
public sealed class Endpoint(HomelabDb db) : Endpoint<Request, Homelab.Api.DiskReplacement>
{
    public override void Configure()
    {
        Post("/api/v1/replacements/{id:guid}/void");
        Policies(ApiAccess.Write);
        Summary(s => s.Summary = "Void Replacement");
        Description(b =>
        {
            b.WithName("StorageDrivesVoidReplacement");
            b.WithTags("Replacements");
            b.Accepts<Request>("application/json");
            b.Produces<Homelab.Api.DiskReplacement>(200);
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
        if (string.IsNullOrWhiteSpace(input.Reason) || input.Reason.Length > 2000)
        {
            await Send.ResultAsync(Results.BadRequest(new { error = "Explain the correction in up to 2,000 characters." }));
            return;
        }

        var record = await db.Replacements.FindAsync(id);
        if (record is null)
        {
            await Send.ResultAsync(Results.NotFound());
            return;
        }

        if (record.VoidedUtc != null)
        {
            await Send.ResultAsync(Results.Conflict(new { error = "This record is already marked incorrect." }));
            return;
        }

        record.VoidedUtc = DateTime.UtcNow;
        record.VoidReason = input.Reason;
        record.VoidedBy = context.User.Identity!.Name!;
        db.Activity.Add(new() { Actor = record.VoidedBy, Action = "Replacement history marked incorrect", Detail = record.Id.ToString() });
        await db.SaveChangesAsync();
        await Send.ResultAsync(Results.Ok(record));
        return;
    }
}
