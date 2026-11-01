using Homelab.Api;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();
var dataPath = Path.GetFullPath(builder.Configuration["Homelab:DataPath"] ?? "data");
Directory.CreateDirectory(dataPath);
builder.Services.AddDbContext<HomelabDb>(o => o.UseSqlite($"Data Source={Path.Combine(dataPath, "homelab.db")}"));
DeploymentSecurity.Configure(builder, dataPath);
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie(o => {
    o.Cookie.Name = "homelab.session"; o.Cookie.HttpOnly = true; o.Cookie.SameSite = SameSiteMode.Strict;
    o.Cookie.SecurePolicy = builder.Environment.IsDevelopment() ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
    o.ExpireTimeSpan = TimeSpan.FromHours(8);
    o.Events.OnRedirectToLogin = c => { c.Response.StatusCode = 401; return Task.CompletedTask; };
    o.Events.OnRedirectToAccessDenied = c => { c.Response.StatusCode = 403; return Task.CompletedTask; };
});
builder.Services.AddAuthorization();
builder.Services.AddRateLimiter(o => o.AddPolicy("auth", context => RateLimitPartition.GetFixedWindowLimiter(context.Connection.RemoteIpAddress?.ToString() ?? "local", _ => new() { PermitLimit = 10, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 })));
builder.Services.AddSingleton<PasswordHasher<Owner>>();
builder.Services.AddSingleton<WriteCoordinator>();
builder.Services.AddSingleton<INasReadClientFactory, NasReadClientFactory>();
builder.Services.AddHostedService<Collector>();
builder.Services.AddSingleton<IWebhookSender, WebhookSender>();
builder.Services.AddSingleton<NotificationEngine>();
builder.Services.AddHostedService<NotificationWorker>();
builder.Services.AddSingleton<BackupService>();
builder.Services.AddHostedService<BackupScheduler>();
builder.Services.AddHostedService<HistoryRetentionWorker>();
var app = builder.Build();
using (var scope = app.Services.CreateScope()) {
    var db = scope.ServiceProvider.GetRequiredService<HomelabDb>();
    DatabaseLifecycle.Initialize(db, dataPath);
}
app.Use(async (context, next) => {
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["Referrer-Policy"] = "no-referrer";
    context.Response.Headers["X-Frame-Options"] = "DENY";
    if (context.Request.Path.StartsWithSegments("/api")) context.Response.Headers.CacheControl = "no-store";
    if (!app.Environment.IsDevelopment() && !context.Request.IsHttps && context.Request.Path.StartsWithSegments("/api")) {
        context.Response.StatusCode = 400; await context.Response.WriteAsJsonAsync(new { error = "HTTPS is required for authenticated API access." }); return;
    }
    await next();
});
app.UseDefaultFiles(); app.UseStaticFiles();
app.UseAuthentication(); app.UseAuthorization(); app.UseRateLimiter();
// A mandatory custom header prevents browser cross-origin form submissions. No CORS is enabled.
app.Use(async (context, next) => {
    if (context.Request.Path.StartsWithSegments("/api") && !HttpMethods.IsGet(context.Request.Method) && context.Request.Headers["X-Homelab-Request"] != "1") {
        context.Response.StatusCode = 403; return;
    }
    // Webhook testing owns short claim/completion leases around its network call.
    var webhookTest = HttpMethods.IsPost(context.Request.Method) && string.Equals(context.Request.Path.Value?.TrimEnd('/'), "/api/v1/notifications/test", StringComparison.OrdinalIgnoreCase);
    if (context.Request.Path.StartsWithSegments("/api") && !HttpMethods.IsGet(context.Request.Method) && !webhookTest) {
        using var lease = await context.RequestServices.GetRequiredService<WriteCoordinator>().EnterAsync(context.RequestAborted);
        await next();
    } else await next();
});
app.MapDefaultEndpoints();
app.MapGet("/health/live", () => Results.Ok(new { status = "alive" }));
app.MapGet("/health/ready", async (HomelabDb db) => await db.Database.CanConnectAsync() ? Results.Ok(new { status = "ready" }) : Results.StatusCode(503));
app.MapGet("/api/v1/session", async (HttpContext ctx, HomelabDb db) => Results.Ok(new { authenticated = ctx.User.Identity?.IsAuthenticated ?? false, setupRequired = !await db.Owners.AnyAsync() }));
app.MapPost("/api/v1/setup", async (Credentials input, HomelabDb db, PasswordHasher<Owner> hasher, IConfiguration config) => {
    if (await db.Owners.AnyAsync()) return Results.Conflict(new { error = "Owner already exists." });
    var token = DeploymentSecurity.ReadSecret(config, "SetupToken");
    if (string.IsNullOrEmpty(token) || input.SetupToken != token) return Results.BadRequest(new { error = "Set Homelab__SetupToken on the server and enter the matching setup token." });
    if (string.IsNullOrEmpty(input.Password) || input.Password.Length > 1024 || input.Username?.Length > 100 || input.Password.Length < 9 || string.IsNullOrWhiteSpace(input.Username)) return Results.BadRequest(new { error = "A username and password of at least 9 characters are required." });
    var owner = new Owner { Username = input.Username.Trim() };
    owner.PasswordHash = hasher.HashPassword(owner, input.Password); db.Owners.Add(owner); await db.SaveChangesAsync();
    return Results.Ok();
}).RequireRateLimiting("auth");
app.MapPost("/api/v1/login", async (Credentials input, HomelabDb db, PasswordHasher<Owner> hasher, HttpContext ctx) => {
    if (string.IsNullOrEmpty(input.Password) || input.Password.Length > 1024 || string.IsNullOrEmpty(input.Username) || input.Username.Length > 100) return Results.Unauthorized();
    var owner = await db.Owners.SingleOrDefaultAsync(x => x.Username == input.Username);
    if (owner is null) return Results.Unauthorized();
    var verification = hasher.VerifyHashedPassword(owner, owner.PasswordHash, input.Password);
    if (verification == PasswordVerificationResult.Failed) return Results.Unauthorized();
    if (verification == PasswordVerificationResult.SuccessRehashNeeded) {
        owner.PasswordHash = hasher.HashPassword(owner, input.Password); await db.SaveChangesAsync();
    }
    await ctx.SignInAsync(new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Name, owner.Username)], CookieAuthenticationDefaults.AuthenticationScheme)));
    return Results.Ok();
}).RequireRateLimiting("auth");
var api = app.MapGroup("/api/v1").RequireAuthorization();
api.MapOperations();
api.MapStorage();
api.MapCollectionHistory();
api.MapCachedTemperatures();
api.MapDatasets();
api.MapNotifications();
api.MapRecovery();
api.MapActivityHistory();
api.MapInventory();
api.MapFindings();
api.MapConnections();
api.MapPost("/logout", async (HttpContext ctx) => { await ctx.SignOutAsync(); return Results.Ok(); });
api.MapGet("/overview", async (HomelabDb db, CancellationToken ct) => {
    var recoveryPolicies = await db.Recovery.ToListAsync();
    var backupEvidence = await db.BackupEvidence.ToListAsync();
    var restoreExercises = await db.RestoreExercises.ToListAsync();
    return new {
    connections = await db.Connections.Select(x => new { x.Id, x.Name, x.ApiUri, x.ConnectHost, x.ConnectPort, x.Version, x.Status, x.LastSuccessUtc, x.PollSeconds, x.Enabled }).ToListAsync(),
    capabilities = (await db.Capabilities.AsNoTracking().ToListAsync()).Select(x => new { x.ConnectionId, x.Name, status = x.EffectiveStatus(DateTime.UtcNow), x.LastSuccessUtc }),
    settings = await db.Settings.SingleOrDefaultAsync() ?? new OperatingSettings(),
    disks = await db.Disks.ToListAsync(), findings = await db.Findings.ToListAsync(),
    plans = await db.Plans.ToListAsync(), recovery = recoveryPolicies,
    scanReports = await ScanOverview.ReadAsync(db, DateTime.UtcNow, ct),
    capacityOutlook = await CapacityOutlook.ReadAsync(db, DateTime.UtcNow, ct),
    recoveryAssessments = recoveryPolicies.Select(p => new { policyId = p.Id, assessment = RecoveryEvaluator.Assess(p, backupEvidence.Where(r => r.PolicyId == p.Id), restoreExercises.Where(r => r.PolicyId == p.Id), DateTime.UtcNow) }),
    activity = await db.Activity.OrderByDescending(x => x.Id).Take(100).ToListAsync()
}; });
api.MapObservationHistory();
api.MapGet("/disks/{id:guid}/observations", async (Guid id, HomelabDb db) => await db.Observations.Where(x => x.DiskId == id).OrderByDescending(x => x.Id).Take(1000).ToListAsync());
api.MapPost("/connections", async (ConnectionInput input, HomelabDb db, IDataProtectionProvider protection) => {
    if (ConnectionEndpoints.Validate(new(input.Name, input.ApiUri, input.ConnectHost, input.ConnectPort, 60, true, input.TrustedCaPem, null)) is {} validationError) return Results.BadRequest(new { error = validationError });
    if (ConnectionEndpoints.ValidateApiKey(input.ApiKey) is {} keyError) return Results.BadRequest(new { error = keyError });
    var connection = new NasConnection { Name = input.Name, ApiUri = input.ApiUri, ConnectHost = input.ConnectHost, ConnectPort = input.ConnectPort, TrustedCaPem = string.IsNullOrWhiteSpace(input.TrustedCaPem) ? null : input.TrustedCaPem, ProtectedApiKey = protection.CreateProtector("nas-credentials").Protect(input.ApiKey) };
    db.Connections.Add(connection); db.Activity.Add(new() { Action = "NAS enrolled", Detail = input.Name }); await db.SaveChangesAsync();
    return Results.Ok(new { connection.Id });
});
api.MapPlanning();
api.MapSpareCompatibility();
api.MapReplacements();
api.MapMaintenance();
api.MapDriveAcquisitions();
api.MapDrivePaths();
api.MapIdentityHistory();
api.MapDiscoveryAssignments();
api.MapPlanEditing();
api.MapSmartReports();
api.MapDriveEvidence();
api.MapPost("/findings/{id:guid}/acknowledge", async (Guid id, HomelabDb db, HttpContext ctx) => {
    var item = await db.Findings.FindAsync(id); if (item is null) return Results.NotFound();
    if (item.State == "resolved") return Results.Conflict(new { error = "Finding is already resolved." });
    item.AssignedOwner = ctx.User.Identity!.Name;
    item.State = "acknowledged"; item.ReviewUtc = DateTime.UtcNow.AddDays(7);
    db.Activity.Add(new() { Actor = ctx.User.Identity!.Name!, Action = "Finding acknowledged", Detail = item.Title }); await db.SaveChangesAsync(); return Results.Ok();
});
api.MapPost("/disks/{id:guid}/observations", async (Guid id, Observation input, HomelabDb db, HttpContext ctx) => {
    var disk = await db.Disks.FindAsync(id); if (disk is null) return Results.NotFound();
    if (ObservationValidation.Validate(input, DateTime.UtcNow) is {} error) return Results.BadRequest(new { error });
    if (await db.Observations.AnyAsync(x => x.ConnectionId == disk.ConnectionId && x.SourceKey == input.SourceKey)) return Results.Conflict(new { error = "This evidence has already been imported." });
    await ObservationIngestion.StageAsync(db, disk, input, ctx.User.Identity!.Name!);
    await db.SaveChangesAsync(); return Results.Ok(input);
});
app.MapFallback("/api/{**path}", () => Results.NotFound()).RequireAuthorization();
app.MapFallbackToFile("index.html");
app.Run();
record Credentials(string Username, string Password, string? SetupToken);
record ConnectionInput(string Name, string ApiUri, string ApiKey, string? ConnectHost, int? ConnectPort, string? TrustedCaPem);
