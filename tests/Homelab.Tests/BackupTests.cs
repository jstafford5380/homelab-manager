using Homelab.Api;
using Microsoft.EntityFrameworkCore;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
namespace Homelab.Tests;

public sealed class BackupTests
{
    [Fact] public async Task BackupRestoresPlansAndDisablesCollectorsWithoutCopyingKeys()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        try {
            var data = Path.Combine(root, "data"); Directory.CreateDirectory(data);
            var backupDir = Path.Combine(root, "backups");
            var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Homelab:DataPath"] = data, ["Homelab:BackupPath"] = backupDir }).Build();
            await using var db = new HomelabDb(new DbContextOptionsBuilder<HomelabDb>().UseSqlite(new SqliteConnectionStringBuilder { DataSource = Path.Combine(data, "homelab.db"), Pooling = false }.ToString()).Options);
            DatabaseLifecycle.Initialize(db, data);
            db.Plans.Add(new() { Title = "Spare purchase", Budget = 250 });
            db.Settings.Add(new() { BackupsEnabled = true });
            db.Connections.Add(new() { Name = "NAS", ProtectedApiKey = "encrypted-reference", Enabled = true });
            await db.SaveChangesAsync();
            var service = new BackupService(config);
            var record = await service.CreateAsync(db, CancellationToken.None);
            var archive = Path.Combine(backupDir, record.FileName);
            Assert.Equal("complete", record.Status);
            using (var zip = ZipFile.OpenRead(archive)) {
                Assert.Equal(2, zip.Entries.Count);
                using var manifest = JsonDocument.Parse(zip.GetEntry("manifest.json")!.Open());
                using var content = zip.GetEntry("homelab.db")!.Open();
                Assert.Equal(manifest.RootElement.GetProperty("databaseSha256").GetString(), Convert.ToHexString(SHA256.HashData(content)));
            }
            var repo = new DirectoryInfo(AppContext.BaseDirectory);
            while (repo is not null && !File.Exists(Path.Combine(repo.FullName, "scripts", "restore-backup.py"))) repo = repo.Parent;
            Assert.NotNull(repo);
            var restored = Path.Combine(root, "restored");
            var start = new ProcessStartInfo("python3") { RedirectStandardError = true, RedirectStandardOutput = true };
            start.ArgumentList.Add(Path.Combine(repo!.FullName, "scripts", "restore-backup.py")); start.ArgumentList.Add(archive); start.ArgumentList.Add(restored);
            using var process = Process.Start(start)!; await process.WaitForExitAsync();
            Assert.True(process.ExitCode == 0, await process.StandardError.ReadToEndAsync());
            await using var restoreDb = new HomelabDb(new DbContextOptionsBuilder<HomelabDb>().UseSqlite(new SqliteConnectionStringBuilder { DataSource = Path.Combine(restored, "homelab.db"), Pooling = false }.ToString()).Options);
            DatabaseLifecycle.Initialize(restoreDb, restored);
            Assert.Equal("Spare purchase", (await restoreDb.Plans.SingleAsync()).Title);
            Assert.False((await restoreDb.Connections.SingleAsync()).Enabled);
            Assert.Equal("", (await restoreDb.Connections.SingleAsync()).ProtectedApiKey);
            Assert.False((await restoreDb.Settings.SingleAsync()).BackupsEnabled);
        } finally { Directory.Delete(root, true); }
    }
    [Fact] public async Task BackupRejectsDestinationInsideLiveData()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Homelab:DataPath"] = "/tmp/homelab-live", ["Homelab:BackupPath"] = "/tmp/homelab-live/backups" }).Build();
        await using var db = new HomelabDb(new DbContextOptionsBuilder<HomelabDb>().UseSqlite("Data Source=:memory:").Options);
        await Assert.ThrowsAsync<InvalidOperationException>(() => new BackupService(config).CreateAsync(db, CancellationToken.None));
    }
}
