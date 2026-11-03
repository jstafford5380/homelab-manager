using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using FastEndpoints;

namespace Homelab.Api.Features.Connections.EnrollConnection;
public sealed class Endpoint(HomelabDb db, IDataProtectionProvider protection) : Endpoint<Request, Response>
{
    public override void Configure()
    {
        Post("/api/v1/connections");
        AuthSchemes(Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationDefaults.AuthenticationScheme);
        Summary(s => s.Summary = "Enroll Connection");
        Description(b =>
        {
            b.WithName("ConnectionsEnrollConnection");
            b.WithTags("Connections");
            b.Accepts<Request>("application/json");
            b.Produces<Response>(200);
            b.Produces<ApiError>(400);
            b.Produces(401);
            b.Produces(403);
        }, clearDefaults: true);
    }

    public override async Task HandleAsync(Request request, CancellationToken ct)
    {
        var input = request.Input;
        if (ConnectionRules.Validate(new(input.Name, input.ApiUri, input.ConnectHost, input.ConnectPort, 60, true, input.TrustedCaPem, null))is { } validationError)
        {
            await Send.ResultAsync(Results.BadRequest(new { error = validationError }));
            return;
        }

        if (ConnectionRules.ValidateApiKey(input.ApiKey)is { } keyError)
        {
            await Send.ResultAsync(Results.BadRequest(new { error = keyError }));
            return;
        }

        var connection = new NasConnection
        {
            Name = input.Name,
            ApiUri = input.ApiUri,
            ConnectHost = input.ConnectHost,
            ConnectPort = input.ConnectPort,
            TrustedCaPem = string.IsNullOrWhiteSpace(input.TrustedCaPem) ? null : input.TrustedCaPem,
            ProtectedApiKey = protection.CreateProtector("nas-credentials").Protect(input.ApiKey)
        };
        db.Connections.Add(connection);
        db.Activity.Add(new() { Action = "NAS enrolled", Detail = input.Name });
        await db.SaveChangesAsync();
        await Send.ResultAsync(Results.Ok(new Response(connection.Id)));
        return;
    }
}
