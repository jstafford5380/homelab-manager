using Microsoft.EntityFrameworkCore;

namespace Homelab.Api;
public sealed class BackupScheduler(IServiceScopeFactory scopes, WriteCoordinator writes, BackupService backups, ILogger<BackupScheduler> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        while (!stop.IsCancellationRequested)
        {
            try
            {
                using var scope = scopes.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<HomelabDb>();
                var settings = await db.Settings.AsNoTracking().SingleOrDefaultAsync(stop);
                if (settings?.BackupsEnabled == true)
                {
                    using var lease = await writes.EnterAsync(stop);
                    var cutoff = DateTime.UtcNow.AddHours(-24);
                    if (!await db.Backups.AnyAsync(x => x.Status == "complete" && x.CreatedUtc > cutoff, stop))
                    {
                        await backups.CreateAsync(db, stop);
                        await backups.RetainAsync(db, settings, stop);
                    }
                }
            }
            catch (OperationCanceledException)when (stop.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogWarning("Scheduled backup failed ({ErrorType}); last successful backup is unchanged.", ex.GetType().Name);
            }

            try
            {
                await Task.Delay(TimeSpan.FromMinutes(5), stop);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }
}
