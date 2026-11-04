using Microsoft.EntityFrameworkCore;
using FastEndpoints;

namespace Homelab.Api.Features.Backups.CreateBackup;
public sealed class Endpoint(HomelabDb db, BackupService backups) : Endpoint<EmptyRequest, Homelab.Api.BackupRecord>
{
    public override void Configure()
    {
        Post("/api/v1/backups");
        Policies(ApiAccess.Owner);
        Summary(s => s.Summary = "Create Backup");
        Description(b =>
        {
            b.WithName("BackupsCreateBackup");
            b.WithTags("Backups");
            b.Produces<Homelab.Api.BackupRecord>(200);
            b.Produces<ApiError>(400);
            b.Produces(500);
            b.Produces(401);
            b.Produces(403);
        }, clearDefaults: true);
    }

    public override async Task HandleAsync(EmptyRequest request, CancellationToken ct)
    {
        try
        {
            var record = await backups.CreateAsync(db, ct);
            await backups.RetainAsync(db, await db.Settings.SingleOrDefaultAsync(ct) ?? new OperatingSettings(), ct);
            await Send.ResultAsync(Results.Ok(record));
            return;
        }
        catch (InvalidOperationException)
        {
            await Send.ResultAsync(Results.BadRequest(new { error = "Backup unavailable. Check the server backup destination and its separation from live data." }));
            return;
        }
        catch (IOException)
        {
            await Send.ResultAsync(Results.Problem("Backup could not be written. Check destination availability and permissions."));
            return;
        }
    }
}
