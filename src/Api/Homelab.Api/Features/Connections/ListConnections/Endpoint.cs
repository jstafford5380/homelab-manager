using Microsoft.EntityFrameworkCore;
using FastEndpoints;

namespace Homelab.Api.Features.Connections.ListConnections;
public sealed class Endpoint(HomelabDb db) : Endpoint<EmptyRequest, System.Collections.Generic.List<ResponseItem>>
{
    public override void Configure()
    {
        Get("/api/v1/connections");
        AuthSchemes(Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationDefaults.AuthenticationScheme);
        Summary(s => s.Summary = "List Connections");
        Description(b =>
        {
            b.WithName("ConnectionsListConnections");
            b.WithTags("Connections");
            b.Produces<System.Collections.Generic.List<ResponseItem>>(200);
            b.Produces(401);
            b.Produces(403);
        }, clearDefaults: true);
    }

    public override async Task HandleAsync(EmptyRequest request, CancellationToken ct)
    {
        await Send.ResultAsync(Results.Ok(await db.Connections.Select(c => new ResponseItem(c.Id, c.ConfigurationRevision, c.Name, c.ApiUri, c.ConnectHost, c.ConnectPort, c.PollSeconds, c.Enabled, c.TrustedCaPem, c.CertificateSha256, c.Status, c.Version, c.LastSuccessUtc, c.SystemSerial, c.ObservedSystemSerial, c.SystemIdentityState, c.SystemIdentityCheckedUtc)).ToListAsync()));
        return;
    }
}
