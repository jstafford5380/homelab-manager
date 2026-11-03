using FastEndpoints;

namespace Homelab.Api.Features.Recovery.RecordBackupRun;
public sealed class Request
{
    [RouteParam]
    public Guid Id { get; set; }

    [FromBody]
    public BackupEvidence Input { get; set; } = default !;
}
