using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using static Homelab.Api.SynologySettings;
using FastEndpoints;

namespace Homelab.Api.Features.Hardware.Synology.UpdateServer;
public sealed class Endpoint(HomelabDb db, IConfiguration config, IDataProtectionProvider protection) : Endpoint<Request, Response>
{
    public override void Configure()
    {
        Put("/api/v1/hardware/synology/{id:guid}");
        AuthSchemes(Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationDefaults.AuthenticationScheme);
        Summary(s => s.Summary = "Update Server");
        Description(b =>
        {
            b.WithName("HardwareSynologyUpdateServer");
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
        var input = request.Input;
        await Send.ResultAsync(await Save(id, input, db, config, protection));
        return;
    }
}
