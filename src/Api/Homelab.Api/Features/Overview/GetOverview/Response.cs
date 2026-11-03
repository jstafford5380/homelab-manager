namespace Homelab.Api.Features.Overview.GetOverview;
public sealed record ResponseConnectionsItem(Guid Id, string Name, string ApiUri, string? ConnectHost, int? ConnectPort, string? Version, string Status, DateTime? LastSuccessUtc, int PollSeconds, bool Enabled);
public sealed record ResponseCapabilitiesItem(Guid ConnectionId, string Name, string Status, DateTime? LastSuccessUtc);
public sealed record ResponseRecoveryAssessmentsItem(Guid PolicyId, RecoveryAssessment Assessment);
public sealed record Response(List<ResponseConnectionsItem> Connections, IEnumerable<ResponseCapabilitiesItem> Capabilities, OperatingSettings Settings, List<Disk> Disks, List<Finding> Findings, List<PlanningItem> Plans, List<RecoveryPolicy> Recovery, ScanReport[] ScanReports, PoolCapacityOutlook[] CapacityOutlook, IEnumerable<ResponseRecoveryAssessmentsItem> RecoveryAssessments, List<AuditEvent> Activity);
