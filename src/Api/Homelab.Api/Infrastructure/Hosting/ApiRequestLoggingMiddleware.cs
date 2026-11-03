using System.Diagnostics;

namespace Homelab.Api.Infrastructure.Hosting;

public sealed class ApiRequestLoggingMiddleware(RequestDelegate next, ILogger<ApiRequestLoggingMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        if (!context.Request.Path.StartsWithSegments("/api"))
        {
            await next(context);
            return;
        }

        var start = Stopwatch.GetTimestamp();
        var failed = false;
        try { await next(context); }
        catch { failed = true; throw; }
        finally
        {
            // Route templates exclude query strings, credentials, and user-supplied identifiers.
            var route = (context.GetEndpoint() as RouteEndpoint)?.RoutePattern.RawText ?? "unmatched";
            logger.LogInformation("HTTP {Method} {Route} returned {StatusCode} in {ElapsedMs:F1} ms",
                context.Request.Method, route, failed ? 500 : context.Response.StatusCode, Stopwatch.GetElapsedTime(start).TotalMilliseconds);
        }
    }
}
