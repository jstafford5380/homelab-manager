using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using FastEndpoints;

namespace Homelab.Api.Features.Storage.Drives.UpdateNickname;
public sealed class Endpoint(HomelabDb db) : Endpoint<Request, EmptyResponse>
{
    public override void Configure()
    {
        Put("/api/v1/storage/drives/{id:guid}/nickname");
        AuthSchemes(Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationDefaults.AuthenticationScheme);
        Summary(s => s.Summary = "Update Nickname");
        Description(b =>
        {
            b.WithName("StorageDrivesUpdateNickname");
            b.WithTags("Storage");
            b.Accepts<Request>("application/json");
            b.Produces(200);
            b.Produces<ApiError>(400);
            b.Produces(404);
            b.Produces(401);
            b.Produces(403);
        }, clearDefaults: true);
    }

    public override async Task HandleAsync(Request request, CancellationToken ct)
    {
        var id = request.Id;
        var input = request.Input;
        var context = HttpContext;
        var disk = await db.Disks.FindAsync(id);
        if (disk is null)
        {
            await Send.ResultAsync(Results.NotFound());
            return;
        }

        if (input.Nickname?.Length > 200)
        {
            await Send.ResultAsync(Results.BadRequest(new { error = "Keep names under 200 characters." }));
            return;
        }

        var before = JsonSerializer.Serialize(disk);
        disk.Nickname = string.IsNullOrWhiteSpace(input.Nickname) ? null : input.Nickname.Trim();
        db.DiskAnnotations.Add(new() { DiskId = id, Actor = context.User.Identity!.Name!, BeforeJson = before, AfterJson = JsonSerializer.Serialize(disk) });
        await db.SaveChangesAsync();
        await Send.ResultAsync(Results.Ok());
        return;
    }
}
