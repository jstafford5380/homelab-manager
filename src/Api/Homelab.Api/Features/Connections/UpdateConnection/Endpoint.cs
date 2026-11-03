using Microsoft.EntityFrameworkCore;
using static Homelab.Api.ConnectionRules;
using FastEndpoints;

namespace Homelab.Api.Features.Connections.UpdateConnection;
public sealed class Endpoint(HomelabDb db) : Endpoint<Request, EmptyResponse>
{
    public override void Configure()
    {
        Put("/api/v1/connections/{id:guid}");
        AuthSchemes(Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationDefaults.AuthenticationScheme);
        Summary(s => s.Summary = "Update Connection");
        Description(b =>
        {
            b.WithName("ConnectionsUpdateConnection");
            b.WithTags("Connections");
            b.Accepts<Request>("application/json");
            b.Produces(200);
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
        var ctx = HttpContext;
        var connection = await db.Connections.FindAsync(id);
        if (connection is null)
        {
            await Send.ResultAsync(Results.NotFound());
            return;
        }

        if (ValidateField(input) is { } error)
        {
            await Send.ResultAsync(Results.BadRequest(new { error = error.Message, errors = new Dictionary<string, string[]> { [error.Field] = [error.Message] } }));
            return;
        }

        if (input.ConfigurationRevision != connection.ConfigurationRevision)
        {
            await Send.ResultAsync(Results.Conflict(new { error = "This connection changed since it was loaded. Reload the saved connection and review it before saving." }));
            return;
        }

        if (input.Enabled && string.IsNullOrWhiteSpace(connection.ProtectedApiKey))
        {
            await Send.ResultAsync(Results.BadRequest(new { error = "Re-enroll a credential before enabling collection." }));
            return;
        }

        var contextChanged = connection.ApiUri != input.ApiUri || connection.ConnectHost != input.ConnectHost || connection.ConnectPort != input.ConnectPort || connection.TrustedCaPem != input.TrustedCaPem || connection.CertificateSha256 != (string.IsNullOrWhiteSpace(input.CertificateSha256) ? null : input.CertificateSha256);
        if (contextChanged)
            await NasIdentity.InvalidateConnectionContextAsync(db, connection);
        connection.Name = input.Name;
        connection.ApiUri = input.ApiUri;
        connection.ConnectHost = input.ConnectHost;
        connection.ConnectPort = input.ConnectPort;
        connection.PollSeconds = input.PollSeconds;
        connection.Enabled = input.Enabled;
        connection.TrustedCaPem = input.TrustedCaPem;
        connection.CertificateSha256 = string.IsNullOrWhiteSpace(input.CertificateSha256) ? null : input.CertificateSha256;
        connection.ConfigurationRevision = Guid.NewGuid();
        connection.NextAttemptUtc = null;
        connection.Status = input.Enabled ? "Configuration changed — awaiting validation" : "Collection paused by owner";
        db.Activity.Add(new() { Actor = ctx.User.Identity!.Name!, Action = "NAS connection/trust configuration changed", Detail = id.ToString() });
        await db.SaveChangesAsync();
        await Send.ResultAsync(Results.Ok());
        return;
    }
}
