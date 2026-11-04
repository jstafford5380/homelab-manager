using Microsoft.EntityFrameworkCore;
using static Homelab.Api.HardwareComponents;
using FastEndpoints;

namespace Homelab.Api.Features.Hardware.Components.UpdateComponent;
public sealed class Endpoint(HomelabDb db, IConfiguration config) : Endpoint<Request, Response>
{
    public override void Configure()
    {
        Put("/api/v1/hardware/servers/{id:guid}/components/{componentId:guid}");
        Policies(ApiAccess.Write);
        Summary(s => s.Summary = "Update Component");
        Description(b =>
        {
            b.WithName("HardwareComponentsUpdateComponent");
            b.WithTags("Hardware");
            b.Accepts<Request>("application/json");
            b.Produces<Response>(200);
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
        var componentId = request.ComponentId;
        var input = request.Input;
        await Send.ResultAsync(await SaveComponent(id, componentId, input, db, config));
        return;
    }
}
