using Microsoft.EntityFrameworkCore;
namespace Homelab.Api;

public sealed class HomelabDb(DbContextOptions<HomelabDb> options) : DbContext(options)
{
    public DbSet<DatasetRenameReview> DatasetRenames => Set<DatasetRenameReview>();
    public DbSet<DatasetInventorySample> DatasetSamples => Set<DatasetInventorySample>();
    public DbSet<DiscoveryAssignment> DiscoveryAssignments => Set<DiscoveryAssignment>();
    public DbSet<IdentityHistoryMerge> IdentityMerges => Set<IdentityHistoryMerge>();
    public DbSet<DrivePathReview> DrivePaths => Set<DrivePathReview>();
    public DbSet<SelfTestEvidence> SelfTestEvidence => Set<SelfTestEvidence>();
    public DbSet<SpareCompatibilityReview> SpareReviews => Set<SpareCompatibilityReview>();
    public DbSet<DriveAcquisition> DriveAcquisitions => Set<DriveAcquisition>();
    public DbSet<DriveMaintenance> Maintenance => Set<DriveMaintenance>();
    public DbSet<DiskReplacement> Replacements => Set<DiskReplacement>();
    public DbSet<PoolDailySummary> PoolDailySummaries => Set<PoolDailySummary>();
    public DbSet<PoolScan> PoolScans => Set<PoolScan>();
    public DbSet<PoolSnapshot> PoolSnapshots => Set<PoolSnapshot>();
    public DbSet<InventoryAsset> Assets => Set<InventoryAsset>();
    public DbSet<AssetDependency> Dependencies => Set<AssetDependency>();
    public DbSet<BackupEvidence> BackupEvidence => Set<BackupEvidence>();
    public DbSet<RestoreExercise> RestoreExercises => Set<RestoreExercise>();
    public DbSet<NotificationChannel> NotificationChannels => Set<NotificationChannel>();
    public DbSet<NotificationDelivery> Deliveries => Set<NotificationDelivery>();
    public DbSet<DeliveryAttempt> DeliveryAttempts => Set<DeliveryAttempt>();
    public DbSet<Capability> Capabilities => Set<Capability>();
    public DbSet<NativeAlert> NativeAlerts => Set<NativeAlert>();
    public DbSet<CollectionDailySummary> CollectionDailySummaries => Set<CollectionDailySummary>();
    public DbSet<CollectionRun> CollectionRuns => Set<CollectionRun>();
    public DbSet<DiskAlias> DiskAliases => Set<DiskAlias>();
    public DbSet<DiskAnnotation> DiskAnnotations => Set<DiskAnnotation>();
    public DbSet<OperatingSettings> Settings => Set<OperatingSettings>();
    public DbSet<BackupRecord> Backups => Set<BackupRecord>();
    public DbSet<Owner> Owners => Set<Owner>();
    public DbSet<NasConnection> Connections => Set<NasConnection>();
    public DbSet<Disk> Disks => Set<Disk>();
    public DbSet<SmartReport> SmartReports => Set<SmartReport>();
    public DbSet<Observation> Observations => Set<Observation>();
    public DbSet<Finding> Findings => Set<Finding>();
    public DbSet<PlanChange> PlanChanges => Set<PlanChange>();
    public DbSet<PlanningItem> Plans => Set<PlanningItem>();
    public DbSet<RecoveryPolicy> Recovery => Set<RecoveryPolicy>();
    public DbSet<AuditEvent> Activity => Set<AuditEvent>();
    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.Properties<DateTime>().HaveConversion<UtcDateTimeConverter>();
    }
    protected override void OnModelCreating(ModelBuilder model)
    {
        model.Entity<DatasetInventorySample>().HasAlternateKey(x=>new{x.Id,x.ConnectionId});
        model.Entity<DatasetRenameReview>().HasOne<NasConnection>().WithMany().HasForeignKey(x=>x.ConnectionId).OnDelete(DeleteBehavior.Restrict);
        model.Entity<DatasetRenameReview>().HasOne<DatasetInventorySample>().WithMany().HasForeignKey(x=>new{x.FromSampleId,x.ConnectionId}).HasPrincipalKey(x=>new{x.Id,x.ConnectionId}).OnDelete(DeleteBehavior.Restrict);
        model.Entity<DatasetRenameReview>().HasOne<DatasetInventorySample>().WithMany().HasForeignKey(x=>new{x.ToSampleId,x.ConnectionId}).HasPrincipalKey(x=>new{x.Id,x.ConnectionId}).OnDelete(DeleteBehavior.Restrict);
        model.Entity<DatasetRenameReview>().HasIndex(x=>x.RequestId).IsUnique();
        model.Entity<DatasetRenameReview>().HasIndex(x=>new{x.ConnectionId,x.Id});
        model.Entity<DatasetInventorySample>().HasOne<NasConnection>().WithMany().HasForeignKey(x=>x.ConnectionId).OnDelete(DeleteBehavior.Restrict);
        model.Entity<DatasetInventorySample>().HasIndex(x=>new{x.ConnectionId,x.ConfigurationRevision,x.HourUtc}).IsUnique();
        model.Entity<DatasetInventorySample>().HasIndex(x=>new{x.ConnectionId,x.Id});
        model.Entity<DatasetInventorySample>().HasIndex(x=>x.FetchedUtc);
        model.Entity<Disk>().HasOne<NasConnection>().WithMany().HasForeignKey(x => x.ConnectionId).OnDelete(DeleteBehavior.Restrict);
        model.Entity<Disk>().HasAlternateKey(x => new { x.Id, x.ConnectionId });
        model.Entity<Observation>().HasOne<NasConnection>().WithMany().HasForeignKey(x => x.ConnectionId).OnDelete(DeleteBehavior.Restrict);
        model.Entity<Observation>().HasOne<Disk>().WithMany().HasForeignKey(x => new { x.DiskId, x.ConnectionId }).HasPrincipalKey(x => new { x.Id, x.ConnectionId }).OnDelete(DeleteBehavior.Restrict);
        model.Entity<Finding>().HasOne<NasConnection>().WithMany().HasForeignKey(x => x.ConnectionId).OnDelete(DeleteBehavior.Restrict);
        model.Entity<Finding>().HasOne<Disk>().WithMany().HasForeignKey(x => new { x.DiskId, x.ConnectionId }).HasPrincipalKey(x => new { x.Id, x.ConnectionId }).OnDelete(DeleteBehavior.Restrict);
        model.Entity<PlanningItem>().HasOne<Disk>().WithMany().HasForeignKey(x => x.DiskId).OnDelete(DeleteBehavior.Restrict);
        model.Entity<DiskAlias>().HasOne<Disk>().WithMany().HasForeignKey(x => x.DiskId).OnDelete(DeleteBehavior.Restrict);
        model.Entity<DiskAnnotation>().HasOne<Disk>().WithMany().HasForeignKey(x => x.DiskId).OnDelete(DeleteBehavior.Restrict);
        model.Entity<Capability>().HasOne<NasConnection>().WithMany().HasForeignKey(x => x.ConnectionId).OnDelete(DeleteBehavior.Restrict);
        model.Entity<NativeAlert>().HasOne<NasConnection>().WithMany().HasForeignKey(x => x.ConnectionId).OnDelete(DeleteBehavior.Restrict);
        model.Entity<CollectionRun>().HasOne<NasConnection>().WithMany().HasForeignKey(x => x.ConnectionId).OnDelete(DeleteBehavior.Restrict);
        model.Entity<CollectionDailySummary>().HasOne<NasConnection>().WithMany().HasForeignKey(x => x.ConnectionId).OnDelete(DeleteBehavior.Restrict);
        model.Entity<BackupEvidence>().HasOne<RecoveryPolicy>().WithMany().HasForeignKey(x => x.PolicyId).OnDelete(DeleteBehavior.Restrict);
        model.Entity<RestoreExercise>().HasOne<RecoveryPolicy>().WithMany().HasForeignKey(x => x.PolicyId).OnDelete(DeleteBehavior.Restrict);
        model.Entity<DiscoveryAssignment>().HasOne<Disk>().WithMany().HasForeignKey(x=>new{x.DiskId,x.ConnectionId}).HasPrincipalKey(x=>new{x.Id,x.ConnectionId}).OnDelete(DeleteBehavior.Restrict);
        model.Entity<DiscoveryAssignment>().HasOne<NasConnection>().WithMany().HasForeignKey(x=>x.ConnectionId).OnDelete(DeleteBehavior.Restrict);
        model.Entity<DiscoveryAssignment>().HasIndex(x=>x.RequestId).IsUnique();
        model.Entity<DiscoveryAssignment>().HasIndex(x=>new{x.ConnectionId,x.Wwn,x.RevokedUtc});
        model.Entity<IdentityHistoryMerge>().HasOne<Disk>().WithMany().HasForeignKey(x=>new{x.FirstDiskId,x.ConnectionId}).HasPrincipalKey(x=>new{x.Id,x.ConnectionId}).OnDelete(DeleteBehavior.Restrict);
        model.Entity<IdentityHistoryMerge>().HasOne<Disk>().WithMany().HasForeignKey(x=>new{x.SecondDiskId,x.ConnectionId}).HasPrincipalKey(x=>new{x.Id,x.ConnectionId}).OnDelete(DeleteBehavior.Restrict);
        model.Entity<IdentityHistoryMerge>().HasOne<NasConnection>().WithMany().HasForeignKey(x=>x.ConnectionId).OnDelete(DeleteBehavior.Restrict);
        model.Entity<IdentityHistoryMerge>().HasIndex(x=>x.RequestId).IsUnique();
        model.Entity<IdentityHistoryMerge>().HasIndex(x=>new{x.ConnectionId,x.ReversedUtc});
        model.Entity<DrivePathReview>().HasOne<Disk>().WithMany().HasForeignKey(x=>x.DiskId).OnDelete(DeleteBehavior.Restrict);
        model.Entity<DrivePathReview>().HasOne<InventoryAsset>().WithMany().HasForeignKey(x=>x.AssetId).OnDelete(DeleteBehavior.Restrict);
        model.Entity<DrivePathReview>().HasIndex(x=>new{x.DiskId,x.RequestId}).IsUnique();
        model.Entity<DrivePathReview>().HasIndex(x=>new{x.DiskId,x.AssetId,x.Id});
        model.Entity<SelfTestEvidence>().HasOne<Disk>().WithMany().HasForeignKey(x => x.DiskId).OnDelete(DeleteBehavior.Restrict);
        model.Entity<SelfTestEvidence>().HasOne<SmartReport>().WithMany().HasForeignKey(x => x.ReportId).OnDelete(DeleteBehavior.Restrict);
        model.Entity<SelfTestEvidence>().HasIndex(x => new { x.DiskId, x.Fingerprint }).IsUnique();
        model.Entity<SpareCompatibilityReview>().HasOne<Disk>().WithMany().HasForeignKey(x => x.DiskId).OnDelete(DeleteBehavior.Restrict);
        model.Entity<SpareCompatibilityReview>().HasOne<InventoryAsset>().WithMany().HasForeignKey(x => x.AssetId).OnDelete(DeleteBehavior.Restrict);
        model.Entity<SpareCompatibilityReview>().HasIndex(x => new { x.DiskId, x.RequestId }).IsUnique();
        model.Entity<SpareCompatibilityReview>().HasIndex(x => new { x.DiskId, x.AssetId, x.Id });
        model.Entity<DriveAcquisition>().HasKey(x => x.DiskId);
        model.Entity<DriveAcquisition>().HasOne<Disk>().WithOne().HasForeignKey<DriveAcquisition>(x => x.DiskId).OnDelete(DeleteBehavior.Restrict);
        model.Entity<DriveMaintenance>().HasOne<Disk>().WithMany().HasForeignKey(x => x.DiskId).OnDelete(DeleteBehavior.Restrict);
        model.Entity<DriveMaintenance>().HasIndex(x => new { x.DiskId, x.RequestId }).IsUnique();
        model.Entity<DriveMaintenance>().HasIndex(x => new { x.DiskId, x.Id });
        model.Entity<SmartReport>().HasOne<Disk>().WithMany().HasForeignKey(x => x.DiskId).OnDelete(DeleteBehavior.Restrict);
        model.Entity<SmartReport>().HasIndex(x => new { x.DiskId, x.ReportHash }).IsUnique();
        model.Entity<PlanChange>().HasOne<PlanningItem>().WithMany().HasForeignKey(x => x.PlanId).OnDelete(DeleteBehavior.Restrict);
        model.Entity<PlanChange>().HasIndex(x => new { x.PlanId, x.Id });
        model.Entity<CollectionRun>().HasIndex(x => new { x.StartedUtc, x.Id });
        model.Entity<CollectionRun>().HasIndex(x => new { x.ConnectionId, x.Id });
        model.Entity<CollectionDailySummary>().HasIndex(x => x.GroupKey).IsUnique();
        model.Entity<CollectionDailySummary>().HasIndex(x => new { x.ConnectionId, x.DayUtc, x.Id });
        model.Entity<CollectionDailySummary>().HasIndex(x => x.DayUtc);
        model.Entity<PoolDailySummary>().HasIndex(x => x.DayUtc);
        model.Entity<DiskReplacement>().HasOne<Disk>().WithMany().HasForeignKey(x => x.OldDiskId).OnDelete(DeleteBehavior.Restrict);
        model.Entity<DiskReplacement>().HasOne<Disk>().WithMany().HasForeignKey(x => x.NewDiskId).OnDelete(DeleteBehavior.Restrict);
        model.Entity<DiskReplacement>().HasIndex(x => new { x.OldDiskId, x.OccurredUtc });
        model.Entity<DiskReplacement>().HasIndex(x => new { x.NewDiskId, x.OccurredUtc });
        model.Entity<PoolDailySummary>().HasIndex(x => x.GroupKey).IsUnique();
        model.Entity<PoolDailySummary>().HasIndex(x => new { x.ConnectionId, x.PoolGuid, x.DayUtc, x.Id });
        model.Entity<PoolDailySummary>().HasOne<NasConnection>().WithMany().HasForeignKey(x => x.ConnectionId).OnDelete(DeleteBehavior.Restrict);
        model.Entity<PoolScan>().HasIndex(x => new { x.ConnectionId, x.PoolGuid, x.ScanFunction, x.ScanStartedUtc }).IsUnique();
        model.Entity<PoolScan>().HasIndex(x => new { x.ConnectionId, x.PoolGuid, x.Id });
        model.Entity<PoolScan>().HasIndex(x => x.LastFetchedUtc);
        model.Entity<PoolScan>().HasOne<NasConnection>().WithMany().HasForeignKey(x => x.ConnectionId).OnDelete(DeleteBehavior.Restrict);
        model.Entity<PoolSnapshot>().HasIndex(x => new { x.FetchedUtc, x.Id });
        model.Entity<PoolSnapshot>().HasIndex(x => new { x.ConnectionId, x.PoolGuid, x.FetchedUtc });
        model.Entity<PoolSnapshot>().HasOne<NasConnection>().WithMany().HasForeignKey(x => x.ConnectionId).OnDelete(DeleteBehavior.Restrict);
        model.Entity<PoolSnapshot>().HasIndex(x => new { x.ConnectionId, x.PoolGuid, x.Id });
        model.Entity<AssetDependency>().HasOne<InventoryAsset>().WithMany().HasForeignKey(x => x.SourceId).OnDelete(DeleteBehavior.Restrict);
        model.Entity<AssetDependency>().HasOne<InventoryAsset>().WithMany().HasForeignKey(x => x.TargetId).OnDelete(DeleteBehavior.Restrict);
        model.Entity<AssetDependency>().HasIndex(x => new { x.SourceId, x.TargetId, x.Relationship }).IsUnique();
        model.Entity<NotificationDelivery>().HasIndex(x => x.EventKey).IsUnique();
        model.Entity<NotificationDelivery>().HasOne<Finding>().WithMany().HasForeignKey(x => x.FindingId).OnDelete(DeleteBehavior.Restrict);
        model.Entity<DeliveryAttempt>().HasOne<NotificationDelivery>().WithMany().HasForeignKey(x => x.DeliveryId).OnDelete(DeleteBehavior.Restrict);
        model.Entity<Capability>().HasIndex(x => new { x.ConnectionId, x.Name }).IsUnique();
        model.Entity<NativeAlert>().HasIndex(x => new { x.ConnectionId, x.SourceId }).IsUnique();
        model.Entity<Observation>().HasIndex(x => new { x.SourceKey, x.ConnectionId }).IsUnique();
        model.Entity<Observation>().HasIndex(x => new { x.DiskId, x.Metric, x.ObservedUtc });
        model.Entity<Observation>().HasIndex(x => new { x.DiskId, x.Metric, x.Id });
        model.Entity<Observation>().HasIndex(x => new { x.DiskId, x.Metric, x.ReceivedUtc });
        model.Entity<Disk>().HasIndex(x => new { x.ConnectionId, x.IdentityKey });
        model.Entity<Owner>().HasIndex(x => x.Username).IsUnique();
    }
}
public sealed class Owner { public int Id { get; set; } public string Username { get; set; } = ""; public string PasswordHash { get; set; } = ""; }
public sealed class NasConnection
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public string ApiUri { get; set; } = "";
    public string? ConnectHost { get; set; }
    public int? ConnectPort { get; set; }
    public string ProtectedApiKey { get; set; } = "";
    public string? Version { get; set; }
    public string Status { get; set; } = "Not collected";
    public DateTime? LastSuccessUtc { get; set; }
    public int PollSeconds { get; set; } = 60;
    public bool Enabled { get; set; } = true;
    public string? DiscoveryIdentityJson { get; set; }
    public string? TopologyJson { get; set; }
    public string? CachedTemperaturesJson { get; set; }
    public string? DatasetsJson { get; set; }
    public DateTime? LastAttemptUtc { get; set; }
    public DateTime? NextAttemptUtc { get; set; }
    public int ConsecutiveFailures { get; set; }
    public string? TrustedCaPem { get; set; }
    public string? CertificateSha256 { get; set; }
    public string? SystemSerial { get; set; }
    public string? ObservedSystemSerial { get; set; }
    public string SystemIdentityState { get; set; } = "unavailable";
    public DateTime? SystemIdentityCheckedUtc { get; set; }
    public Guid ConfigurationRevision { get; set; } = Guid.NewGuid();

}
public sealed class Disk
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ConnectionId { get; set; }
    public string IdentityKey { get; set; } = "";
    public string Serial { get; set; } = "";
    public string Model { get; set; } = "";
    public string? Wwn { get; set; }
    public string Alias { get; set; } = "";
    public string MappingConfidence { get; set; } = "inferred";
    public string? Bay { get; set; }
    public bool BayVerified { get; set; }
    public bool Present { get; set; } = true;
    public DateTime FirstSeenUtc { get; set; } = DateTime.UtcNow;
    public DateTime LastSeenUtc { get; set; } = DateTime.UtcNow;
    public DateTime? PurchasedUtc { get; set; }
    public DateTime? WarrantyEndUtc { get; set; }
    public string Notes { get; set; } = "";
    public string Lifecycle { get; set; } = "installed";
    public string PurchaseDateConfidence { get; set; } = "unknown";
    public string? BayEvidence { get; set; }
    public DateTime? BayVerifiedUtc { get; set; }

}
public sealed class Observation
{
    public long Id { get; set; }
    public Guid ConnectionId { get; set; }
    public Guid? DiskId { get; set; }
    public string SourceKey { get; set; } = "";
    public string Metric { get; set; } = "";
    public double? Value { get; set; }
    public string Unit { get; set; } = "";
    public DateTime? ObservedUtc { get; set; }
    public DateTime ReceivedUtc { get; set; } = DateTime.UtcNow;
    public string? RecordedBy { get; set; }
    public string Source { get; set; } = "manual";
    public string Quality { get; set; } = "unverified";
    public string? MissingReason { get; set; }
    public string ResetEpoch { get; set; } = "";
    public string SourceSchema { get; set; } = "manual-v1";
}
public sealed class Finding
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ConnectionId { get; set; }
    public Guid? DiskId { get; set; }
    public string Rule { get; set; } = "";
    public string Severity { get; set; } = "review";
    public string Title { get; set; } = "";
    public string Evidence { get; set; } = "";
    public string NextStep { get; set; } = "";
    public string State { get; set; } = "open";
    public DateTime FirstSeenUtc { get; set; } = DateTime.UtcNow;
    public DateTime LastSeenUtc { get; set; } = DateTime.UtcNow;
    public DateTime? ReviewUtc { get; set; }
    public string? AssignedOwner { get; set; }
    public DateTime? SnoozedUntilUtc { get; set; }
    public DateTime? ResolvedUtc { get; set; }
    public string? ResolutionEvidence { get; set; }
    public DateTime? EvidenceObservedUtc { get; set; }
    public string? EvidenceUnit { get; set; }
    public string? EvidenceSource { get; set; }
    public string? EvidenceEpoch { get; set; }
    public string? EvidenceSchema { get; set; }
    public int HealthySamples { get; set; }
    public int ReminderSequence { get; set; }

}
public sealed class PlanningItem
{
    public Guid Revision { get; set; } = Guid.NewGuid();
    public string Owner { get; set; } = "";
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid? DiskId { get; set; }
    public string Title { get; set; } = "";
    public string Reason { get; set; } = "";
    public string Status { get; set; } = "planned";
    public decimal? Budget { get; set; }
    public string Currency { get; set; } = "USD";
    public DateTime? DueUtc { get; set; }
}
public sealed class RecoveryPolicy
{
    public string DestinationCapacity { get; set; } = "";
    public string RestoreRequirements { get; set; } = "";
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Asset { get; set; } = "";
    public string Destination { get; set; } = "";
    public string Mechanism { get; set; } = "independent backup";
    public Guid Revision { get; set; } = Guid.NewGuid();
    public string Owner { get; set; } = "";
    public string Importance { get; set; } = "important";
    public string Scope { get; set; } = "";
    public string Exclusions { get; set; } = "";
    public string Retention { get; set; } = "";
    public string Schedule { get; set; } = "";
    public string FailureDomains { get; set; } = "unknown";
    public string Independence { get; set; } = "unverified";
    public int RtoHours { get; set; } = 4;
    public string Runbook { get; set; } = "";
    public string KeyCustodyReference { get; set; } = "";

