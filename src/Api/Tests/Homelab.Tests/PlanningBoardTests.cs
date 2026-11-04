using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Homelab.Api;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Homelab.Tests;

public sealed class PlanningBoardTests
{
    [Fact]
    public async Task EvidenceAndNotesPersistSeparatelyAndCompletionIsServerOwned()
    {
        using var factory = new ApiFactory();
        using var client = await Owner(factory);
        var response = await client.PostAsJsonAsync("/api/v1/plans", new {
            title = "Review recorded evidence", notes = "Check cable", owner = "Jess",
            evidence = new[] { new PlanningReason("finding", "Observed checksum errors", Summary: "3 checksum errors") },
            completedUtc = DateTime.UtcNow.AddYears(-1)
        });
        response.EnsureSuccessStatusCode();
        var plan = (await response.Content.ReadFromJsonAsync<PlanningItem>())!;
        Assert.Equal("Check cable", plan.Notes);
        Assert.Equal("Observed checksum errors", Assert.Single(plan.Evidence).Detail);
        Assert.Null(plan.CompletedUtc);
        plan.Status = "complete";
        var done = await client.PutAsJsonAsync($"/api/v1/plans/{plan.Id}", plan);
        done.EnsureSuccessStatusCode();
        plan = (await done.Content.ReadFromJsonAsync<PlanningItem>())!;
        Assert.InRange(plan.CompletedUtc!.Value, DateTime.UtcNow.AddMinutes(-1), DateTime.UtcNow);
        var completed = plan.CompletedUtc;
        plan.Notes = "Updated notes";
        plan.Evidence = [new("finding", "Attempted evidence replacement")];
        plan.CompletedUtc = DateTime.UtcNow.AddYears(-1);
        var edit = await client.PutAsJsonAsync($"/api/v1/plans/{plan.Id}", plan);
        edit.EnsureSuccessStatusCode();
        plan = (await edit.Content.ReadFromJsonAsync<PlanningItem>())!;
        Assert.Equal(completed, plan.CompletedUtc);
        Assert.Equal("Observed checksum errors", Assert.Single(plan.Evidence).Detail);
        Assert.Equal("Updated notes", plan.Notes);
        plan.Status = "planned";
        var reopened = await client.PutAsJsonAsync($"/api/v1/plans/{plan.Id}", plan);
        reopened.EnsureSuccessStatusCode();
        Assert.Null((await reopened.Content.ReadFromJsonAsync<PlanningItem>())!.CompletedUtc);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HomelabDb>();
        Assert.Equal(4, await db.PlanChanges.CountAsync(x => x.PlanId == plan.Id));
        Assert.Contains("Observed checksum errors", (await db.Plans.SingleAsync()).EvidenceJson);
    }

    [Fact]
    public async Task SummaryUsesOnlyCurrentCompatibleChecksAndSplitsSpendByStatus()
    {
        using var factory = new ApiFactory();
        using var client = await Owner(factory);
        var now = DateTime.UtcNow;
        Disk first, second;
        InventoryAsset spare;
        using (var scope = factory.Services.CreateScope()) {
            var db = scope.ServiceProvider.GetRequiredService<HomelabDb>();
            var nas = new NasConnection { Name = "Fixture", Enabled = false };
            first = new Disk { ConnectionId = nas.Id, Model = "Disk", Serial = "FIRST", Nickname = "Primary", Bay = "Bay 1" };
            second = new Disk { ConnectionId = nas.Id, Model = "Disk", Serial = "SECOND" };
            spare = new InventoryAsset { Name = "Cold spare", Type = "spare", Quantity = 1, UpdatedUtc = now, EstimatedUnitCost = 100 };
            db.Connections.Add(nas); db.Disks.AddRange(first, second); db.Assets.Add(spare);
            db.Plans.AddRange(new PlanningItem { DiskId = first.Id, Title = "One", Status = "ordered", Budget = 100 }, new PlanningItem { Title = "Two", Budget = 50 }, new PlanningItem { Title = "Three" }, new PlanningItem { Title = "Excluded", Status = "complete", Budget = 900 });
            await db.SaveChangesAsync();
            foreach (var disk in new[] { first, second }) db.SpareReviews.Add(new() { RequestId = Guid.NewGuid(), DiskId = disk.Id, AssetId = spare.Id, AssetName = spare.Name, AssetUpdatedUtc = now, ConfirmedIdentity = disk.Serial, DiskModel = disk.Model, Verdict = "compatible", ValidUntilUtc = now.AddDays(30) });
            await db.SaveChangesAsync();
            // A newer incompatible assessment supersedes the old compatible assessment.
            db.SpareReviews.Add(new() { RequestId = Guid.NewGuid(), DiskId = second.Id, AssetId = spare.Id, AssetName = spare.Name, AssetUpdatedUtc = now, ConfirmedIdentity = second.Serial, DiskModel = second.Model, Verdict = "incompatible", ValidUntilUtc = now.AddDays(30) });
            await db.SaveChangesAsync();
        }
        var result = await client.GetFromJsonAsync<JsonElement>("/api/v1/planning/review");
        Assert.Equal(1, result.GetProperty("spares")[0].GetProperty("compatibleDriveCount").GetInt32());
        var reviews = result.GetProperty("reviews").EnumerateArray().ToArray();
        var primary = reviews.Single(x => x.GetProperty("diskId").GetGuid() == first.Id);
        Assert.Equal("Primary", primary.GetProperty("label").GetString());
        Assert.Equal("Bay 1", primary.GetProperty("location").GetString());
        Assert.Equal("ordered", primary.GetProperty("openPlan").GetProperty("status").GetString());
        Assert.Equal("compatible", primary.GetProperty("spareFit").GetProperty("status").GetString());
        Assert.Equal("incompatible", reviews.Single(x => x.GetProperty("diskId").GetGuid() == second.Id).GetProperty("spareFit").GetProperty("status").GetString());
        var budget = result.GetProperty("budgets")[0];
        Assert.Equal(100, budget.GetProperty("ordered").GetDecimal()); Assert.Equal(50, budget.GetProperty("planned").GetDecimal()); Assert.Equal(1, budget.GetProperty("withoutEstimate").GetInt32());
        using (var scope = factory.Services.CreateScope()) {
            var db = scope.ServiceProvider.GetRequiredService<HomelabDb>();
            (await db.Assets.SingleAsync()).UpdatedUtc = now.AddSeconds(1); await db.SaveChangesAsync();
        }
        var invalidated = await client.GetFromJsonAsync<JsonElement>("/api/v1/planning/review");
        Assert.Equal(0, invalidated.GetProperty("spares")[0].GetProperty("compatibleDriveCount").GetInt32());
        Assert.All(invalidated.GetProperty("reviews").EnumerateArray(), x => Assert.Equal("unchecked", x.GetProperty("spareFit").GetProperty("status").GetString()));
    }

