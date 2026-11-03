using Homelab.Api;
using Microsoft.EntityFrameworkCore;
namespace Homelab.Tests;

// Old-schema tests must insert only fields present at their migration boundary.
// Using today's full Disk INSERT would require future columns before upgrading.
internal static class LegacyDiskFixture
{
    public static async Task SaveAsync(HomelabDb db)
    {
        var upgraded = (await db.Database.GetAppliedMigrationsAsync()).Any(x => x.EndsWith("_StorageDriveProfile"));
        if (!upgraded) {
            if (db.ChangeTracker.Entries<NasConnection>().Any(x => x.State == EntityState.Added)) {
                var pending = db.ChangeTracker.Entries().Where(x => x.Entity is not NasConnection && x.State == EntityState.Added).ToArray();
                foreach (var entry in pending) entry.State = EntityState.Unchanged;
                await db.SaveChangesAsync();
                foreach (var entry in pending) entry.State = EntityState.Added;
            }
            foreach (var entry in db.ChangeTracker.Entries<Disk>().Where(x => x.State == EntityState.Added).ToArray()) {
                var d = entry.Entity;
                await db.Database.ExecuteSqlInterpolatedAsync($"""
                    INSERT INTO Disks (Id,ConnectionId,IdentityKey,Serial,Model,Wwn,Alias,MappingConfidence,Bay,BayVerified,Present,
                      FirstSeenUtc,LastSeenUtc,PurchasedUtc,WarrantyEndUtc,Notes,Lifecycle,PurchaseDateConfidence,BayEvidence,BayVerifiedUtc)
                    VALUES ({d.Id},{d.ConnectionId},{d.IdentityKey},{d.Serial},{d.Model},{d.Wwn},{d.Alias},{d.MappingConfidence},
                      {d.Bay},{d.BayVerified},{d.Present},{d.FirstSeenUtc},{d.LastSeenUtc},{d.PurchasedUtc},{d.WarrantyEndUtc},
                      {d.Notes},{d.Lifecycle},{d.PurchaseDateConfidence},{d.BayEvidence},{d.BayVerifiedUtc})
                    """);
                entry.State = EntityState.Unchanged;
            }
        }
        await db.SaveChangesAsync();
    }
}
