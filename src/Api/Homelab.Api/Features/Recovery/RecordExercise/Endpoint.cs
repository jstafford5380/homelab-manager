using Microsoft.EntityFrameworkCore;
using static Homelab.Api.RecoveryRules;
using FastEndpoints;

namespace Homelab.Api.Features.Recovery.RecordExercise;
public sealed class Endpoint(HomelabDb db) : Endpoint<Request, Homelab.Api.RestoreExercise>
{
    public override void Configure()
    {
        Post("/api/v1/recovery/{id:guid}/exercises");
        AuthSchemes(Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationDefaults.AuthenticationScheme);
        Summary(s => s.Summary = "Record Exercise");
        Description(b =>
        {
            b.WithName("RecoveryRecordExercise");
            b.WithTags("Recovery");
            b.Accepts<Request>("application/json");
            b.Produces<Homelab.Api.RestoreExercise>(200);
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
        var policy = await db.Recovery.FindAsync(id);
        if (policy is null)
        {
            await Send.ResultAsync(Results.NotFound());
            return;
        }

        if (input.PolicyRevision != policy.Revision)
        {
            await Send.ResultAsync(StalePolicy());
            return;
        }

        var now = DateTime.UtcNow;
        if (RecoveryValidation.Exercise(input, now)is { } error)
        {
            await Send.ResultAsync(Results.BadRequest(new { error }));
            return;
        }

        input.Id = Guid.NewGuid();
        input.PolicyId = id;
        input.PolicyRevision = policy.Revision;
        input.Author = ctx.User.Identity!.Name!;
        input.RecordedUtc = now;
        db.RestoreExercises.Add(input);
        db.Activity.Add(new() { Actor = input.Author, Action = "Restore exercise recorded", Detail = policy.Asset });
        await db.SaveChangesAsync();
        await Send.ResultAsync(Results.Ok(input));
        return;
    }
}