    public int RpoHours { get; set; } = 24;
    public DateTime? RecoveryPointUtc { get; set; }
    public DateTime? RestoreTestUtc { get; set; }
    public string RestoreResult { get; set; } = "unverified";
    public string Evidence { get; set; } = "";
}
public sealed class AuditEvent
{
    public long Id { get; set; }
    public DateTime AtUtc { get; set; } = DateTime.UtcNow;
    public string Actor { get; set; } = "system";
    public string Action { get; set; } = "";
    public string Detail { get; set; } = "";
}

public sealed class OperatingSettings
{
    public int Id { get; set; } = 1;
    public string Timezone { get; set; } = "America/Phoenix";
    public string CapacityUnits { get; set; } = "binary";
    public int DriveAgeReviewYears { get; set; } = 5;
    public int WarrantyNoticeDays { get; set; } = 90;
    public int InventoryMinutes { get; set; } = 15;
    public int SmartMinutes { get; set; } = 90;
    public double? TemperatureReviewCelsius { get; set; }
    public string TemperaturePolicyReason { get; set; } = "";
    public bool ReadCachedTemperatures { get; set; }
    public bool ReadDatasets { get; set; }
    public int SampleRetentionDays { get; set; } = 90;
    public int RawRetentionDays { get; set; } = 14;
    public bool BackupsEnabled { get; set; }
    public int BackupDailyCopies { get; set; } = 7;
    public int BackupWeeklyCopies { get; set; } = 4;
}
public sealed class BackupRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public string FileName { get; set; } = "";
    public string Status { get; set; } = "pending";
    public string? Sha256 { get; set; }
    public long SizeBytes { get; set; }
}

