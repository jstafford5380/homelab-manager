using System.Text.Json;
using System.Text.Json.Nodes;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;

namespace Homelab.Api.Features.Settings.PatchSection;

public sealed class Request
{
    [RouteParam]
    public string Section { get; set; } = "";

    [FromBody]
    public SectionChanges? Input { get; set; }
}

public sealed record SectionChanges(string Revision, JsonObject? Changes);
public sealed record Response(OperatingSettings Settings, Dictionary<string, string> Revisions);

public sealed class Endpoint(HomelabDb db, BackupService backups) : Endpoint<Request>
{
    public override void Configure()
    {
        Patch("/api/v1/settings/{section}");
        Policies(ApiAccess.Owner);
        Summary(s => s.Summary = "Save changes to one settings section");
        Description(b =>
        {
            b.WithName("SettingsPatchSection");
            b.WithTags("Settings");
            b.Accepts<SectionChanges>("application/json");
            b.Produces<Response>(200);
            b.ProducesProblem(400);
            b.Produces(401);
            b.Produces(403);
            b.Produces(404);
            b.Produces<ApiError>(409);
        }, clearDefaults: true);
    }

    public override async Task HandleAsync(Request request, CancellationToken ct)
    {
        if (!SettingsSections.Fields.TryGetValue(request.Section, out var fields))
        {
            await Send.ResultAsync(Results.NotFound());
            return;
        }
        if (request.Input?.Changes is not { Count: > 0 } changes || changes.Any(change => !fields.Contains(change.Key)))
        {
            await Send.ResultAsync(Results.BadRequest(new { error = "Send only fields belonging to this section." }));
            return;
        }

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var stored = await db.Settings.SingleOrDefaultAsync(ct);
        var document = SettingsSections.Document(stored ?? new());
        if (request.Input.Revision != SettingsSections.Revision(document, fields))
        {
            await Send.ResultAsync(Results.Conflict(new { error = "This section changed since you opened it. Reload the saved settings and review your edits." }));
            return;
        }
        foreach (var change in changes)
            document[change.Key] = change.Value?.DeepClone();

        OperatingSettings input;
        try { input = SettingsSections.Read(document); }
        catch (JsonException e)
        {
            var field = e.Path?.TrimStart('$', '.') ?? "";
            await Send.ResultAsync(Results.ValidationProblem(new Dictionary<string, string[]> { [field] = ["Enter a value of the expected type."] }));
            return;
        }

        var errors = SettingsSections.Validate(input, backups.Destination is not null)
            .Where(error => fields.Contains(error.Key)).ToDictionary();
        if (errors.Count > 0)
        {
            await Send.ResultAsync(Results.ValidationProblem(errors));
            return;
        }
        if (stored is null)
            db.Settings.Add(input);
        else
            db.Entry(stored).CurrentValues.SetValues(input);
        db.Activity.Add(new() { Action = "Operating settings changed" });
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        await Send.ResultAsync(Results.Ok(new Response(input, SettingsSections.Revisions(input))));
    }
}
