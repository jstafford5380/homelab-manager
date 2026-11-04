using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using static Homelab.Api.DatasetRenames;
using FastEndpoints;

namespace Homelab.Api.Features.Storage.Datasets.PostConnectionsDatasetRenamesById;
public sealed class Endpoint(HomelabDb db) : Endpoint<Request, Homelab.Api.DatasetRenameReview>
{
    public override void Configure()
    {
        Post("/api/v1/connections/{id:guid}/dataset-renames");
        Policies(ApiAccess.Write);
        Summary(s => s.Summary = "Post Connections Dataset Renames By Id");
        Description(b =>
        {
            b.WithName("PostConnectionsDatasetRenamesById");
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
        var input = request.Input;
        var ctx = HttpContext;
        var hash = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(input)));
        var prior = await db.DatasetRenames.SingleOrDefaultAsync(x => x.RequestId == input.RequestId);
        if (prior is not null)
        {
            await Send.ResultAsync(prior.ConnectionId == id && prior.SubmissionHash == hash ? Results.Ok(prior) : Results.Conflict(new { error = "This request ID was already used for different evidence." }));
            return;
        }

        var from = await db.DatasetSamples.SingleOrDefaultAsync(x => x.Id == input.FromSampleId && x.ConnectionId == id);
        var to = await db.DatasetSamples.SingleOrDefaultAsync(x => x.Id == input.ToSampleId && x.ConnectionId == id);
        if (from is null || to is null)
        {
            await Send.ResultAsync(Results.NotFound(new { error = "A selected inventory is no longer retained. Reload history before reviewing." }));
            return;
        }

        if (Token(from, to) != input.ReviewToken)
        {
            await Send.ResultAsync(Results.Conflict(new { error = "The reviewed inventories changed. Reload both before submitting." }));
            return;
        }

        if (Validate(from, to, input)is { } error)
        {
            await Send.ResultAsync(Results.BadRequest(new { error }));
            return;
        }

        if (await db.DatasetRenames.AnyAsync(x => x.ConnectionId == id && x.ReversedUtc == null && ((x.FromSampleId == input.FromSampleId && x.FromName == input.FromName) || (x.ToSampleId == input.ToSampleId && x.ToName == input.ToName))))
        {
            await Send.ResultAsync(Results.Conflict(new { error = "This inventory entry already has an active rename review. Review or reverse that record first." }));
            return;
        }

        var row = new DatasetRenameReview
        {
            ConnectionId = id,
            FromSampleId = from.Id,
            ToSampleId = to.Id,
            FromName = input.FromName,
            ToName = input.ToName,
            RequestId = input.RequestId,
            SubmissionHash = hash,
            Evidence = input.Evidence,
            Author = ctx.User.Identity!.Name!,
            RecordedUtc = DateTime.UtcNow
        };
        db.DatasetRenames.Add(row);
        db.Activity.Add(new() { Actor = row.Author, Action = "Dataset rename reviewed", Detail = JsonSerializer.Serialize(new { id, input.FromSampleId, input.ToSampleId, input.FromName, input.ToName }) });
        await db.SaveChangesAsync();
        await Send.ResultAsync(Results.Ok(row));
        return;
    }
}
