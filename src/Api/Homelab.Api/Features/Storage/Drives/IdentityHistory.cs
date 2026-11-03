using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace Homelab.Api;
public static class IdentityHistory
{
    public static string SubmissionHash(IdentityMergeInput input) => Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(input)));
    public static string Confirmation(Disk disk) => DrivePaths.Identity(disk) ?? disk.Id.ToString();
    public static async Task<IdentityHistoryGraph> Graph(HomelabDb db, Guid nas) => new(await db.Disks.AsNoTracking().Where(x => x.ConnectionId == nas).ToArrayAsync(), await db.IdentityMerges.AsNoTracking().Where(x => x.ConnectionId == nas).ToArrayAsync());
}
