using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using FastEndpoints;

namespace Homelab.Api.Features.Storage.Datasets.ReverseRename;
public sealed class Endpoint(HomelabDb db) : Endpoint<Request, Homelab.Api.DatasetRenameReview>
{
    public override void Configure()
    {
        Post("/api/v1/connections/{id:guid}/dataset-renames/{reviewId:long}/reverse");
        AuthSchemes(Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationDefaults.AuthenticationScheme);
        Summary(s => s.Summary = "Reverse Rename");
        Description(b =>
        {
            b.WithName("StorageDatasetsReverseRename");
            b.WithTags("Connections");
            b.Accepts<Request>("application/json");
            b.Produces<Homelab.Api.DatasetRenameReview>(200);
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
        var reviewId = request.ReviewId;
        var input = request.Input;
        var ctx = HttpContext;
        if (string.IsNullOrWhiteSpace(input.Evidence) || input.Evidence.Length > 4000)
        {
            await Send.ResultAsync(Results.BadRequest(new { error = "Record the reversal reason in 1–4000 characters." }));
            return;
        }

        var row = await db.DatasetRenames.SingleOrDefaultAsync(x => x.Id == reviewId && x.ConnectionId == id);
        if (row is null)
        {
            await Send.ResultAsync(Results.NotFound());
            return;
        }

        if (row.ReversedUtc is not null)
        {
            await Send.ResultAsync(row.ReversalEvidence == input.Evidence ? Results.Ok(row) : Results.Conflict(new { error = "This review was already reversed with different evidence." }));
            return;
        }

        row.ReversedUtc = DateTime.UtcNow;
        row.ReversalEvidence = input.Evidence;
        row.ReversedBy = ctx.User.Identity!.Name!;
        db.Activity.Add(new() { Actor = row.ReversedBy, Action = "Dataset rename review reversed", Detail = JsonSerializer.Serialize(new { row.Id, input.Evidence }) });
        await db.SaveChangesAsync();
        await Send.ResultAsync(Results.Ok(row));
        return;
    }
}
