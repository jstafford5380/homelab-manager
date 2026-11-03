using Microsoft.EntityFrameworkCore;

namespace Homelab.Api;
public sealed class NotificationWorker(IServiceScopeFactory scopes, WriteCoordinator writes, NotificationEngine engine, ILogger<NotificationWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        while (!stop.IsCancellationRequested)
        {
            try
            {
                using var scope = scopes.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<HomelabDb>();
                NotificationChannel? channel;
                using (var lease = await writes.EnterAsync(stop))
                {
                    await FindingLifecycle.ExpireAsync(db, DateTime.UtcNow, stop);
                    channel = await db.NotificationChannels.SingleOrDefaultAsync(stop);
                    if (channel is not null)
                        await engine.QueueAsync(db, channel, DateTime.UtcNow, stop);
                }

                if (channel is not null)
                    await engine.DeliverOneAsync(db, channel, DateTime.UtcNow, stop);
            }
            catch (OperationCanceledException)when (stop.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogWarning("Notification processing failed ({ErrorType}).", ex.GetType().Name);
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(10), stop);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }
}
