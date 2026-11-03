using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using FastEndpoints;

namespace Homelab.Api.Features.Authentication.SignIn;
public sealed class Endpoint(HomelabDb db, PasswordHasher<Owner> hasher) : Endpoint<Request, EmptyResponse>
{
    public override void Configure()
    {
        Post("/api/v1/login");
        AllowAnonymous();
        Summary(s => s.Summary = "Sign In");
        Options(b => b.RequireRateLimiting("auth"));
        Description(b =>
        {
            b.WithName("AuthenticationSignIn");
            b.WithTags("Authentication");
            b.Accepts<Request>("application/json");
            b.Produces(200);
            b.Produces<ApiError>(400);
            b.Produces(401);
            b.Produces(403);
            b.Produces(503);
        }, clearDefaults: true);
    }

    public override async Task HandleAsync(Request request, CancellationToken ct)
    {
        var input = request.Input;
        var ctx = HttpContext;
        if (string.IsNullOrEmpty(input.Password) || input.Password.Length > 1024 || string.IsNullOrEmpty(input.Username) || input.Username.Length > 100)
        {
            await Send.ResultAsync(Results.Unauthorized());
            return;
        }

        var owner = await db.Owners.SingleOrDefaultAsync(x => x.Username == input.Username);
        if (owner is null)
        {
            await Send.ResultAsync(Results.Unauthorized());
            return;
        }

        var verification = hasher.VerifyHashedPassword(owner, owner.PasswordHash, input.Password);
        if (verification == PasswordVerificationResult.Failed)
        {
            await Send.ResultAsync(Results.Unauthorized());
            return;
        }

        if (verification == PasswordVerificationResult.SuccessRehashNeeded)
        {
            owner.PasswordHash = hasher.HashPassword(owner, input.Password);
            await db.SaveChangesAsync();
        }

        await ctx.SignInAsync(new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Name, owner.Username)], CookieAuthenticationDefaults.AuthenticationScheme)));
        await Send.ResultAsync(Results.Ok());
        return;
    }
}
