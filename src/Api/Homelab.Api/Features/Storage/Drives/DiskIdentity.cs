using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace Homelab.Api;
public static class DiskIdentity
{
    public static async Task ReconcileAsync(HomelabDb db, Guid nas, IReadOnlyList<IdentityCandidate> rows, DateTime at, CancellationToken ct, Guid? revision = null)
    {
        var existing = await db.Disks.Where(x => x.ConnectionId == nas).ToListAsync(ct);
        var aliases = await db.DiskAliases.Where(x => existing.Select(d => d.Id).Contains(x.DiskId) && x.EndedUtc == null).ToListAsync(ct);
        var decisions = revision is null ? [] : await db.DiscoveryAssignments.Where(x => x.ConnectionId == nas && x.ConfigurationRevision == revision && x.RevokedUtc == null && x.ValidUntilUtc > at).ToListAsync(ct);
        var reviewedTargets = new HashSet<Guid>();
        foreach (var disk in existing)
        {
            disk.Present = false;
            if (disk.MappingConfidence == "reviewed")
                disk.MappingConfidence = "inferred";
        }

        var seen = new HashSet<Guid>();
        foreach (var group in rows.GroupBy(r => r.Key))
        {
            var row = group.First();
            var wwn = IdentityCandidate.NormalizeWwn(row.Wwn);
            // Multipath requires one WWN and compatible serial/model evidence. Serial-only duplicates are ambiguous.
            var ambiguous = group.Key is null || group.Count() > 1 && (wwn is null || group.Select(r => (r.Serial.Trim(), r.Model.Trim())).Distinct().Count() != 1);
            var matches = existing.Where(d => wwn is not null && IdentityCandidate.NormalizeWwn(d.Wwn) == wwn || row.HasSerial && d.Serial.Trim() == row.Serial.Trim() && d.Model.Trim() == row.Model.Trim()).ToArray();
            ambiguous |= matches.Length > 1 || matches.Any(d => wwn is not null && IdentityCandidate.NormalizeWwn(d.Wwn)is { } prior && prior != wwn);
            var aliasConflict = !DiscoveryAssignments.AliasConsistent(group, rows);
            ambiguous |= aliasConflict;
            var reviewed = !aliasConflict && revision is { } rev ? decisions.Where(x => x.Wwn == wwn).Select(x => DiscoveryAssignments.Target(x, group, existing, rev, at)).OfType<Disk>().DistinctBy(x => x.Id).ToArray() : [];
            if (reviewed.Length == 1)
            {
                matches = reviewed;
                ambiguous = false;
                reviewed[0].MappingConfidence = "reviewed";
                reviewedTargets.Add(reviewed[0].Id);
                var finding = await db.Findings.FirstOrDefaultAsync(x => x.ConnectionId == nas && x.Rule == "identity-v2:" + group.Key && x.State != "resolved", ct);
                if (finding is not null)
                {
                    FindingLifecycle.Resolve(finding, at, "A recorded stable-WWN assignment matched current inventory. Original histories and physical bay verification are unchanged.");
                    db.Activity.Add(new() { Action = "Discovery identity review matched inventory", Detail = finding.Id.ToString() });
                }
            }

            if (ambiguous)
            {
                var rule = "identity-v2:" + (group.Key ?? "missing");
                if (!db.Findings.Local.Any(f => f.ConnectionId == nas && f.Rule == rule && f.State != "resolved") && !await db.Findings.AnyAsync(f => f.ConnectionId == nas && f.Rule == rule && f.State != "resolved", ct))
                    db.Findings.Add(new() { ConnectionId = nas, Rule = rule, Title = "Disk identity needs review", Evidence = JsonSerializer.Serialize(group), NextStep = "Compare serial/WWN evidence with physical labels. No histories were merged.", Severity = "review" });
                foreach (var match in matches.Where(x => !reviewedTargets.Contains(x.Id)))
                {
                    match.MappingConfidence = "ambiguous";
                    match.Present = true;
                    seen.Add(match.Id);
                }

                continue;
            }

            var disk = matches.SingleOrDefault();
            if (disk is null)
            {
                disk = new Disk
                {
                    ConnectionId = nas,
                    IdentityKey = group.Key!,
                    Serial = row.Serial,
                    Model = row.Model,
                    Wwn = row.Wwn,
                    FirstSeenUtc = at
                };
                db.Disks.Add(disk);
                existing.Add(disk);
            }

            if (disk.Wwn is null && wwn is not null)
                disk.Wwn = row.Wwn;
            disk.Present = true;
            disk.LastSeenUtc = at;
            disk.Alias = row.Alias;
            seen.Add(disk.Id);
            foreach (var path in group.Select(r => r.Alias).Distinct())
            {
                var alias = aliases.SingleOrDefault(a => a.DiskId == disk.Id && a.Path == path);
                if (alias is null)
                {
                    alias = new DiskAlias
                    {
                        DiskId = disk.Id,
                        Path = path,
                        FirstSeenUtc = at
                    };
                    db.DiskAliases.Add(alias);
                    aliases.Add(alias);
                }

                alias.LastSeenUtc = at;
            }
        }

        foreach (var alias in aliases.Where(a => a.LastSeenUtc != at && (!seen.Contains(a.DiskId) || existing.Single(d => d.Id == a.DiskId).MappingConfidence != "ambiguous")))
            alias.EndedUtc = at;
    }
}
