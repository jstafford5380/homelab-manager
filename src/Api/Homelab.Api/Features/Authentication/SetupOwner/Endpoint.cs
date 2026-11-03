using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using FastEndpoints;

namespace Homelab.Api.Features.Authentication.SetupOwner;
public sealed class Endpoint(HomelabDb db, PasswordHasher<Owner> hasher, IConfiguration config) : Endpoint<Request, EmptyResponse>
{
    public override void Configure()
    {
        Post("/api/v1/setup");
        AllowAnonymous();
        Summary(s => s.Summary = "Setup Owner");
        Options(b => b.RequireRateLimiting("auth"));
        Description(b =>
        {
            b.WithName("AuthenticationSetupOwner");
            b.WithTags("Authentication");
            b.Accepts<Request>("application/json");
            b.Produces(200);
            b.Produces<ApiError>(400);
            b.Produces<ApiError>(409);
            b.Produces(403);
            b.Produces(503);
        }, clearDefaults: true);
    }

    public override async Task HandleAsync(Request request, CancellationToken ct)
    {
        var input = request.Input;
        if (await db.Owners.AnyAsync())
        {
            await Send.ResultAsync(Results.Conflict(new { error = "Owner already exists." }));
            return;
        }

        var token = DeploymentSecurity.ReadSecret(config, "SetupToken");
        if (string.IsNullOrEmpty(token) || input.SetupToken != token)
        {
            await Send.ResultAsync(Results.BadRequest(new { error = "Set Homelab__SetupToken on the server and enter the matching setup token." }));
            return;
        }

        if (string.IsNullOrEmpty(input.Password) || input.Password.Length > 1024 || input.Username?.Length > 100 || input.Password.Length < 9 || string.IsNullOrWhiteSpace(input.Username))
        {
            await Send.ResultAsync(Results.BadRequest(new { error = "A username and password of at least 9 characters are required." }));
            return;
        }

        var owner = new Owner
        {
            Username = input.Username.Trim()
        };
        owner.PasswordHash = hasher.HashPassword(owner, input.Password);
        db.Owners.Add(owner);
        await db.SaveChangesAsync();
        await Send.ResultAsync(Results.Ok());
        return;
    }
}
