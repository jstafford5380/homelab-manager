using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using FastEndpoints;

namespace Homelab.Api.Features.Inventory.DeleteDependency;
public sealed class Endpoint(HomelabDb db) : Endpoint<Request, EmptyResponse>
{
    public override void Configure()
    {
        Delete("/api/v1/inventory/dependencies/{id:guid}");
        AuthSchemes(Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationDefaults.AuthenticationScheme);
        Summary(s => s.Summary = "Delete Dependency");
        Description(b =>
        {
            b.WithName("InventoryDeleteDependency");
            b.WithTags("Inventory");
            b.Accepts<Request>(true, "*/*", "application/json");
            b.Produces(204);
            b.Produces(404);
            b.Produces(401);
            b.Produces(403);
        }, clearDefaults: true);
    }

    public override async Task HandleAsync(Request request, CancellationToken ct)
    {
        var id = request.Id;
        var ctx = HttpContext;
        var dependency = await db.Dependencies.FindAsync(id);
        if (dependency is null)
        {
            await Send.ResultAsync(Results.NotFound());
            return;
        }

        db.Activity.Add(new() { Actor = ctx.User.Identity!.Name!, Action = "Dependency removed", Detail = JsonSerializer.Serialize(dependency) });
        db.Dependencies.Remove(dependency);
        await db.SaveChangesAsync();
        await Send.ResultAsync(Results.NoContent());
        return;
    }
}
