namespace Homelab.Api.Features.Storage.Drives.GetDrive;
public sealed record Response(Disk Disk, List<DiskAlias> Aliases, List<DiskAnnotation> Annotations, List<Finding> Findings, List<PlanningItem> Plans);
