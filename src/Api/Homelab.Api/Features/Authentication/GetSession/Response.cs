namespace Homelab.Api.Features.Authentication.GetSession;
public sealed record Response(bool Authenticated, bool SetupRequired);
