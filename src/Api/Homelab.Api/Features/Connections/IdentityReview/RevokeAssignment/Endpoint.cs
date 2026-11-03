using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using FastEndpoints;

namespace Homelab.Api.Features.Connections.IdentityReview.RevokeAssignment;
public sealed class Endpoint(HomelabDb db) : Endpoint<Request, Homelab.Api.DiscoveryAssignment>
{
    public override void Configure()
    {
        Post("/api/v1/connections/{id:guid}/identity-review/{assignmentId:guid}/revoke");
        AuthSchemes(Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationDefaults.AuthenticationScheme);
        Summary(s => s.Summary = "Revoke Assignment");
        Description(b =>
        {
            b.WithName("ConnectionsIdentityReviewRevokeAssignment");
            b.WithTags("Connections");
            b.Accepts<Request>("application/json");
            b.Produces<Homelab.Api.DiscoveryAssignment>(200);
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
        var assignmentId = request.AssignmentId;
        var input = request.Input;
        var ctx = HttpContext;
        var row = await db.DiscoveryAssignments.SingleOrDefaultAsync(x => x.Id == assignmentId && x.ConnectionId == id);
        if (row is null)
        {
            await Send.ResultAsync(Results.NotFound());
            return;
        }

        if (string.IsNullOrWhiteSpace(input.Evidence) || input.Evidence.Length > 4000)
        {
            await Send.ResultAsync(Results.BadRequest(new { error = "Record a reason in 1–4000 characters." }));
            return;
        }

        if (row.RevokedUtc != null)
        {
            await Send.ResultAsync(row.RevocationEvidence == input.Evidence ? Results.Ok(row) : Results.Conflict(new { error = "This assignment was already revoked." }));
            return;
        }

        row.RevokedUtc = DateTime.UtcNow;
        row.RevokedBy = ctx.User.Identity!.Name!;
        row.RevocationEvidence = input.Evidence;
        var disk = await db.Disks.FindAsync(row.DiskId);
        if (disk is not null)
            disk.MappingConfidence = "ambiguous";
        db.Activity.Add(new() { Actor = row.RevokedBy, Action = "Discovery assignment revoked", Detail = JsonSerializer.Serialize(row) });
        await db.SaveChangesAsync();
        await Send.ResultAsync(Results.Ok(row));
        return;
    }
}
