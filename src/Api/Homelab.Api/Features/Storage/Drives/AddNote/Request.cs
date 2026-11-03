using FastEndpoints;

namespace Homelab.Api.Features.Storage.Drives.AddNote;
public sealed class Request
{
    [RouteParam]
    public Guid Id { get; set; }

    [FromBody]
    public StorageNoteInput Input { get; set; } = default !;
}
