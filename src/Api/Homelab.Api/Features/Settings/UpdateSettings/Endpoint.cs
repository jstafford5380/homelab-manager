using Microsoft.EntityFrameworkCore;
using FastEndpoints;

namespace Homelab.Api.Features.Settings.UpdateSettings;
public sealed class Endpoint(HomelabDb db, BackupService backups) : Endpoint<Request, Homelab.Api.OperatingSettings>
{
    public override void Configure()
    {
        Put("/api/v1/settings");
        Policies(ApiAccess.Owner);
        Summary(s => s.Summary = "Update Settings");
        Description(b =>
        {
            b.WithName("SettingsUpdateSettings");
            b.WithTags("Settings");
            b.Accepts<Request>("application/json");
            b.Produces<Homelab.Api.OperatingSettings>(200);
            b.Produces<ApiError>(400);
            b.Produces(401);
            b.Produces(403);
        }, clearDefaults: true);
    }

    public override async Task HandleAsync(Request request, CancellationToken ct)
    {
        var input = request.Input;
        var errors = SettingsSections.Validate(input, backups.Destination is not null);
        if (errors.Count > 0)
        {
            await Send.ResultAsync(Results.BadRequest(new { error = errors.Values.First()[0], errors }));
            return;
        }

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        input.Id = 1;
        var stored = await db.Settings.FindAsync(1);
        if (stored is null)
            db.Settings.Add(input);
        else
        {
            input.ElectricityRateUsdPerKwh ??= stored.ElectricityRateUsdPerKwh;
            db.Entry(stored).CurrentValues.SetValues(input);
        }
        db.Activity.Add(new() { Action = "Operating settings changed" });
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        await Send.ResultAsync(Results.Ok(input));
        return;
    }
}
