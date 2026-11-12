using Casino.Game.Rounds;
using Casino.Game.Configuration.Settings;
using Casino.Game.Persistence;

namespace Casino.Game.Recovery;

internal sealed class RoundRecoveryWorker(IGameStore store, IServiceScopeFactory scopes, ILogger<RoundRecoveryWorker> log, RecoverySettings recoverySettings) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(recoverySettings.IntervalSeconds));
        do
        {
            try
            {
                foreach (var id in store.Unfinished())
                {
                    if (stoppingToken.IsCancellationRequested) return;
                    try
                    {
                        using var scope = scopes.CreateScope();
                        await scope.ServiceProvider.GetRequiredService<GameEngine>().Advance(id, stoppingToken);
                    }
                    catch (Exception ex) when (!stoppingToken.IsCancellationRequested) { log.LogWarning(ex, "RoundRecoveryWorker deferred for {RoundId}", id); }
                }
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested) { log.LogError(ex, "RoundRecoveryWorker sweep failed; will retry"); }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
