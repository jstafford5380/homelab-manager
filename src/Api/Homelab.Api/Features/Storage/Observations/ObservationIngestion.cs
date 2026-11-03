using Microsoft.EntityFrameworkCore;

namespace Homelab.Api;
public static class ObservationIngestion
{
    // Caller validates and deduplicates; one SaveChanges commits the full evidence batch.
    public static async Task StageAsync(HomelabDb db, Disk disk, Observation input, string actor)
    {
        var previous = await db.Observations.Where(x => x.DiskId == disk.Id && x.Metric == input.Metric && x.Source == input.Source).OrderByDescending(x => x.ObservedUtc).FirstOrDefaultAsync();
        input.Id = 0;
        input.DiskId = disk.Id;
        input.ConnectionId = disk.ConnectionId;
        input.ReceivedUtc = DateTime.UtcNow;
        input.RecordedBy = actor;
        db.Observations.Add(input);
        // Different import sources can describe the same drive at different times.
        // Keep backfilled evidence, but do not replay it as a new lifecycle event.
        var newerOrEqualEvidence = input.ObservedUtc is not null && await db.Observations.AnyAsync(x => x.DiskId == disk.Id && x.Metric == input.Metric && x.Unit == input.Unit && x.Quality == "observed" && x.Value != null && x.ObservedUtc >= input.ObservedUtc);
        db.Activity.Add(new() { Actor = actor, Action = "Disk evidence imported", Detail = $"Disk {disk.Id}; source key {input.SourceKey}; metric {input.Metric}" + (newerOrEqualEvidence ? "; historical or same-time evidence retained without changing finding state" : "") });
        if (newerOrEqualEvidence)
            return;
        var settings = await db.Settings.SingleOrDefaultAsync() ?? new OperatingSettings();
        var rule = HealthRules.Evaluate(input, previous) ?? TemperaturePolicy.Evaluate(input, settings);
        if (rule is not null)
        {
            var finding = await db.Findings.FirstOrDefaultAsync(x => x.DiskId == disk.Id && x.Rule == rule.Key && x.State != "resolved");
            if (finding is null)
            {
                finding = new Finding
                {
                    ConnectionId = disk.ConnectionId,
                    DiskId = disk.Id,
                    Rule = rule.Key
                };
                db.Findings.Add(finding);
            }

            // A newly imported historical source must not replace newer finding evidence.
            if (finding.EvidenceObservedUtc is null || input.ObservedUtc >= finding.EvidenceObservedUtc)
            {
                finding.Title = rule.Title;
                finding.Evidence = rule.Evidence;
                finding.NextStep = rule.NextStep;
                finding.Severity = rule.Severity;
                finding.LastSeenUtc = DateTime.UtcNow;
                finding.EvidenceUnit = input.Unit;
                finding.EvidenceObservedUtc = input.ObservedUtc;
                finding.EvidenceSource = input.Source;
                finding.EvidenceEpoch = input.ResetEpoch;
                finding.EvidenceSchema = input.SourceSchema;
                finding.HealthySamples = 0;
            }
        }

        var candidates = await db.Findings.Where(f => f.DiskId == disk.Id && f.State != "resolved").ToListAsync();
        var freshnessMinutes = settings.SmartMinutes;
        foreach (var finding in candidates)
            if (FindingLifecycle.CanResolvePresence(finding, input, previous, DateTime.UtcNow, TimeSpan.FromMinutes(freshnessMinutes * 3)))
            {
                FindingLifecycle.Resolve(finding, DateTime.UtcNow, $"Two later zero observations from {input.Source}, epoch {input.ResetEpoch}, at {previous!.ObservedUtc:O} and {input.ObservedUtc:O}. Historical adverse observations remain retained.");
                db.Activity.Add(new() { Action = "Finding resolved by later evidence", Detail = finding.Id.ToString() });
            }
    }
}
