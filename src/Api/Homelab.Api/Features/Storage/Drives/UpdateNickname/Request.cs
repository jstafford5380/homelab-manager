using FastEndpoints;

namespace Homelab.Api.Features.Storage.Drives.UpdateNickname;
public sealed class Request
{
    [RouteParam]
    public Guid Id { get; set; }

    [FromBody]
    public StorageNicknameInput Input { get; set; } = default !;
}
