using Homelab.Api;
using Microsoft.EntityFrameworkCore;
using Microsoft.Data.Sqlite;
using System.Text;
using System.Text.Json;
namespace Homelab.Tests;
public class InventoryTests
{
    [Fact] public async Task ExportsExcludeCredentialsAndRedactedExportOmitsPrivateNotes()
    {
        using var connection = new SqliteConnection("Data Source=:memory:"); connection.Open();
        await using var db = new HomelabDb(new DbContextOptionsBuilder<HomelabDb>().UseSqlite(connection).Options); db.Database.EnsureCreated();
        db.Owners.Add(new() { Username = "owner-secret", PasswordHash = "password-secret" });
        db.Connections.Add(new() { Name = "nas-secret", ProtectedApiKey = "api-secret", ApiUri = "wss://private.example/websocket" });
        db.NotificationChannels.Add(new() { ProtectedUrl = "webhook-secret" });
        db.Assets.Add(new() { Name = "private-host", Runbook = "private-runbook", Location = "private-location", Type = "host" });
        db.Disks.Add(new() { ConnectionId=db.Connections.Local.Single().Id, Serial = "private-serial", Notes = "private-notes" });
        var policy = new RecoveryPolicy { Asset = "private-dataset", Runbook = "recovery-runbook",
            DestinationCapacity = "private-capacity", RestoreRequirements = "private-prerequisites",
            Owner = "private-owner", Importance = "critical", Exclusions = "private-exclusions",
            Retention = "private-retention", Schedule = "private-schedule", FailureDomains = "private-failure-domains",
            KeyCustodyReference = "private-custody-reference" };
        db.Recovery.Add(policy);
        await db.SaveChangesAsync();
        var full = Encoding.UTF8.GetString(await InventoryExport.CreateAsync(db, false));
        var redacted = Encoding.UTF8.GetString(await InventoryExport.CreateAsync(db, true));
        foreach (var secret in new[] { "owner-secret", "password-secret", "api-secret", "webhook-secret" }) { Assert.DoesNotContain(secret, full); Assert.DoesNotContain(secret, redacted); }
        Assert.Contains("private-runbook", full); Assert.Contains("recovery-runbook", full);
        using var fullDocument = JsonDocument.Parse(full);
        using var redactedDocument = JsonDocument.Parse(redacted);
        var exported = fullDocument.RootElement.GetProperty("recovery")[0];
        Assert.Equal(policy.Revision, exported.GetProperty("revision").GetGuid());
        Assert.Equal("critical", exported.GetProperty("importance").GetString());
        foreach (var field in new[] { "destinationCapacity", "restoreRequirements", "owner", "exclusions", "retention", "schedule", "failureDomains", "keyCustodyReference" })
        {
            Assert.False(string.IsNullOrWhiteSpace(exported.GetProperty(field).GetString()));
            Assert.Equal(JsonValueKind.Null, redactedDocument.RootElement.GetProperty("recovery")[0].GetProperty(field).ValueKind);
        }
        foreach (var value in new[] { "private-host", "private-runbook", "private-location", "private-serial", "private-notes", "private-dataset", "recovery-runbook" }) Assert.DoesNotContain(value, redacted);
    }
    [Theory]
    [InlineData("ambiguous", false)]
    [InlineData("inferred", true)]
    [InlineData("verified", true)]
    public async Task ExportDistinguishesHistoricalBayConfirmationFromCurrentIdentity(string confidence, bool currentVerified)
    {
        using var connection = new SqliteConnection("Data Source=:memory:"); connection.Open();
        await using var db = new HomelabDb(new DbContextOptionsBuilder<HomelabDb>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        var nas = new NasConnection(); db.Connections.Add(nas);
        var confirmed = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        db.Disks.Add(new() { ConnectionId = nas.Id, MappingConfidence = confidence, Bay = "private-bay",
            BayVerified = true, BayVerifiedUtc = confirmed, BayEvidence = "private-bay-evidence", LastSeenUtc = confirmed });
        await db.SaveChangesAsync();
        foreach (var redacted in new[] { false, true })
        {
            using var document = JsonDocument.Parse(await InventoryExport.CreateAsync(db, redacted));
            var disk = document.RootElement.GetProperty("disks")[0];
            Assert.Equal(currentVerified, disk.GetProperty("bayVerified").GetBoolean());
            Assert.True(disk.GetProperty("bayPreviouslyVerified").GetBoolean());
            Assert.Equal(confidence, disk.GetProperty("mappingConfidence").GetString());
            Assert.Equal(confirmed, disk.GetProperty("bayVerifiedUtc").GetDateTime());
            Assert.Equal(confirmed, disk.GetProperty("lastSeenUtc").GetDateTime());
            Assert.Equal(redacted ? null : "private-bay", disk.GetProperty("bay").GetString());
            Assert.Equal(redacted ? null : "private-bay-evidence", disk.GetProperty("bayEvidence").GetString());
        }
    }
    [Theory] [InlineData("javascript:alert(1)")] [InlineData("file:///etc/passwd")] [InlineData("https://user:password@example.com/")]
    public void ManagementLinksRejectExecutableSchemesAndCredentials(string url) => Assert.NotNull(InventoryRules.ValidateAsset(new() { Name = "host", ManagementUrl = url }));
    [Fact] public void SparesRequireNonnegativeQuantityAndCost()
    {
        Assert.NotNull(InventoryRules.ValidateAsset(new() { Name = "Spare", Type = "spare", Quantity = -1 }));
        Assert.NotNull(InventoryRules.ValidateAsset(new() { Name = "Spare", Type = "spare", EstimatedUnitCost = -1 }));
        Assert.Null(InventoryRules.ValidateAsset(new() { Name = "Spare", Type = "spare", Quantity = 2, EstimatedUnitCost = 200 }));
    }
    [Fact] public async Task DependencyCannotReferToMissingAsset()
    {
        using var connection = new SqliteConnection("Data Source=:memory:"); connection.Open();
        await using var db = new HomelabDb(new DbContextOptionsBuilder<HomelabDb>().UseSqlite(connection).Options); db.Database.EnsureCreated();
        db.Dependencies.Add(new() { SourceId = Guid.NewGuid(), TargetId = Guid.NewGuid() });
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }
}
