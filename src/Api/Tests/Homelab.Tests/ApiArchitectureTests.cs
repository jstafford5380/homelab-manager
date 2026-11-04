using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Homelab.Api;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;
using OpenTelemetry.Logs;

namespace Homelab.Tests;

public sealed class ApiArchitectureTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task EveryOperationIsDiscoveredAndKeepsItsAuthorizationBoundary()
    {
        using var client = factory.CreateClient();
        var endpoints = factory.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>().Where(e => e.Metadata.GetMetadata<FastEndpoints.EndpointDefinition>() is not null).ToArray();
        var expected = await File.ReadAllLinesAsync(Path.Combine(AppContext.BaseDirectory, "Fixtures", "api-routes.txt"));
        var actual = endpoints.SelectMany(e => e.Metadata.GetRequiredMetadata<HttpMethodMetadata>().HttpMethods.Select(m => m + " " + e.RoutePattern.RawText)).Order().ToArray();
        Assert.Equal(expected.Order(), actual);
        Assert.Equal(113, endpoints.Length);
        Assert.Equal(3, endpoints.Count(e => e.Metadata.GetMetadata<OwnsWriteLease>() is not null));
        foreach (var endpoint in endpoints)
        {
            var path = endpoint.RoutePattern.RawText!;
            var anonymous = path.StartsWith("/health/") || path is "/api/v1/session" or "/api/v1/setup" or "/api/v1/login";
            Assert.Equal(anonymous, endpoint.Metadata.GetMetadata<IAllowAnonymous>() is not null);
            if (!anonymous) Assert.NotEmpty(endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>());
        }
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/storage/overview")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health/ready")).StatusCode);
    }

    [Fact]
    public async Task OpenApiIncludesAllOperationsAndDescribesBodyRouteAndQueryBinding()
    {
        using var client = factory.CreateClient();
        var json = await client.GetFromJsonAsync<JsonElement>("/openapi/v1.json");
        var paths = json.GetProperty("paths");
        Assert.Equal(113, paths.EnumerateObject().Sum(p => p.Value.EnumerateObject().Count(o => o.Name is "get" or "post" or "put" or "delete" or "patch")));
        var profile = paths.GetProperty("/api/v1/storage/drives/{id}/profile").GetProperty("put");
        Assert.Equal("3.0.4", json.GetProperty("openapi").GetString());
        Assert.True(profile.TryGetProperty("requestBody", out _));
        Assert.Contains(profile.GetProperty("parameters").EnumerateArray(), p => p.GetProperty("name").GetString() == "X-Homelab-Request" && p.GetProperty("required").GetBoolean());
        Assert.True(profile.TryGetProperty("security", out var security));
        Assert.NotEmpty(security.EnumerateArray());
        Assert.True(paths.GetProperty("/api/v1/setup").GetProperty("post").GetProperty("responses").TryGetProperty("200", out _));
        Assert.Contains("Storage", paths.GetProperty("/api/v1/storage/overview").GetProperty("get").GetProperty("tags").EnumerateArray().Select(t => t.GetString()));
        Assert.Contains(profile.GetProperty("parameters").EnumerateArray(), p => p.GetProperty("name").GetString() == "id" && p.GetProperty("in").GetString() == "path");
        var detail = paths.GetProperty("/api/v1/hardware/servers/{id}").GetProperty("get");
        Assert.Contains(detail.GetProperty("parameters").EnumerateArray(), p => p.GetProperty("name").GetString() == "range" && p.GetProperty("in").GetString() == "query");
        Assert.Contains("scalar", await client.GetStringAsync("/scalar/v1"), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ConsoleAndOpenTelemetryLoggingAreBothRegistered()
    {
        using var client = factory.CreateClient();
        var providers = factory.Services.GetServices<ILoggerProvider>().ToArray();
        Assert.Contains(providers, p => p is ConsoleLoggerProvider);
        Assert.Contains(providers, p => p is OpenTelemetryLoggerProvider);
    }

    [Fact]
    public async Task JsonBodyBindingAndMutationHeaderRemainCompatible()
    {
        using var client = factory.CreateClient();
        var credentials = new { username = "owner", password = "refactor-test-password", setupToken = "refactor-test" };
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/api/v1/setup", credentials)).StatusCode);
        client.DefaultRequestHeaders.Add("X-Homelab-Request", "1");
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/v1/setup", credentials)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/v1/login", credentials)).StatusCode);
        foreach (var malformed in new[] { "null", "{" })
        {
            using var content = new StringContent(malformed, System.Text.Encoding.UTF8, "application/json");
            var rejected = await client.PostAsync("/api/v1/login", content);
            Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
            Assert.False(string.IsNullOrWhiteSpace((await rejected.Content.ReadFromJsonAsync<ApiError>())!.Error));
        }
        var session = await client.GetFromJsonAsync<JsonElement>("/api/v1/session");
        Assert.True(session.GetProperty("authenticated").GetBoolean());
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/v1/plans", new PlanningItem())).StatusCode);
        var saved = await client.GetFromJsonAsync<JsonElement>("/api/v1/settings");
        var revision = saved.GetProperty("revisions").GetProperty("monitoring").GetString();
        const string monitoring = "/api/v1/settings/monitoring";
        var rejectedSection = await client.PatchAsJsonAsync(monitoring, new { revision, changes = new { timezone = "UTC" } });
        Assert.Equal(HttpStatusCode.BadRequest, rejectedSection.StatusCode);
        var invalidField = await client.PatchAsJsonAsync(monitoring, new { revision, changes = new { inventoryMinutes = 0 } });
        Assert.Equal(HttpStatusCode.BadRequest, invalidField.StatusCode);
        var invalidBody = await invalidField.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(invalidBody.GetProperty("errors").TryGetProperty("inventoryMinutes", out _));
        var patch = await client.PatchAsJsonAsync(monitoring, new { revision, changes = new { inventoryMinutes = 30 } });
        Assert.Equal(HttpStatusCode.OK, patch.StatusCode);
        var result = (await patch.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("settings");
        Assert.Equal(30, result.GetProperty("inventoryMinutes").GetInt32());
        Assert.Equal(saved.GetProperty("settings").GetProperty("timezone").GetString(), result.GetProperty("timezone").GetString());
        Assert.Equal(HttpStatusCode.Conflict, (await client.PatchAsJsonAsync(monitoring, new { revision, changes = new { inventoryMinutes = 45 } })).StatusCode);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HomelabDb>();
        var connection = new NasConnection { Name = "Route binding", Enabled = false };
        var disk = new Disk { ConnectionId = connection.Id, Serial = "route-binding" };
        db.Connections.Add(connection);
        db.Disks.Add(disk);
        await db.SaveChangesAsync();
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/v1/hardware/servers/{connection.Id}?range=30d")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/v1/disks/{disk.Id}/spare-reviews")).StatusCode);
        var beforeRotation = await client.GetFromJsonAsync<JsonElement>("/api/v1/connections");
        Assert.Equal(JsonValueKind.Null, beforeRotation[0].GetProperty("apiKeyReplacedUtc").ValueKind);
        Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync($"/api/v1/connections/{connection.Id}/credentials",
            new { apiKey = "replacement-fixture-key", configurationRevision = connection.ConfigurationRevision })).StatusCode);
        var afterRotation = await client.GetFromJsonAsync<JsonElement>("/api/v1/connections");
        Assert.InRange(afterRotation[0].GetProperty("apiKeyReplacedUtc").GetDateTime(), DateTime.UtcNow.AddMinutes(-1), DateTime.UtcNow);
        Assert.DoesNotContain("replacement-fixture-key", afterRotation.GetRawText());

    }
}

public sealed class ApiFactory : WebApplicationFactory<Program>
{
    private readonly string folder = Path.Combine(Path.GetTempPath(), "homelab-architecture-" + Guid.NewGuid().ToString("N"));
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("Homelab:DataPath", folder);
        builder.UseSetting("Homelab:SetupToken", "refactor-test");
        builder.ConfigureTestServices(services => services.RemoveAll<IHostedService>());
    }
    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(folder)) Directory.Delete(folder, true);
    }
}
