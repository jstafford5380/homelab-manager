using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using static Homelab.Api.SynologySettings;
using FastEndpoints;

namespace Homelab.Api.Features.Hardware.Synology.AddServer;
public sealed class Endpoint(HomelabDb db, IConfiguration config, IDataProtectionProvider protection) : Endpoint<Request, Response>
{
    public override void Configure()
    {
        Post("/api/v1/hardware/synology");
        Policies(ApiAccess.Owner);
        Summary(s => s.Summary = "Add Server");
        Description(b =>
        {
            b.WithName("HardwareSynologyAddServer");
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
        var input = request.Input;
        await Send.ResultAsync(await Save(null, input, db, config, protection));
        return;
    }
}
