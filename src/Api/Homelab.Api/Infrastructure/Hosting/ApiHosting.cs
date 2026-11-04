using Homelab.Api.Infrastructure.OpenApi;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using System.Threading.RateLimiting;
using FastEndpoints;
using FastEndpoints.OpenApi;
using Scalar.AspNetCore;
using Microsoft.AspNetCore.HttpOverrides;
using System.Net;

namespace Homelab.Api.Infrastructure.Hosting;
public static class ApiHosting
{
    public static WebApplicationBuilder AddHomelabApi(this WebApplicationBuilder builder)
    {
        var dataPath = Path.GetFullPath(builder.Configuration["Homelab:DataPath"] ?? "data");
        Directory.CreateDirectory(dataPath);
        builder.Services.AddDbContext<HomelabDb>(o => o.UseSqlite($"Data Source={Path.Combine(dataPath, "homelab.db")}"));
        DeploymentSecurity.Configure(builder, dataPath);
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddAuthentication(options =>
        {
            options.DefaultScheme = ApiAccess.Scheme;
            options.DefaultSignInScheme = CookieAuthenticationDefaults.AuthenticationScheme;
            options.DefaultSignOutScheme = CookieAuthenticationDefaults.AuthenticationScheme;
        }).AddPolicyScheme(ApiAccess.Scheme, null, options =>
        {
            // An explicit credential must never silently fall back to a browser session.
            options.ForwardDefaultSelector = context => context.Request.Headers.ContainsKey("Authorization")
                ? ApiAccess.KeyScheme : CookieAuthenticationDefaults.AuthenticationScheme;
        }).AddScheme<AuthenticationSchemeOptions, ApiKeyHandler>(ApiAccess.KeyScheme, _ => { }).AddCookie(o =>
        {
            o.Cookie.Name = "homelab.session";
            o.Cookie.HttpOnly = true;
            o.Cookie.SameSite = SameSiteMode.Strict;
            o.Cookie.SecurePolicy = builder.Environment.IsDevelopment() ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
            o.ExpireTimeSpan = TimeSpan.FromHours(8);
            o.Events.OnRedirectToLogin = c =>
            {
                c.Response.StatusCode = 401;
                return Task.CompletedTask;
            };
            o.Events.OnRedirectToAccessDenied = c =>
            {
                c.Response.StatusCode = 403;
                return Task.CompletedTask;
            };
        });
        builder.Services.AddAuthorization(ApiAccess.Configure);
        builder.Services.AddRateLimiter(o => o.AddPolicy("auth", context => RateLimitPartition.GetFixedWindowLimiter(context.Connection.RemoteIpAddress?.ToString() ?? "local", _ => new() { PermitLimit = 10, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 })));
        builder.Services.AddSingleton<PasswordHasher<Owner>>();
        builder.Services.AddSingleton<WriteCoordinator>();
        builder.Services.AddSingleton<INasReadClientFactory, NasReadClientFactory>();
        builder.Services.AddSingleton<IIntegrationProvider, TrueNasProvider>();
        builder.Services.AddSingleton<IIntegrationProvider, SynologyHardwareProvider>();
        builder.Services.AddSingleton<ProviderRegistry>();
        builder.Services.AddSingleton<ISynologyHardwareReader, SynologyHardwareReader>();
        builder.Services.AddSingleton<SynologyHardwareCollector>();
        builder.Services.AddHostedService(sp => sp.GetRequiredService<SynologyHardwareCollector>());
        builder.Services.AddSingleton<Collector>();
        builder.Services.AddHostedService(sp => sp.GetRequiredService<Collector>());
        builder.Services.AddSingleton<ISmartSshTransport, SmartSshTransport>();
        builder.Services.AddHostedService<SmartSshCollector>();
        builder.Services.AddSingleton<IWebhookSender, WebhookSender>();
        builder.Services.AddSingleton<NotificationEngine>();
        builder.Services.AddHostedService<NotificationWorker>();
        builder.Services.AddSingleton<BackupService>();
        builder.Services.AddHostedService<BackupScheduler>();
        builder.Services.AddHostedService<HistoryRetentionWorker>();
        builder.Services.AddFastEndpoints();
        builder.Services.AddApiDocumentation();
        return builder;
    }

    public static WebApplication UseHomelabApi(this WebApplication app)
    {
        var dataPath = Path.GetFullPath(app.Configuration["Homelab:DataPath"] ?? "data");
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HomelabDb>();
            DatabaseLifecycle.Initialize(db, dataPath);
        }

        if (app.Configuration["Homelab:TrustedProxy"] is { Length: > 0 } trustedProxy)
        {
            var options = new ForwardedHeadersOptions {
                ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
                ForwardLimit = 1
            };
            options.KnownProxies.Clear();
            options.KnownIPNetworks.Clear();
            if (IPAddress.TryParse(trustedProxy, out var address))
                options.KnownProxies.Add(address);
            else if (System.Net.IPNetwork.TryParse(trustedProxy, out var network) && network.PrefixLength > 0)
                options.KnownIPNetworks.Add(network);
            else
                throw new InvalidOperationException("Homelab__TrustedProxy must be a proxy IP address or a restricted CIDR network.");
            app.UseForwardedHeaders(options);
        }
        app.UseMiddleware<ApiRequestLoggingMiddleware>();
        app.Use(async (context, next) =>
        {
            context.Response.Headers["X-Content-Type-Options"] = "nosniff";
            context.Response.Headers["Referrer-Policy"] = "no-referrer";
            context.Response.Headers["X-Frame-Options"] = "DENY";
            if (context.Request.Path.StartsWithSegments("/api"))
                context.Response.Headers.CacheControl = "no-store";
            if (!app.Environment.IsDevelopment() && !context.Request.IsHttps && context.Request.Path.StartsWithSegments("/api"))
            {
                context.Response.StatusCode = 400;
                await context.Response.WriteAsJsonAsync(new { error = "HTTPS is required for authenticated API access." });
                return;
            }

            await next();
        });
        app.UseDefaultFiles();
        app.UseStaticFiles();
        app.UseAuthentication();
        app.UseAuthorization();
        app.UseRateLimiter();
        // A mandatory custom header prevents browser cross-origin form submissions. No CORS is enabled.
        app.Use(async (context, next) =>
        {
            if (context.Request.Path.StartsWithSegments("/api") && !HttpMethods.IsGet(context.Request.Method) && context.Request.Headers["X-Homelab-Request"] != "1")
            {
                context.Response.StatusCode = 403;
                return;
            }

            var ownsWriteLease = context.GetEndpoint()?.Metadata.GetMetadata<OwnsWriteLease>() is not null;
            if (context.Request.Path.StartsWithSegments("/api") && !HttpMethods.IsGet(context.Request.Method) && !ownsWriteLease)
            {
                using var lease = await context.RequestServices.GetRequiredService<WriteCoordinator>().EnterAsync(context.RequestAborted);
                await next();
            }
            else
                await next();
        });
        app.MapDefaultEndpoints();
        app.UseFastEndpoints(options =>
        {
            options.Errors.ContentType = "application/json";
            options.Errors.ProducesMetadataType = typeof(ApiError);
            options.Errors.ResponseBuilder = (failures, _, _) => new ApiError(string.Join(" ", failures.Select(failure => failure.ErrorMessage)));
        });
        if (app.Environment.IsDevelopment())
        {
            app.MapOpenApi();
            app.MapScalarApiReference();
        }

        app.MapFallback("/api/{**path}", () => Results.NotFound()).RequireAuthorization();
        app.MapFallbackToFile("index.html");
        return app;
    }
}
