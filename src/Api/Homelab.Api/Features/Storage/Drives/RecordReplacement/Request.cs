using FastEndpoints;

namespace Homelab.Api.Features.Storage.Drives.RecordReplacement;
public sealed class Request
{
    [FromBody]
    public ReplacementInput Input { get; set; } = default !;
}