public sealed class DiskAlias
{
    public long Id { get; set; }
    public Guid DiskId { get; set; }
    public string Path { get; set; } = "";
    public DateTime FirstSeenUtc { get; set; } = DateTime.UtcNow;
    public DateTime LastSeenUtc { get; set; } = DateTime.UtcNow;
    public DateTime? EndedUtc { get; set; }
}
public sealed class DiskAnnotation
{
    public long Id { get; set; }
    public Guid DiskId { get; set; }
    public string Actor { get; set; } = "";
    public DateTime AtUtc { get; set; } = DateTime.UtcNow;
    public string BeforeJson { get; set; } = "";
    public string AfterJson { get; set; } = "";
}

public sealed class Capability
{
    public long Id { get; set; }
    public Guid ConnectionId { get; set; }
    public string Name { get; set; } = "";
    public string Status { get; set; } = "unavailable";
    public string Detail { get; set; } = "";
    public DateTime? LastSuccessUtc { get; set; }
    public DateTime? LastAttemptUtc { get; set; }
    public int ExpectedSeconds { get; set; } = 60;
    public string EffectiveStatus(DateTime now) => Status == "supported" && (LastSuccessUtc is null || now - LastSuccessUtc > TimeSpan.FromSeconds(ExpectedSeconds * 3)) ? "stale" : Status;
}
public sealed class NativeAlert
{
    public long Id { get; set; }
    public Guid ConnectionId { get; set; }
    public string SourceId { get; set; } = "";
    public string Level { get; set; } = "";
    public string Class { get; set; } = "";
    public string Message { get; set; } = "";
    public bool DismissedOnNas { get; set; }
    public DateTime FirstSeenUtc { get; set; } = DateTime.UtcNow;
    public DateTime LastSeenUtc { get; set; } = DateTime.UtcNow;
    public DateTime? AbsentSinceUtc { get; set; }
}
public sealed class CollectionRun
{
    public long Id { get; set; }
    public Guid ConnectionId { get; set; }
    public DateTime StartedUtc { get; set; }
    public DateTime EndedUtc { get; set; }
    public string Result { get; set; } = "";
    public string CapabilitySummaryJson { get; set; } = "{}";
}

