namespace Homelab.Api.Features.Storage.Smart.ListReports;
public sealed record ResponseItem(Guid Id, string Actor, DateTime ImportedUtc, SmartReportPreview? Report);
