using Microsoft.Extensions.Options;
using Turnly.Core.Notifications;
using Turnly.Core.Services;

namespace Turnly.Api;

/// <summary>Polls once a minute and fires any chore notifications that have come due. Idle (no
/// scheduling) until VAPID keys are configured. Each tick runs in its own DI scope so it can use the
/// scoped <see cref="NotificationService"/>/<c>DbContext</c>.</summary>
public class NotificationSchedulerService : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly VapidOptions _vapid;
    private readonly ILogger<NotificationSchedulerService> _logger;

    public NotificationSchedulerService(
        IServiceScopeFactory scopeFactory, IOptions<VapidOptions> vapid, ILogger<NotificationSchedulerService> logger)
    {
        _scopeFactory = scopeFactory;
        _vapid = vapid.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_vapid.IsConfigured)
        {
            _logger.LogInformation("VAPID keys not configured; notification scheduler is idle.");
            return;
        }

        using var timer = new PeriodicTimer(Interval);
        do
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var notifications = scope.ServiceProvider.GetRequiredService<NotificationService>();
                var fired = await notifications.ProcessDueAsync(DateTimeOffset.UtcNow, stoppingToken);
                if (fired > 0)
                    _logger.LogInformation("Fired {Count} chore notification(s).", fired);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Notification scan failed; will retry next tick.");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