public sealed class NotificationChannel
{
    public string Kind { get; set; } = "webhook";
    public int Id { get; set; } = 1;
    public Guid Revision { get; set; } = Guid.NewGuid();
    public string ProtectedUrl { get; set; } = "";
    public string Host { get; set; } = "";
    public bool Enabled { get; set; }
    public bool MaskIdentifiers { get; set; } = true;
    public DateTime? TestedUtc { get; set; }
    public DateTime? MaintenanceUntilUtc { get; set; }
}
public sealed class NotificationDelivery
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ChannelRevision { get; set; }
    public Guid FindingId { get; set; }
    public string EventKey { get; set; } = "";
    public string Kind { get; set; } = "opened";
    public string PayloadJson { get; set; } = "{}";
    public string Status { get; set; } = "pending";
    public int Attempts { get; set; }
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public DateTime NextAttemptUtc { get; set; } = DateTime.UtcNow;
    public DateTime? AcceptedUtc { get; set; }
}
public sealed class DeliveryAttempt
{
    public long Id { get; set; }
    public Guid DeliveryId { get; set; }
    public DateTime AtUtc { get; set; } = DateTime.UtcNow;
    public int? HttpStatus { get; set; }
    public string Result { get; set; } = "";
}

public sealed class BackupEvidence
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid PolicyId { get; set; }
    public Guid PolicyRevision { get; set; }
    public DateTime AttemptedUtc { get; set; }
    public string Outcome { get; set; } = "unknown";
    public DateTime? RecoveryPointUtc { get; set; }
    public bool DestinationConfirmed { get; set; }
    public string Evidence { get; set; } = "";
    public string FailureReason { get; set; } = "";
    public string Author { get; set; } = "";
    public DateTime RecordedUtc { get; set; } = DateTime.UtcNow;
    public DateTime ReviewByUtc { get; set; }
}
public sealed class RestoreExercise
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid PolicyId { get; set; }
    public Guid PolicyRevision { get; set; }
    public DateTime TestedUtc { get; set; }
    public DateTime RecoveryPointUtc { get; set; }
    public string Scope { get; set; } = "";
    public string Checks { get; set; } = "";
    public string Result { get; set; } = "unverified";
    public int DurationMinutes { get; set; }
    public string Evidence { get; set; } = "";
    public string Author { get; set; } = "";
    public DateTime RecordedUtc { get; set; } = DateTime.UtcNow;
    public DateTime ReviewByUtc { get; set; }
}

public sealed class InventoryAsset
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Type { get; set; } = "host";
    public string Name { get; set; } = "";
    public string Purpose { get; set; } = "";
    public string Owner { get; set; } = "";
    public string Criticality { get; set; } = "normal";
    public string Location { get; set; } = "";
    public string Version { get; set; } = "";
    public string ManagementUrl { get; set; } = "";
    public string Documentation { get; set; } = "";
    public string Runbook { get; set; } = "";
    public string Verification { get; set; } = "unverified";
    public int Quantity { get; set; } = 1;
    public string CompatibilityNotes { get; set; } = "";
    public decimal? EstimatedUnitCost { get; set; }
    public string Currency { get; set; } = "USD";
    public bool Archived { get; set; }
    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;
}
public sealed class AssetDependency
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SourceId { get; set; }
    public Guid TargetId { get; set; }
    public string Relationship { get; set; } = "runs_on";
    public string Verification { get; set; } = "unverified";
    public string Evidence { get; set; } = "";
    public string Author { get; set; } = "";
    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;
}
