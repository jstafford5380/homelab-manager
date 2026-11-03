namespace Homelab.Api.Features.Settings.GetSettings;
public sealed record PowerOnHoursRun(DateTime AtUtc, string Outcome, int? HoursAvailable, int? Drives);
public sealed record Response(OperatingSettings Settings, bool BackupDestinationConfigured,
    string? BackupDestinationPath, Dictionary<string, string> Revisions, PowerOnHoursRun? PowerOnHoursLastRun);
