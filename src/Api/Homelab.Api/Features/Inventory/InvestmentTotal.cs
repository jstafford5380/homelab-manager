using Microsoft.EntityFrameworkCore;

namespace Homelab.Api;

public record InvestmentTotal(string Currency, decimal Amount)
{
    public static InvestmentTotal[] Sum(IEnumerable<InvestmentTotal> purchases) =>
        purchases.GroupBy(p => p.Currency.ToUpperInvariant())
            .OrderBy(g => g.Key)
            .Select(g => new InvestmentTotal(g.Key, g.Sum(p => p.Amount)))
            .ToArray();

    public static InvestmentTotal[] Storage(IEnumerable<Disk> disks, IEnumerable<DriveAcquisition> acquisitions)
    {
        var installed = disks.Where(disk => disk.Lifecycle == "installed").Select(disk => disk.Id).ToHashSet();
        return Sum(acquisitions.Where(a => a.PurchaseCost.HasValue && installed.Contains(a.DiskId))
            .Select(a => new InvestmentTotal(a.Currency, a.PurchaseCost!.Value)));
    }

    public static async Task<InvestmentTotal[]> Hardware(HomelabDb db, CancellationToken ct)
    {
        var amounts = await db.HardwareComponents.AsNoTracking()
            .Where(c => c.RetiredUtc == null && c.PricePaid != null).Select(c => c.PricePaid!.Value).ToArrayAsync(ct);
        return amounts.Length == 0 ? [] : [new("USD", amounts.Sum())];
    }
}
