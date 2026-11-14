using System.Runtime.CompilerServices;
using Pumpkin.DotNet.Api.Scheduling;

namespace Pumpkin.DotNet.Api.Tasks;

public static class PumpkinTask
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ISyncScheduler GetSyncContext()
    {
        if (SynchronizationContext.Current is ISyncScheduler ctx)
            return ctx;

        throw new InvalidOperationException(
            "PumpkinTask sync methods can only be used inside the Pumpkin server tick loop (missing ISyncScheduler).");
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static IAsyncScheduler GetAsyncContext()
    {
        if (SynchronizationContext.Current is IAsyncScheduler ctx)
            return ctx;

        throw new InvalidOperationException(
            "PumpkinTask async methods can only be used inside the Pumpkin server tick loop (missing IAsyncScheduler).");
    }

    // ==========================================
    // 1. Synchronous tick operations (Action)
    // ==========================================

    public static long CurrentTick => GetSyncContext().CurrentTick;

    public static IScheduledTask Run(Action action) 
        => GetSyncContext().Run(action);

    public static IScheduledTask Run(long delayTicks, Action action) 
        => GetSyncContext().RunDelayTicks(delayTicks, action);

    public static IScheduledTask RunRepeatingTicks(long delayTicks, long periodTicks, Action action) 
        => GetSyncContext().RunRepeatingTicks(delayTicks, periodTicks, action);    

    public static IScheduledTask RunRepeatingTicks(long delayTicks, long periodTicks, Action<IScheduledTask> action) 
        => GetSyncContext().RunRepeatingTicks(delayTicks, periodTicks, action);    

    // ==========================================
    // 2. Asynchronous coroutines and delays (ValueTask)
    // ==========================================

    public static ValueTask RunAsync(
        Func<CancellationToken, ValueTask> action, 
        CancellationToken cancellationToken = default) 
        => GetAsyncContext().RunAsync(action, cancellationToken);

    public static ValueTask RunAsync(
        Func<ValueTask> action, 
        CancellationToken cancellationToken = default) 
        => GetAsyncContext().RunAsync(_ => action(), cancellationToken);

    public static ValueTask RunDelayTicksAsync(
        long delayTicks, 
        Func<CancellationToken, ValueTask> action, 
        CancellationToken cancellationToken = default) 
        => GetAsyncContext().RunDelayTicksAsync(delayTicks, action, cancellationToken);

    public static ValueTask DelayTicksAsync(
        long ticks, 
        CancellationToken cancellationToken = default) 
        => GetAsyncContext().DelayTicksAsync(ticks, cancellationToken);

    public static ValueTask NextTickAsync(CancellationToken cancellationToken = default) 
        => GetAsyncContext().DelayTicksAsync(1, cancellationToken);

    public static ValueTask YieldAsync(CancellationToken cancellationToken = default) 
        => GetAsyncContext().YieldAsync(cancellationToken); 

    public static void Spawn(Func<ValueTask> action) 
        => GetAsyncContext().Spawn(action);

    public static void Spawn(
        Func<CancellationToken, 
        ValueTask> action, 
        CancellationToken cancellationToken = default)
        => GetAsyncContext().Spawn(action, cancellationToken); 
}