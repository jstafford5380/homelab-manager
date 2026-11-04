using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;

namespace Homelab.Api;

public static class ApiAccess
{
    public const string Scheme = "SessionOrApiKey";
    public const string KeyScheme = "ApiKey";
    public const string Owner = "OwnerSession";
    public const string Read = "ApiRead";
    public const string Write = "ApiWrite";
    public const string ScopeClaim = "homelab:access";
    public const string ReadOnly = "read";
    public const string ReadWrite = "read-write";

    public static bool IsOwner(System.Security.Claims.ClaimsPrincipal user) =>
        user.Identities.Any(identity => identity.IsAuthenticated && identity.AuthenticationType == CookieAuthenticationDefaults.AuthenticationScheme);

    public static void Configure(AuthorizationOptions options)
    {
        var owner = new AuthorizationPolicyBuilder(Scheme).RequireAuthenticatedUser()
            .RequireAssertion(context => IsOwner(context.User)).Build();
        options.AddPolicy(Owner, owner);
        // New operations remain owner-only until they explicitly select API access.
        options.DefaultPolicy = owner;
        options.AddPolicy(Read, new AuthorizationPolicyBuilder(Scheme).RequireAuthenticatedUser()
            .RequireAssertion(context => IsOwner(context.User) || context.User.HasClaim(ScopeClaim, ReadOnly) || context.User.HasClaim(ScopeClaim, ReadWrite)).Build());
        options.AddPolicy(Write, new AuthorizationPolicyBuilder(Scheme).RequireAuthenticatedUser()
            .RequireAssertion(context => IsOwner(context.User) || context.User.HasClaim(ScopeClaim, ReadWrite)).Build());
    }
}
