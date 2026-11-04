using Microsoft.EntityFrameworkCore;
using FastEndpoints;

namespace Homelab.Api.Features.Backups.ListBackups;
public sealed class Endpoint(HomelabDb db) : Endpoint<EmptyRequest, System.Collections.Generic.List<Homelab.Api.BackupRecord>>
{
    public override void Configure()
    {
        Get("/api/v1/backups");
        Policies(ApiAccess.Owner);
        Summary(s => s.Summary = "List Backups");
        Description(b =>
        {
            b.WithName("BackupsListBackups");
            b.WithTags("Backups");
            b.Produces<System.Collections.Generic.List<Homelab.Api.BackupRecord>>(200);
            b.Produces(401);
            b.Produces(403);
        }, clearDefaults: true);
    }

    public override async Task HandleAsync(EmptyRequest request, CancellationToken ct)
    {
        await Send.ResultAsync(Results.Ok(await db.Backups.OrderByDescending(x => x.CreatedUtc).Take(100).ToListAsync()));
        return;
    }
}
