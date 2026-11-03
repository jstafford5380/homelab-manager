
namespace Homelab.Api;
public static class Replacements
{
    public static string Identity(Disk disk) => !string.IsNullOrWhiteSpace(disk.Serial) ? disk.Serial : disk.Wwn ?? "";
    public static string? Validate(Disk oldDisk, Disk newDisk, ReplacementInput input, DateTime now)
    {
        if (oldDisk.Id != input.OldDiskId || newDisk.Id != input.NewDiskId || oldDisk.Id == newDisk.Id || oldDisk.ConnectionId != newDisk.ConnectionId)
            return "Choose two different physical drives from the same NAS.";
        if (Identity(oldDisk) == Identity(newDisk) || oldDisk.MappingConfidence == "ambiguous" || newDisk.MappingConfidence == "ambiguous" || string.IsNullOrWhiteSpace(Identity(oldDisk)) || string.IsNullOrWhiteSpace(Identity(newDisk)))
            return "Resolve ambiguous or missing drive identity before recording a replacement.";
        if (input.ConfirmedOldIdentity != Identity(oldDisk) || input.ConfirmedNewIdentity != Identity(newDisk))
            return "Retype each drive's serial (or WWN when no serial is available) to confirm the link.";
        if (input.OccurredUtc.Kind != DateTimeKind.Utc || input.OccurredUtc > now || input.OccurredUtc < new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc) || input.DateConfidence is not ("documented" or "estimated"))
            return "Enter a past replacement time with a UTC offset and indicate whether it is documented or estimated.";
        if (string.IsNullOrWhiteSpace(input.Reason) || input.Reason.Length > 2000 || string.IsNullOrWhiteSpace(input.Evidence) || input.Evidence.Length > 10000 || string.IsNullOrWhiteSpace(input.MembershipEvidence) || input.MembershipEvidence.Length > 4000 || input.PoolGuid is null || input.PoolGuid.Length > 100)
            return "Provide a reason, operator evidence and pool/vdev membership context within the supported lengths. State explicitly when membership is unknown.";
        return null;
    }

    public static DiskReplacement Create(Disk oldDisk, Disk newDisk, ReplacementInput input, string author, DateTime now)
    {
        if (Validate(oldDisk, newDisk, input, now)is { } error)
            throw new ArgumentException(error);
        return new()
        {
            OldDiskId = oldDisk.Id,
            NewDiskId = newDisk.Id,
            OccurredUtc = input.OccurredUtc,
            DateConfidence = input.DateConfidence,
            PoolGuid = input.PoolGuid,
            MembershipEvidence = input.MembershipEvidence,
            Reason = input.Reason,
            Evidence = input.Evidence,
            OldIdentity = Identity(oldDisk),
            NewIdentity = Identity(newDisk),
            Author = author,
            RecordedUtc = now
        };
    }
}
