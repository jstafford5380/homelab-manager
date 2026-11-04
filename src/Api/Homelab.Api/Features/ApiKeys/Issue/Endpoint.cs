using System.Security.Cryptography;
using System.Text;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;

namespace Homelab.Api.Features.ApiKeys.Issue;

public sealed class Endpoint(HomelabDb db, TimeProvider clock) : Endpoint<Request, Response>
{
    public override void Configure()
    {
        Post("/api/v1/api-keys");
        Policies(ApiAccess.Owner);
        Summary(s => s.Summary = "Issue an API key (secret returned once)");
        Description(b => b.WithTags("API keys").Produces<Response>(201).Produces<ApiError>(400).Produces(401).Produces(403));
    }

    public override async Task HandleAsync(Request request, CancellationToken ct)
    {
        var name = request.Name?.Trim();
        if (string.IsNullOrEmpty(name) || name.Length > 100 || name.Any(char.IsControl) ||
            request.Access is not (ApiAccess.ReadOnly or ApiAccess.ReadWrite) || request.ExpiresInDays is < 1 or > 365)
        {
            await Send.ResultAsync(Results.BadRequest(new ApiError("Enter a name (1–100 characters), read or read-write access, and an expiry of 1–365 days.")));
            return;
        }
        var now = clock.GetUtcNow().UtcDateTime;
        if (await db.ApiKeys.CountAsync(key => key.RevokedUtc == null && key.ExpiresUtc > now, ct) >= 100)
        {
            await Send.ResultAsync(Results.BadRequest(new ApiError("Revoke an existing key before issuing more than 100 active keys.")));
            return;
        }
        var owner = await db.Owners.SingleAsync(owner => owner.Username == User.Identity!.Name, ct);
        var key = new ApiKey { Name = name, OwnerId = owner.Id, Access = request.Access, CreatedUtc = now, ExpiresUtc = now.AddDays(request.ExpiresInDays) };
        var secret = "hmk_" + key.Id.ToString("N") + "_" + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));
        key.SecretHash = SHA256.HashData(Encoding.UTF8.GetBytes(secret));
        db.ApiKeys.Add(key);
        db.Activity.Add(new() { Actor = User.Identity!.Name!, Action = "API key issued", Detail = $"{key.Name} ({key.Id}); {key.Access}; expires {key.ExpiresUtc:O}" });
        await db.SaveChangesAsync(ct);
        await Send.ResultAsync(Results.Created($"/api/v1/api-keys/{key.Id}", new Response(ApiKeyInfo.From(key), secret)));
    }
}
