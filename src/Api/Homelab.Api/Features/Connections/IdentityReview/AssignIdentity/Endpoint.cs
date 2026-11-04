using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using static Homelab.Api.DiscoveryAssignments;
using FastEndpoints;

namespace Homelab.Api.Features.Connections.IdentityReview.AssignIdentity;
public sealed class Endpoint(HomelabDb db) : Endpoint<Request, Homelab.Api.DiscoveryAssignment>
{
    public override void Configure()
    {
        Post("/api/v1/connections/{id:guid}/identity-review");
        Policies(ApiAccess.Owner);
        Summary(s => s.Summary = "Assign Identity");
        Description(b =>
        {
            b.WithName("ConnectionsIdentityReviewAssignIdentity");
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
        var input = request.Input;
        var ctx = HttpContext;
        var old = await db.DiscoveryAssignments.SingleOrDefaultAsync(x => x.RequestId == input.RequestId);
        if (old is not null)
        {
            await Send.ResultAsync(old.ConnectionId == id && old.SubmissionHash == Hash(input) ? Results.Ok(old) : Results.Conflict(new { error = "This request ID was used for different evidence." }));
            return;
        }

        var nas = await db.Connections.FindAsync(id);
        if (nas is null)
        {
            await Send.ResultAsync(Results.NotFound());
            return;
        }

        var now = DateTime.UtcNow;
        var snapshot = Read(nas);
        if (!Usable(nas, snapshot, now))
        {
            await Send.ResultAsync(Results.Conflict(new { error = "A running connection and current-configuration inventory from the last 24 hours are required. Refresh inventory before review." }));
            return;
        }

        var disks = await db.Disks.Where(x => x.ConnectionId == id).ToArrayAsync();
        var assignments = await db.DiscoveryAssignments.Where(x => x.ConnectionId == id).ToArrayAsync();
        if (Token(nas, snapshot!, disks, assignments) != input.ReviewToken)
        {
            await Send.ResultAsync(Results.Conflict(new { error = "Inventory, identity records or assignments changed. Reload the review." }));
            return;
        }

        var rows = snapshot!.Rows.Where(x => IdentityCandidate.NormalizeWwn(x.Wwn) == input.Wwn).ToArray();
        if (!Compatible(rows) || !AliasConsistent(rows, snapshot.Rows) || input.Wwn != input.ConfirmedWwn || !input.ConfirmedPhysicalIdentity || input.RequestId == Guid.Empty || string.IsNullOrWhiteSpace(input.Evidence) || input.Evidence.Length > 4000 || input.ValidUntilUtc.Kind != DateTimeKind.Utc || input.ValidUntilUtc <= now || input.ValidUntilUtc > now.AddYears(1))
        {
            await Send.ResultAsync(Results.BadRequest(new { error = "Confirm the exact stable WWN and physical identity, provide evidence, and set a review expiry within one year. Missing or conflicting source identities cannot be overridden." }));
            return;
        }

        var row = rows[0];
        var target = input.DiskId is { } targetId ? disks.SingleOrDefault(x => x.Id == targetId) : null;
        if (input.DiskId != null && (target is null || IdentityCandidate.NormalizeWwn(target.Wwn) != input.Wwn || target.Serial.Trim() != row.Serial.Trim() || target.Model.Trim() != row.Model.Trim() || target.Lifecycle == "retired"))
        {
            await Send.ResultAsync(Results.BadRequest(new { error = "The selected record must match the reported WWN, serial and model and must not be retired." }));
            return;
        }

        if (target is null && disks.Any(x => IdentityCandidate.NormalizeWwn(x.Wwn) == input.Wwn && x.Serial.Trim() == row.Serial.Trim() && x.Model.Trim() == row.Model.Trim() && x.Lifecycle != "retired"))
        {
            await Send.ResultAsync(Results.BadRequest(new { error = "A matching active identity record exists. Review and select it instead of creating another duplicate." }));
            return;
        }

        if (target is null)
        {
            target = new Disk
            {
                ConnectionId = id,
                IdentityKey = "wwn:" + input.Wwn,
                Wwn = input.Wwn,
                Serial = row.Serial.Trim(),
                Model = row.Model.Trim(),
                Present = false,
                MappingConfidence = "review pending",
                FirstSeenUtc = snapshot.FetchedUtc,
                LastSeenUtc = snapshot.FetchedUtc
            };
            db.Disks.Add(target);
        }

        foreach (var prior in assignments.Where(x => x.Wwn == input.Wwn && x.RevokedUtc == null))
        {
            prior.RevokedUtc = now;
            prior.RevokedBy = ctx.User.Identity!.Name!;
            prior.RevocationEvidence = "Superseded by a newly reviewed assignment.";
        }

        var decision = new DiscoveryAssignment
        {
            ConnectionId = id,
            DiskId = target.Id,
            ConfigurationRevision = nas.ConfigurationRevision,
            RequestId = input.RequestId,
            SubmissionHash = Hash(input),
            Wwn = input.Wwn,
            Serial = row.Serial.Trim(),
            Model = row.Model.Trim(),
            Evidence = input.Evidence,
            Author = ctx.User.Identity!.Name!,
            RecordedUtc = now,
            ValidUntilUtc = input.ValidUntilUtc
        };
        db.DiscoveryAssignments.Add(decision);
        db.Activity.Add(new() { Actor = decision.Author, Action = "Stable WWN discovery assignment reviewed", Detail = JsonSerializer.Serialize(decision) });
        await db.SaveChangesAsync();
        await Send.ResultAsync(Results.Ok(decision));
        return;
    }
}
