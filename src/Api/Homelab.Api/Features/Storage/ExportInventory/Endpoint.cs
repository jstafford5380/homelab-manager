using FastEndpoints;

namespace Homelab.Api.Features.Storage.ExportInventory;

public sealed class Endpoint(HomelabDb db) : EndpointWithoutRequest
{
    public override void Configure()
    {
        Get("/api/v1/storage/export.csv");
        Policies(ApiAccess.Read);
        Summary(s => s.Summary = "Export complete saved storage inventory as a restorable CSV");
        Description(b => {
            b.WithName("StorageExportInventory").WithTags("Storage");
            b.Produces(200, contentType: "text/csv");
            b.Produces(401);
        }, clearDefaults: true);
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var stream = await StorageCsvExport.CreateAsync(db, now, ct);
        await Send.ResultAsync(Results.Stream(stream, "text/csv; charset=utf-8", $"homelab-storage-{now:yyyyMMdd-HHmmss}.csv"));
    }
}
