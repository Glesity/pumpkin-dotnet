namespace Pumpkin.DotNet.Api.Scheduling;

public interface IAsyncScheduler
{
    ValueTask RunAsync(Func<CancellationToken, ValueTask> action, CancellationToken cancellationToken = default);
    ValueTask RunDelayTicksAsync(long delayTicks, Func<CancellationToken, ValueTask> action, CancellationToken cancellationToken = default);

    /// <summary>
    /// Suspends the coroutine until the next server tick.
    /// </summary>
    ValueTask NextTickAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Suspends the coroutine for the specified number of server ticks.
    /// </summary>
    ValueTask DelayTicksAsync(long delayTicks, CancellationToken cancellationToken = default);

    ValueTask YieldAsync(CancellationToken cancellationToken = default);

    void Spawn(Func<ValueTask> action);
    
    void Spawn(Func<CancellationToken, ValueTask> action, CancellationToken cancellationToken = default);

    void CancelAll();
}