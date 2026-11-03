namespace Homelab.Api.Features.Hardware.Synology.GetSettings;
public sealed record ResponseItem(Guid Id, string Name, string Host, bool Connect, bool CredentialConfigured);