    [Theory]
    [InlineData(true, false, false, false)]
    [InlineData(true, false, true, true)]
    [InlineData(true, true, false, true)]
    [InlineData(false, false, false, true)]
    public async Task ReplacedAbsentDrivesAreHistoricalUnlessReplacementIsVoidedOrDriveReturns(
        bool recordedReplacement, bool present, bool voided, bool expectedReview)
    {
        using var factory = new ApiFactory();
        using var client = await Owner(factory);
        Guid oldId;
        using (var scope = factory.Services.CreateScope()) {
            var db = scope.ServiceProvider.GetRequiredService<HomelabDb>();
            var nas = new NasConnection { Name = "Replacement fixture", Enabled = false };
            var oldDisk = new Disk { ConnectionId = nas.Id, Serial = "REPLACED", Present = present };
            var newDisk = new Disk { ConnectionId = nas.Id, Serial = "REPLACEMENT", PurchasedUtc = DateTime.UtcNow, PurchaseDateConfidence = "documented" };
            oldId = oldDisk.Id;
            db.Connections.Add(nas); db.Disks.AddRange(oldDisk, newDisk);
            // Historical evidence and open plans must be preserved, without bringing
            // an already-replaced absent drive back into the active review queue.
            db.Findings.Add(new() { ConnectionId = nas.Id, DiskId = oldId, Title = "Recorded media evidence", Rule = "media-present-v1:ata.pending_sectors" });
            db.Plans.Add(new() { DiskId = oldId, Title = "Historical replacement task" });
            if (recordedReplacement) db.Replacements.Add(new() {
                OldDiskId = oldId, NewDiskId = newDisk.Id, OccurredUtc = DateTime.UtcNow.AddDays(-1),
                VoidedUtc = voided ? DateTime.UtcNow : null
            });
            await db.SaveChangesAsync();
        }
        var result = await client.GetFromJsonAsync<JsonElement>("/api/v1/planning/review");
        var reviews = result.GetProperty("reviews").EnumerateArray().ToArray();
        Assert.Equal(expectedReview, reviews.Any(x => x.GetProperty("diskId").GetGuid() == oldId));
        Assert.Equal(expectedReview ? 2 : 1, result.GetProperty("trackedDriveCount").GetInt32());
        if (expectedReview) Assert.Equal(!present, reviews.Single().GetProperty("reasons").EnumerateArray().Any(x => x.GetProperty("kind").GetString() == "absent"));
        using var check = factory.Services.CreateScope();
        var stored = check.ServiceProvider.GetRequiredService<HomelabDb>();
        Assert.True(await stored.Disks.AnyAsync(x => x.Id == oldId));
        Assert.True(await stored.Findings.AnyAsync(x => x.DiskId == oldId));
        Assert.True(await stored.Plans.AnyAsync(x => x.DiskId == oldId));
    }

    private static async Task<HttpClient> Owner(ApiFactory factory)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Homelab-Request", "1");
        var credentials = new { username = "owner", password = "planning-board-test-password", setupToken = "refactor-test" };
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/v1/setup", credentials)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/v1/login", credentials)).StatusCode);
        return client;
    }
}
