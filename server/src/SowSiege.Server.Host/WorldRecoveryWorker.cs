using SowSiege.Server.Infrastructure;

namespace SowSiege.Server.Host;

/// <summary>Polling controls operational latency only; persisted server UTC deadlines decide every event.</summary>
public sealed class WorldRecoveryWorker(WorldMarchStore marches, WorldRecoveryGate gate, TimeProvider time,
    IConfiguration configuration, ILogger<WorldRecoveryWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        int milliseconds = configuration.GetValue("World:RecoveryPollMilliseconds", 1000);
        if (milliseconds is < 100 or > 60000) { throw new InvalidOperationException("World recovery polling must be between 100 and 60000 milliseconds."); }
        if (!gate.IsAttached) { throw new InvalidOperationException("World recovery must initialize before the worker starts."); }
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(milliseconds), time);
        try
        {
            do
            {
                WorldRecoveryBatchResult batch;
                do
                {
                    stoppingToken.ThrowIfCancellationRequested();
                    batch = await marches.RecoverBatchAsync(stoppingToken);
                    if (batch.HasMore) { await Task.Yield(); }
                }
                while (batch.HasMore);
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
        catch (Exception error)
        {
            logger.LogCritical(error, "World recovery failed; stopping host to prevent chronology from being bypassed.");
            throw;
        }
    }
}
