using System.Net.Http.Headers;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Homelab.Api;

public sealed class ApiKeyHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger,
    UrlEncoder encoder, HomelabDb db, WriteCoordinator writes, TimeProvider clock)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue("Authorization", out var values))
            return AuthenticateResult.NoResult();
        if (values.Count != 1 || !AuthenticationHeaderValue.TryParse(values[0], out var authorization) ||
            !authorization.Scheme.Equals("Bearer", StringComparison.OrdinalIgnoreCase) ||
            authorization.Parameter is not { Length: 101 } token || !token.StartsWith("hmk_", StringComparison.Ordinal) ||
            token[36] != '_' || !Guid.TryParseExact(token.AsSpan(4, 32), "N", out var id) ||
            token.AsSpan(37).ContainsAnyExcept("0123456789abcdef"))
        {
            return AuthenticateResult.Fail("Invalid API key.");
        }
        var now = clock.GetUtcNow().UtcDateTime;
        var key = await db.ApiKeys.AsNoTracking().SingleOrDefaultAsync(key => key.Id == id, Context.RequestAborted);
        if (key is null || key.RevokedUtc is not null || key.ExpiresUtc <= now ||
            key.Access is not (ApiAccess.ReadOnly or ApiAccess.ReadWrite) ||
            !CryptographicOperations.FixedTimeEquals(SHA256.HashData(Encoding.UTF8.GetBytes(token)), key.SecretHash))
            return AuthenticateResult.Fail("Invalid API key.");

        // Validate revocation on every request, but avoid writing usage on every read.
        if (key.LastUsedUtc is null || key.LastUsedUtc < now.AddMinutes(-5))
        {
            using var lease = await writes.EnterAsync(Context.RequestAborted);
            await db.ApiKeys.Where(row => row.Id == id && row.RevokedUtc == null &&
                (row.LastUsedUtc == null || row.LastUsedUtc < now.AddMinutes(-5)))
                .ExecuteUpdateAsync(update => update.SetProperty(row => row.LastUsedUtc, now), Context.RequestAborted);
        }
        var identity = new ClaimsIdentity([
            new Claim(ClaimTypes.NameIdentifier, key.Id.ToString()),
            new Claim(ClaimTypes.Name, "API key: " + key.Name + " (" + key.Id + ")"),
            new Claim(ApiAccess.ScopeClaim, key.Access)
        ], Scheme.Name);
        return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name));
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.Headers.WWWAuthenticate = "Bearer";
        Response.StatusCode = StatusCodes.Status401Unauthorized;
        return Response.WriteAsJsonAsync(new ApiError("A valid API key or owner session is required."));
    }

    protected override Task HandleForbiddenAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status403Forbidden;
        return Response.WriteAsJsonAsync(new ApiError("This credential does not have permission for this operation."));
    }
}
