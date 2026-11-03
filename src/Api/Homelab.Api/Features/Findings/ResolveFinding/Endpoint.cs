using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using static Homelab.Api.FindingLifecycle;
using FastEndpoints;

namespace Homelab.Api.Features.Findings.ResolveFinding;
public sealed class Endpoint(HomelabDb db) : Endpoint<Request, Homelab.Api.Finding>
{
    public override void Configure()
    {
        Post("/api/v1/findings/{id:guid}/resolve");
        AuthSchemes(Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationDefaults.AuthenticationScheme);
        Summary(s => s.Summary = "Resolve Finding");
        Description(b =>
        {
            b.WithName("FindingsResolveFinding");
            b.WithTags("Findings");
            b.Accepts<Request>("application/json");
            b.Produces<Homelab.Api.Finding>(200);
            b.Produces<ApiError>(400);
            b.Produces(404);
            b.Produces<ApiError>(409);
            b.Produces(401);
            b.Produces(403);
        }, clearDefaults: true);
    }

    public override async Task HandleAsync(Request request, CancellationToken ct)
    {
        var id = request.Id;
        var input = request.Input;
        var ctx = HttpContext;
        var finding = await db.Findings.FindAsync(id);
        if (finding is null)
        {
            await Send.ResultAsync(Results.NotFound());
            return;
        }

        if (string.IsNullOrWhiteSpace(input.Evidence) || input.Evidence.Length > 4000 || !input.ReviewedCurrentEvidence)
        {
            await Send.ResultAsync(Results.BadRequest(new { error = "Record resolution evidence (up to 4000 characters) and confirm you reviewed current evidence and recovery readiness." }));
            return;
        }

        if (finding.State == "resolved")
        {
            await Send.ResultAsync(Results.Conflict(new { error = "This finding is already resolved. Refresh to read the saved resolution." }));
            return;
        }

        if (input.ResolutionToken != ResolutionToken(finding))
        {
            await Send.ResultAsync(Results.Conflict(new { error = "The finding changed after you opened the form. Close it, refresh the finding and review the latest evidence before resolving it." }));
            return;
        }

        FindingLifecycle.Resolve(finding, DateTime.UtcNow, input.Evidence);
        db.Activity.Add(new() { Actor = ctx.User.Identity!.Name!, Action = "Finding manually resolved", Detail = JsonSerializer.Serialize(new { finding.Id, finding.Rule, finding.ResolutionEvidence, finding.ResolvedUtc }) });
        await db.SaveChangesAsync();
        await Send.ResultAsync(Results.Ok(finding));
        return;
    }
}
