using Microsoft.EntityFrameworkCore;
using FastEndpoints;

namespace Homelab.Api.Features.Inventory.ExportInventory;
public sealed class Endpoint(HomelabDb db) : Endpoint<Request, EmptyResponse>
{
    public override void Configure()
    {
        Get("/api/v1/inventory/export");
        AuthSchemes(Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationDefaults.AuthenticationScheme);
        Summary(s => s.Summary = "Export Inventory");
        Description(b =>
        {
            b.WithName("InventoryExportInventory");
            b.WithTags("Inventory");
            b.Accepts<Request>(true, "*/*", "application/json");
            b.Produces(200, contentType: "application/json");
            b.Produces(401);
            b.Produces(403);
        }, clearDefaults: true);
    }

    public override async Task HandleAsync(Request request, CancellationToken ct)
    {
        var redacted = request.Redacted;
        await Send.ResultAsync(Results.File(await InventoryExport.CreateAsync(db, redacted == true), "application/json", $"homelab-inventory-{DateTime.UtcNow:yyyyMMdd}.json"));
        return;
    }
}
