using Microsoft.EntityFrameworkCore;

namespace Homelab.Api;
public static class Planning
{
    public const string RuleVersion = "drive-planning-v1";
    public static DriveReview? Review(Disk disk, IEnumerable<Finding> findings, IEnumerable<PlanningItem> plans, OperatingSettings settings, DateTime now)
    {
        if (disk.Lifecycle == "retired")
            return null;
        var reasons = new List<PlanningReason>();
        var active = findings.Where(f => f.DiskId == disk.Id && f.State != "resolved").ToArray();
        var media = active.Any(f => f.Rule.StartsWith("media-present-v1:", StringComparison.Ordinal) || f.Rule.StartsWith("media-increase-v1:", StringComparison.Ordinal));
        foreach (var finding in active)
            reasons.Add(new("finding", $"{finding.Title}: {finding.Evidence} Last recorded {finding.LastSeenUtc:O}; state {finding.State}. {FindingAdvice(finding.Rule)}".Trim(), finding.Id, finding.Title));
        if (disk.PurchasedUtc is not { } purchased || disk.PurchaseDateConfidence == "unknown")
            reasons.Add(new("age-unknown", "Purchase date is unknown. Add a receipt or an explicitly estimated date; first discovery is not purchase or installation date."));
        else if (purchased <= now.AddYears(-settings.DriveAgeReviewYears))
            reasons.Add(new("age-review", $"{disk.PurchaseDateConfidence} purchase date {purchased:yyyy-MM-dd} exceeds the configured {settings.DriveAgeReviewYears}-year planning review threshold. Age alone is not evidence of failure or a reason to replace a working drive.", PurchasedUtc: purchased, Confidence: disk.PurchaseDateConfidence));
        if (disk.WarrantyEndUtc is { } warranty && warranty.Date <= now.Date.AddDays(settings.WarrantyNoticeDays))
            reasons.Add(new("warranty", warranty.Date < now.Date ? $"Recorded warranty ended {warranty:yyyy-MM-dd}. Confirm terms and purchase records before budgeting." : $"Recorded warranty ends {warranty:yyyy-MM-dd}, within the configured {settings.WarrantyNoticeDays}-day notice window. Review coverage and purchase records.", EndsUtc: warranty));
        if (!disk.Present)
            reasons.Add(new("absent", "This drive was absent from the last inventory snapshot. Verify its current location and identity before planning a physical action."));
        if (disk.MappingConfidence == "ambiguous")
            reasons.Add(new("ambiguous", "Identity is ambiguous. Resolve conflicting identifiers before associating replacement evidence with a physical drive."));
        if (reasons.Count == 0)
            return null;
        return new(disk.Id, disk.Model, disk.Serial, media ? "Review media evidence" : active.Length > 0 ? "Review observed issue" : "Prepare ahead", media ? "Review current disk evidence and recovery readiness, then confirm replacement compatibility and budget. No failure date can be inferred." : active.Any(f => f.Rule == SelfTestFindings.Rule) ? "Review imported self-test failure details, current native evidence and independent recovery readiness. Historical failures do not establish when a drive will fail or mandate replacement." : active.Any(f => f.Rule == "nvme-warning-v1") ? "Review device-reported NVMe warnings and recovery readiness before deciding on replacement. Warning flags alone are not a failure-date prediction." : active.Any(f => f.Rule == "nvme-endurance-v1") ? "Review the manufacturer's endurance guidance, workload and spare budget. Rated usage is not a failure probability or an automatic replacement instruction." : active.Length > 0 ? "Review the linked evidence and affected hardware path. A transport error alone does not establish failed media." : "Confirm inventory and warranty records; consider spare stock and budget. This is a planning reminder, not a failure alert.", disk.LastSeenUtc, disk.Present, disk.MappingConfidence, reasons.ToArray(), plans.Where(p => p.DiskId == disk.Id && p.Status != "complete").Select(p => p.Id).ToArray())
        {
            Label = disk.Nickname,
            OpenPlan = plans.Where(p => p.DiskId == disk.Id && p.Status != "complete").OrderBy(p => p.DueUtc ?? DateTime.MaxValue).ThenBy(p => p.Id).Select(p => new ReviewOpenPlan(p.Id, p.Status)).FirstOrDefault()
        };
    }

    private static string FindingAdvice(string rule) => rule switch {
        SelfTestFindings.Rule => "Review imported self-test failure details, current native evidence and independent recovery readiness. Historical failures do not establish when a drive will fail or mandate replacement.",
        "nvme-warning-v1" => "Review device-reported NVMe warnings and recovery readiness before deciding on replacement. Warning flags alone are not a failure-date prediction.",
        "nvme-endurance-v1" => "Review the manufacturer's endurance guidance, workload and spare budget. Rated usage is not a failure probability or an automatic replacement instruction.",
        _ => ""
    };

    public static string? ValidatePlan(PlanningItem item)
    {
        if (string.IsNullOrWhiteSpace(item.Title) || item.Title.Length > 200 || item.Reason is null || item.Reason.Length > 20000)
            return "Enter a title (up to 200 characters) and evidence (up to 20,000 characters).";
        if (item.Evidence is null || item.Evidence.Length > 100 || item.Evidence.Any(x => x is null || string.IsNullOrWhiteSpace(x.Kind) || x.Kind.Length > 50 || x.Detail is null || x.Detail.Length > 20000 || x.Summary?.Length > 1000))
            return "Attach at most 100 evidence items with bounded text.";
        if (item.Budget is < 0 or > 1000000000 || item.Currency is null || item.Currency.Length != 3 || item.Currency.Any(c => c is < 'A' or > 'Z'))
            return "Use a nonnegative budget up to one billion and a three-letter uppercase currency code.";
        if (item.Owner is null || item.Owner.Length > 200)
            return "Owner must be at most 200 characters.";
        if (item.DueUtc is { } due && (due.Kind != DateTimeKind.Utc || due < DateTime.UnixEpoch))
            return "Review time must be a UTC timestamp ending in Z, no earlier than 1970.";
        if (item.Status is not ("planned" or "ordered" or "complete"))
            return "Choose a supported plan status.";
        return null;
    }
}
