using System.Text.Json;
namespace Homelab.Api;

public record VdevDriveMapping(string Status,Guid? DiskId,string? Serial,string? Wwn,string? Model,string Explanation);
public static class VdevMapping
{
    private static readonly JsonSerializerOptions Options=new(JsonSerializerDefaults.Web);
    public static string Attach(string evidence,IReadOnlyList<IdentityCandidate>? inventory,IReadOnlyList<Disk> disks)
    {
        var source=VdevEvidence.Read(evidence);
        if(source.Status!="reported")return evidence;
        VdevDriveMapping Unknown(string reason,string status="unavailable")=>new(status,null,null,null,null,reason);
        VdevDriveMapping Map(VdevNodeEvidence node) {
            if(node.Type!="DISK" || source.Nodes.Any(x=>x.ParentGuid==node.Guid))return Unknown("This ZFS object is not a physical disk leaf.","not_applicable");
            if(inventory is null)return Unknown("No fresh disk inventory was read in this collection cycle. Older aliases are not used to infer a current match.");
            if(node.State!="ONLINE")return Unknown("The member was not reported ONLINE; a current physical-drive match is not established.");
            if(string.IsNullOrWhiteSpace(node.DiskAlias) || string.IsNullOrWhiteSpace(node.Device) || node.Path is null || !node.Path.StartsWith("/dev/",StringComparison.Ordinal))return Unknown("The source did not report a complete path → device → disk alias chain. Partition suffixes are never guessed.");
            var candidates=inventory.Where(x=>x.Alias==node.DiskAlias).ToArray();
            if(candidates.Length!=1)return Unknown(candidates.Length==0?"The reported disk alias is absent from this inventory.":"Multiple inventory rows claim this alias.",candidates.Length==0?"unavailable":"ambiguous");
            var candidate=candidates[0];
            if(candidate.Key is null)return Unknown("The inventory row lacks a usable stable identity.","ambiguous");
            var matches=disks.Where(x=>x.Present && (IdentityCandidate.NormalizeWwn(candidate.Wwn) is {} wwn && IdentityCandidate.NormalizeWwn(x.Wwn)==wwn || candidate.HasSerial && x.Serial.Trim()==candidate.Serial.Trim() && x.Model.Trim()==candidate.Model.Trim())).ToArray();
            var reviewed=matches.Where(x=>x.MappingConfidence=="reviewed" && IdentityCandidate.NormalizeWwn(candidate.Wwn) is {} wwn && IdentityCandidate.NormalizeWwn(x.Wwn)==wwn && x.Serial.Trim()==candidate.Serial.Trim() && x.Model.Trim()==candidate.Model.Trim()).ToArray();
            if(reviewed.Length==1)matches=reviewed;
            if(matches.Length!=1 || matches[0].MappingConfidence=="ambiguous")return Unknown("The physical identity is unresolved or has conflicting matches.","ambiguous");
            var disk=matches[0];
            if(IdentityCandidate.NormalizeWwn(candidate.Wwn) is {} current && IdentityCandidate.NormalizeWwn(disk.Wwn) is {} prior && current!=prior)return Unknown("Reported and retained WWNs conflict.","ambiguous");
            return new("inferred",disk.Id,disk.Serial,IdentityCandidate.NormalizeWwn(disk.Wwn),disk.Model,
                "Source-reported partition/device → disk alias matched one reconciled identity in the same collection cycle. Reads are sequential, not atomic; this is inferred historical evidence, not physical bay verification or permission to remove a drive.");
        }
        var result=JsonSerializer.Serialize(source with {Nodes=source.Nodes.Select(x=>x with {DriveMapping=Map(x)}).ToArray()},Options);
        // Preserve the bounded counter/tree evidence if enrichment would exceed its storage budget.
        return result.Length<=VdevEvidence.MaximumLength?result:evidence;
    }
}
