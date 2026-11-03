using Microsoft.EntityFrameworkCore;

namespace Homelab.Api;
public sealed class HistoryRetentionWorker(IServiceScopeFactory scopes, WriteCoordinator writes, BackupService backups, ILogger<HistoryRetentionWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        while (!stop.IsCancellationRequested)
        {
            try
            {
                using var scope = scopes.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<HomelabDb>();
                using var lease = await writes.EnterAsync(stop);
                await HistoryRetention.MaintainAsync(db, backups, DateTime.UtcNow, stop);
            }
            catch (OperationCanceledException)when (stop.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogWarning("History retention did not finish ({ErrorType}); uncommitted history is preserved.", ex.GetType().Name);
            }

            try
            {
                await Task.Delay(TimeSpan.FromHours(1), stop);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }
}
