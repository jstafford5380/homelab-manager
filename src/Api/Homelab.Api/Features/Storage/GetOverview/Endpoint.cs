using Microsoft.EntityFrameworkCore;
using static Homelab.Api.StoragePage;
using FastEndpoints;

namespace Homelab.Api.Features.Storage.GetOverview;
public sealed class Endpoint(HomelabDb db, IConfiguration configuration) : Endpoint<EmptyRequest, Homelab.Api.StoragePageData>
{
    public override void Configure()
    {
        Get("/api/v1/storage/overview");
        AuthSchemes(Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationDefaults.AuthenticationScheme);
        Summary(s => s.Summary = "Get Overview");
        Description(b =>
        {
            b.WithName("StorageGetOverview");
            b.WithTags("Storage");
            b.Produces<Homelab.Api.StoragePageData>(200);
            b.Produces(401);
            b.Produces(403);
        }, clearDefaults: true);
    }

    public override async Task HandleAsync(EmptyRequest request, CancellationToken ct)
    {
        await Send.ResultAsync(Results.Ok(await ReadAsync(db, DateTime.UtcNow, ct, configuration.GetValue<bool>("Homelab:SmartEnabled"))));
        return;
    }
}
