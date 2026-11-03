namespace Homelab.Api.Features.Settings.GetSettings;
public sealed record Response(OperatingSettings Settings, bool BackupDestinationConfigured);
