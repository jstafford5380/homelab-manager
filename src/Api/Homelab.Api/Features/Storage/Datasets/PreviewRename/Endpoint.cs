using Microsoft.EntityFrameworkCore;
using static Homelab.Api.DatasetRenames;
using FastEndpoints;

namespace Homelab.Api.Features.Storage.Datasets.PreviewRename;
public sealed class Endpoint(HomelabDb db) : Endpoint<Request, Response>
{
    public override void Configure()
    {
        Get("/api/v1/connections/{id:guid}/dataset-renames/review");
        Policies(ApiAccess.Read);
        Summary(s => s.Summary = "Preview Rename");
        Description(b =>
        {
            b.WithName("StorageDatasetsPreviewRename");
            b.WithTags("Connections");
            b.Accepts<Request>(true, "*/*", "application/json");
            b.Produces<Response>(200);
            b.Produces<ApiError>(400);
            b.Produces(404);
            b.Produces(401);
            b.Produces(403);
        }, clearDefaults: true);
    }

    public override async Task HandleAsync(Request request, CancellationToken ct)
    {
        var id = request.Id;
        var fromSampleId = request.FromSampleId;
        var toSampleId = request.ToSampleId;
        var from = await db.DatasetSamples.AsNoTracking().SingleOrDefaultAsync(x => x.Id == fromSampleId && x.ConnectionId == id);
        var to = await db.DatasetSamples.AsNoTracking().SingleOrDefaultAsync(x => x.Id == toSampleId && x.ConnectionId == id);
        if (from is null || to is null)
        {
            await Send.ResultAsync(Results.NotFound());
            return;
        }

        if (from.Id == to.Id || from.FetchedUtc >= to.FetchedUtc)
        {
            await Send.ResultAsync(Results.BadRequest(new { error = "Choose an earlier and a later saved inventory." }));
            return;
        }

        await Send.ResultAsync(Results.Ok(new Response(Token(from, to), Preview(from), Preview(to), "Manual rename evidence only. This does not establish a GUID or authorize changes to the NAS. Saved inventories remain separate and are retained while referenced by this review.")));
        return;
    }
}
