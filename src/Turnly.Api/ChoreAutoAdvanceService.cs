using Turnly.Core.Services;

namespace Turnly.Api;

/// <summary>Polls once a minute and auto-advances multi-completion chores whose completion window
/// has expired. Runs unconditionally (no VAPID dependency). Each tick runs in its own DI scope
/// so it can use the scoped <see cref="ChoreService"/>/<c>DbContext</c>.</summary>
public class ChoreAutoAdvanceService : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ChoreAutoAdvanceService> _logger;

    public ChoreAutoAdvanceService(IServiceScopeFactory scopeFactory, ILogger<ChoreAutoAdvanceService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);
        do
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var chores = scope.ServiceProvider.GetRequiredService<ChoreService>();
                var advanced = await chores.AutoAdvanceAsync(DateTimeOffset.UtcNow, stoppingToken);
                if (advanced > 0)
                    _logger.LogInformation("Auto-advanced {Count} incomplete chore occurrence(s).", advanced);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Chore auto-advance scan failed; will retry next tick.");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
