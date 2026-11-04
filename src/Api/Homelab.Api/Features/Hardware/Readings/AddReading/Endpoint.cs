using Microsoft.EntityFrameworkCore;
using static Homelab.Api.HardwareReadings;
using FastEndpoints;

namespace Homelab.Api.Features.Hardware.Readings.AddReading;
public sealed class Endpoint(HomelabDb db, IConfiguration config) : Endpoint<Request, HardwareReading>
{
    public override void Configure()
    {
        Post("/api/v1/hardware/servers/{id:guid}/readings");
        Policies(ApiAccess.Write);
        Summary(s => s.Summary = "Add Reading");
        Description(b =>
        {
            b.WithName("HardwareReadingsAddReading");
            b.WithTags("Hardware");
            b.Accepts<Request>("application/json");
            b.Produces<HardwareReading>(200);
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
        await Send.ResultAsync(await SaveReading(id, null, input, db, config));
        return;
    }
}
