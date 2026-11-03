using Microsoft.EntityFrameworkCore;
using FastEndpoints;

namespace Homelab.Api.Features.Findings.GetFinding;
public sealed class Endpoint(HomelabDb db) : Endpoint<Request, System.Text.Json.Nodes.JsonNode>
{
    public override void Configure()
    {
        Get("/api/v1/findings/{id:guid}");
        AuthSchemes(Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationDefaults.AuthenticationScheme);
        Summary(s => s.Summary = "Get Finding");
        Description(b =>
        {
            b.WithName("FindingsGetFinding");
            b.WithTags("Findings");
            b.Accepts<Request>(true, "*/*", "application/json");
            b.Produces<System.Text.Json.Nodes.JsonNode>(200);
            b.Produces(404);
            b.Produces(401);
            b.Produces(403);
        }, clearDefaults: true);
    }

    public override async Task HandleAsync(Request request, CancellationToken ct)
    {
        var id = request.Id;
        var finding = await db.Findings.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id);
        if (finding is null)
        {
            await Send.ResultAsync(Results.NotFound());
            return;
        }

        var result = System.Text.Json.JsonSerializer.SerializeToNode(finding, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web))!;
        result["resolutionToken"] = FindingLifecycle.ResolutionToken(finding);
        await Send.ResultAsync(Results.Ok(result));
        return;
    }
}
