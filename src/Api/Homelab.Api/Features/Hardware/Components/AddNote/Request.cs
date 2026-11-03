using FastEndpoints;

namespace Homelab.Api.Features.Hardware.Components.AddNote;
public sealed class Request
{
    [RouteParam]
    public Guid Id { get; set; }

    [RouteParam]
    public Guid ComponentId { get; set; }

    [FromBody]
    public HardwareNoteInput Input { get; set; } = default !;
}
