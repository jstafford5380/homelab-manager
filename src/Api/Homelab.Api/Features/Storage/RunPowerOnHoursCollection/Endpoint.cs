using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using FastEndpoints;

namespace Homelab.Api.Features.Storage.RunPowerOnHoursCollection;
public sealed class Endpoint(Collector collector) : Endpoint<EmptyRequest, PowerOnHoursRunResult>
{
    public override void Configure()
    {
        Post("/api/v1/storage/power-on-hours/run");
        Policies(ApiAccess.Write);
        Summary(s => s.Summary = "Run Power On Hours Collection");
        Options(b => b.WithMetadata(new OwnsWriteLease()));
        Description(b =>
        {
            b.WithName("StorageRunPowerOnHoursCollection");
            b.WithTags("Storage");
            b.Produces<PowerOnHoursRunResult>(200);
            b.Produces<ApiError>(400);
            b.Produces<ApiError>(409);
            b.Produces(401);
            b.Produces(403);
        }, clearDefaults: true);
    }

    public override async Task HandleAsync(EmptyRequest request, CancellationToken ct)
    {
        await Send.ResultAsync(await collector.RunPowerOnHoursNowAsync(ct));
        return;
    }
}
