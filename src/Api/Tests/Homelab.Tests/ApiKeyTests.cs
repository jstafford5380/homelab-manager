using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Homelab.Api;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Homelab.Tests;

public sealed class ApiKeyTests
{
    private static async Task<HttpClient> Owner(ApiFactory factory)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Homelab-Request", "1");
        var credentials = new { username = "owner", password = "api-key-test-password", setupToken = "refactor-test" };
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/v1/setup", credentials)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/v1/login", credentials)).StatusCode);
        return client;
    }

    private static async Task<(Guid Id, string Secret)> Issue(HttpClient owner, string access = "read")
    {
        var response = await owner.PostAsJsonAsync("/api/v1/api-keys", new { name = "MCP client", access, expiresInDays = 30 });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Contains("no-store", response.Headers.CacheControl!.ToString());
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        return (json.GetProperty("key").GetProperty("id").GetGuid(), json.GetProperty("secret").GetString()!);
    }

    private static HttpClient KeyClient(ApiFactory factory, string secret)
    {
        var client = factory.CreateClient(new() { HandleCookies = false });
        client.DefaultRequestHeaders.Authorization = new("Bearer", secret);
        return client;
    }

    [Fact]
    public async Task SecretIsReturnedOnceHashedAndNeverListedOrAudited()
    {
        using var factory = new ApiFactory();
        using var owner = await Owner(factory);
        var (id, secret) = await Issue(owner);
        Assert.Equal(101, secret.Length);
        var list = await owner.GetStringAsync("/api/v1/api-keys");
        Assert.DoesNotContain(secret, list);
        Assert.DoesNotContain("secretHash", list, StringComparison.OrdinalIgnoreCase);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HomelabDb>();
            var saved = await db.ApiKeys.SingleAsync();
            Assert.Equal(id, saved.Id);
            Assert.Equal(SHA256.HashData(Encoding.UTF8.GetBytes(secret)), saved.SecretHash);
            Assert.Null(saved.LastUsedUtc);
            Assert.DoesNotContain(secret, JsonSerializer.Serialize(await db.Activity.ToArrayAsync()));
        }
        using var key = KeyClient(factory, secret);
        Assert.Equal(HttpStatusCode.OK, (await key.GetAsync("/api/v1/storage/overview")).StatusCode);
        using var check = factory.Services.CreateScope();
        Assert.NotNull((await check.ServiceProvider.GetRequiredService<HomelabDb>().ApiKeys.SingleAsync()).LastUsedUtc);
    }

    [Fact]
    public async Task ReadKeyCannotWriteOrAdministerEvenWithBrowserCookie()
    {
        using var factory = new ApiFactory();
        using var owner = await Owner(factory);
        var (_, secret) = await Issue(owner);
        using var key = KeyClient(factory, secret);
        Assert.Equal(HttpStatusCode.OK, (await key.GetAsync("/api/v1/planning/review")).StatusCode);
        key.DefaultRequestHeaders.Add("X-Homelab-Request", "1");
        Assert.Equal(HttpStatusCode.Forbidden, (await key.PostAsJsonAsync("/api/v1/plans", new { title = "Denied", reason = "Test" })).StatusCode);
        foreach (var path in new[] { "settings", "connections", "api-keys", "backups", "notifications", "hardware/synology/settings" })
            Assert.Equal(HttpStatusCode.Forbidden, (await key.GetAsync("/api/v1/" + path)).StatusCode);
        owner.DefaultRequestHeaders.Authorization = new("Bearer", secret);
        Assert.Equal(HttpStatusCode.Forbidden, (await owner.GetAsync("/api/v1/api-keys")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await owner.PostAsJsonAsync("/api/v1/plans", new { title = "Denied", reason = "Test" })).StatusCode);
    }

    [Fact]
    public async Task WriteKeyCanChangeApplicationRecordsWithGuardAndHasDistinctAuditActor()
    {
        using var factory = new ApiFactory();
        using var owner = await Owner(factory);
        var (_, secret) = await Issue(owner, "read-write");
        using var key = KeyClient(factory, secret);
        Assert.Equal(HttpStatusCode.Forbidden, (await key.PostAsJsonAsync("/api/v1/plans", new { title = "MCP plan", reason = "API test" })).StatusCode);
        key.DefaultRequestHeaders.Add("X-Homelab-Request", "1");
        Assert.Equal(HttpStatusCode.OK, (await key.PostAsJsonAsync("/api/v1/plans", new { title = "MCP plan", reason = "API test" })).StatusCode);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HomelabDb>();
        Assert.StartsWith("API key: MCP client (", (await db.PlanChanges.SingleAsync()).Actor);
        Assert.Equal(HttpStatusCode.Forbidden, (await key.PostAsJsonAsync("/api/v1/api-keys", new { name = "Escalation" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await key.PatchAsJsonAsync("/api/v1/settings/monitoring", new { })).StatusCode);
    }

    [Fact]
    public async Task RevocationIsImmediateIdempotentAndCannotBeUndoneByUsageUpdate()
    {
        using var factory = new ApiFactory();
        using var owner = await Owner(factory);
        var (id, secret) = await Issue(owner);
        using var key = KeyClient(factory, secret);
        Assert.Equal(HttpStatusCode.OK, (await key.GetAsync("/api/v1/storage/overview")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await owner.DeleteAsync("/api/v1/api-keys/" + id)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await owner.DeleteAsync("/api/v1/api-keys/" + id)).StatusCode);
        var rejected = await key.GetAsync("/api/v1/storage/overview");
        Assert.Equal(HttpStatusCode.Unauthorized, rejected.StatusCode);
        Assert.Contains(rejected.Headers.WwwAuthenticate, header => header.Scheme == "Bearer");
        Assert.Equal(HttpStatusCode.NotFound, (await owner.DeleteAsync("/api/v1/api-keys/" + Guid.NewGuid())).StatusCode);
        using var scope = factory.Services.CreateScope();
        Assert.Single(await scope.ServiceProvider.GetRequiredService<HomelabDb>().Activity.Where(row => row.Action == "API key revoked").ToArrayAsync());
    }

    [Theory]
    [InlineData("expired")]
    [InlineData("wrong-secret")]
    [InlineData("unknown-id")]
    [InlineData("malformed")]
    [InlineData("duplicate")]
    public async Task InvalidKeysFailWithoutFallingBackToOwnerSession(string failure)
    {
        using var factory = new ApiFactory();
        using var owner = await Owner(factory);
        var (_, secret) = await Issue(owner);
        if (failure == "expired")
        {
            using var scope = factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<HomelabDb>();
            (await db.ApiKeys.SingleAsync()).ExpiresUtc = DateTime.UtcNow.AddSeconds(-1);
            await db.SaveChangesAsync();
        }
        if (failure == "wrong-secret") secret = secret[..^1] + (secret[^1] == '0' ? "1" : "0");
        if (failure == "unknown-id") secret = "hmk_" + Guid.NewGuid().ToString("N") + secret[36..];
        if (failure == "malformed") secret = "invalid";
        owner.DefaultRequestHeaders.Authorization = new("Bearer", secret);
        if (failure == "duplicate") owner.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", "Bearer " + secret);
        Assert.Equal(HttpStatusCode.Unauthorized, (await owner.GetAsync("/api/v1/storage/overview")).StatusCode);
    }

    [Fact]
    public async Task IssuanceRejectsInvalidInputAndRequiresSessionAndMutationGuard()
    {
        using var factory = new ApiFactory();
        using var anonymous = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/v1/api-keys")).StatusCode);
        using var owner = await Owner(factory);
        foreach (var input in new object[] {
            new { name = "", access = "read", expiresInDays = 30 },
            new { name = (string?)null, access = "read", expiresInDays = 30 },
            new { name = new string('x', 101), access = "read", expiresInDays = 30 },
            new { name = "Unsafe\nname", access = "read", expiresInDays = 30 },
            new { name = "Client", access = "admin", expiresInDays = 30 },
            new { name = "Client", access = "read", expiresInDays = 0 },
            new { name = "Client", access = "read", expiresInDays = 366 } })
            Assert.Equal(HttpStatusCode.BadRequest, (await owner.PostAsJsonAsync("/api/v1/api-keys", input)).StatusCode);
        using var content = new StringContent("null", Encoding.UTF8, "application/json");
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.PostAsync("/api/v1/api-keys", content)).StatusCode);
        owner.DefaultRequestHeaders.Remove("X-Homelab-Request");
        Assert.Equal(HttpStatusCode.Forbidden, (await owner.PostAsJsonAsync("/api/v1/api-keys", new { name = "Client" })).StatusCode);
    }

    [Fact]
    public async Task OpenApiDescribesKeyAccessOnlyWherePolicyAllowsIt()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        var document = await client.GetFromJsonAsync<JsonElement>("/openapi/v1.json");
        var schemes = document.GetProperty("components").GetProperty("securitySchemes");
        Assert.Equal("bearer", schemes.GetProperty(ApiAccess.KeyScheme).GetProperty("scheme").GetString());
        var paths = document.GetProperty("paths");
        var readSecurity = paths.GetProperty("/api/v1/storage/overview").GetProperty("get").GetProperty("security");
        Assert.Contains(readSecurity.EnumerateArray(), requirement => requirement.TryGetProperty(ApiAccess.KeyScheme, out _));
        var ownerSecurity = paths.GetProperty("/api/v1/api-keys").GetProperty("post").GetProperty("security");
        Assert.DoesNotContain(ownerSecurity.EnumerateArray(), requirement => requirement.TryGetProperty(ApiAccess.KeyScheme, out _));
        Assert.DoesNotContain("secretHash", document.GetRawText(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task KeysAreNotAcceptedFromQueryStringsOrCustomHeaders()
    {
        using var factory = new ApiFactory();
        using var owner = await Owner(factory);
        var (_, secret) = await Issue(owner);
        using var client = factory.CreateClient(new() { HandleCookies = false });
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/storage/overview?api_key=" + secret)).StatusCode);
        client.DefaultRequestHeaders.Add("X-Api-Key", secret);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/storage/overview")).StatusCode);
    }

    [Fact]
    public void EveryProtectedOperationDeclaresAnExplicitAccessPolicy()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        var endpoints = factory.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>()
            .Where(endpoint => endpoint.Metadata.GetMetadata<FastEndpoints.EndpointDefinition>() is not null);
        foreach (var endpoint in endpoints.Where(endpoint => endpoint.Metadata.GetMetadata<IAllowAnonymous>() is null))
        {
            var policies = endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>().Select(data => data.Policy).ToArray();
            Assert.Single(policies, policy => policy is ApiAccess.Read or ApiAccess.Write or ApiAccess.Owner);
            if (endpoint.Metadata.GetRequiredMetadata<HttpMethodMetadata>().HttpMethods.Any(method => method != "GET"))
                Assert.DoesNotContain(ApiAccess.Read, policies);
        }
    }
}
