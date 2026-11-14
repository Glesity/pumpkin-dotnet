#nullable enable

namespace Pumpkin.DotNet.Runtime.Scheduling;

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Pumpkin.DotNet.Api.Scheduling;
using Pumpkin.DotNet.Runtime.Logging;
using Pumpkin.DotNet.Runtime.Scheduling.Tasks;

public enum SchedulerPhase
{
    Bootstrap,
    BeginTick,
    WakeCompletions,
    ExecuteSyncActions,
    DrainMicrotasks,
    EndTick
}

public sealed class PumpkinScheduler : SynchronizationContext, ISyncScheduler, IAsyncScheduler, IDisposable
{
    private const int MaxMicrotasksPerTick = 4096;

    private long _currentTick;
    private long _sequence;
    private ulong _nextTaskId;

    public SchedulerPhase Phase { get; private set; } = SchedulerPhase.Bootstrap;
    public long CurrentTick => _currentTick;

    public static PumpkinScheduler Instance { get; } = new();

    private static readonly PumpkinLogger Logger = PumpkinLogger.CreateLogger<PumpkinScheduler>();

    private readonly PriorityQueue<ScheduledCompletion, (long Tick, long Seq)> _scheduledCompletions;
    private readonly PriorityQueue<ScheduledAction, (long Tick, long Seq)> _scheduledSyncActions;

    private readonly Queue<Action> _immediateActions = new(256);
    private readonly Queue<Action> _deferredMicrotasks = new(64);

    public PumpkinScheduler()
    {
        var comparer = new TickSequenceComparer();
        _scheduledCompletions = new(comparer);
        _scheduledSyncActions = new(comparer);

        SetSynchronizationContext(this);
    }

    public void Tick()
    {
        var prevContext = SynchronizationContext.Current;
        SetSynchronizationContext(this);

        try
        {
            Phase = SchedulerPhase.BeginTick;
            _currentTick++;

            while (_deferredMicrotasks.TryDequeue(out var deferred))
            {
                _immediateActions.Enqueue(deferred);
            }

            Phase = SchedulerPhase.WakeCompletions;
            while (_scheduledCompletions.TryPeek(out var item, out var priority))
            {
                if (priority.Tick > _currentTick)
                    break;

                _scheduledCompletions.Dequeue();

                try
                {
                    item.Complete();
                }
                catch (Exception ex)
                {
                    Logger.Error($"Error completing task on tick {_currentTick}: {ex}");
                }
            }

            Phase = SchedulerPhase.ExecuteSyncActions;
            while (_scheduledSyncActions.TryPeek(out var item, out var priority))
            {
                if (priority.Tick > _currentTick)
                    break;

                _scheduledSyncActions.Dequeue();
                ExecuteSyncAction(item);
            }

            Phase = SchedulerPhase.DrainMicrotasks;
            DrainMicrotasks(MaxMicrotasksPerTick);

            Phase = SchedulerPhase.EndTick;
        }
        finally
        {
            SetSynchronizationContext(prevContext);
        }
    }

    private void DrainMicrotasks(int budget)
    {
        int executed = 0;

        while (executed < budget && _immediateActions.TryDequeue(out var action))
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                Logger.Error($"Unhandled exception in microtask continuation: {ex}");
            }

            executed++;
        }

        while (_immediateActions.TryDequeue(out var remaining))
        {
            _deferredMicrotasks.Enqueue(remaining);
        }
    }

    private void ExecuteSyncAction(ScheduledAction scheduled)
    {
        if (!scheduled.TaskHandle.IsActive)
            return;

        try
        {
            scheduled.Action();
        }
        catch (Exception ex)
        {
            Logger.Error($"Exception in sync task #{scheduled.TaskHandle.Id}: {ex}");
        }
        finally
        {
            if (scheduled.IsRepeating && scheduled.TaskHandle.IsActive)
            {
                long nextTick = scheduled.TargetTick + scheduled.PeriodTicks;
                if (nextTick <= _currentTick)
                {
                    long missedPeriods = ((_currentTick - scheduled.TargetTick) / scheduled.PeriodTicks) + 1;
                    nextTick = scheduled.TargetTick + (missedPeriods * scheduled.PeriodTicks);
                }

                scheduled.TargetTick = nextTick;
                _scheduledSyncActions.Enqueue(scheduled, (nextTick, ++_sequence));
            }
            else
            {
                scheduled.TaskHandle.MarkCompleted();
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private long CalculateTargetTick(long delayTicks)
    {
        if (delayTicks < 0)
            throw new ArgumentOutOfRangeException(nameof(delayTicks), "Delay cannot be negative.");

        long effectiveDelay = Math.Max(1, delayTicks);

        long minRunnableTick = (_currentTick == 0)
            ? 1
            : checked(_currentTick + 1);

        long requestedTick = checked(_currentTick + effectiveDelay);

        return Math.Max(requestedTick, minRunnableTick);
    }

    public IScheduledTask Run(Action action) => RunDelayTicks(0, action);

    public IScheduledTask RunDelayTicks(long delayTicks, Action action)
    {
        ArgumentNullException.ThrowIfNull(action);

        var task = new ScheduledTask(++_nextTaskId, isRepeating: false);
        long targetTick = CalculateTargetTick(delayTicks);

        var item = new ScheduledAction(task, action, targetTick, 0, isRepeating: false);
        _scheduledSyncActions.Enqueue(item, (targetTick, ++_sequence));

        return task;
    }

    public IScheduledTask RunRepeatingTicks(long delayTicks, long periodTicks, Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        return RunRepeatingTicks(delayTicks, periodTicks, _ => action());
    }

    public IScheduledTask RunRepeatingTicks(long delayTicks, long periodTicks, Action<IScheduledTask> action)
    {
        ArgumentNullException.ThrowIfNull(action);
        if (periodTicks < 1)
            throw new ArgumentOutOfRangeException(nameof(periodTicks), "Period must be at least 1 tick.");

        var task = new ScheduledTask(++_nextTaskId, isRepeating: true);
        long targetTick = CalculateTargetTick(delayTicks);

        var item = new ScheduledAction(task, () => action(task), targetTick, periodTicks, isRepeating: true);
        _scheduledSyncActions.Enqueue(item, (targetTick, ++_sequence));

        return task;
    }

    public ValueTask NextTickAsync(CancellationToken cancellationToken = default)
        => DelayTicksAsync(1, cancellationToken);

    public ValueTask DelayTicksAsync(long delayTicks, CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
            return ValueTask.FromCanceled(cancellationToken);

        if (delayTicks <= 0)
            return ValueTask.CompletedTask;

        long targetTick = CalculateTargetTick(delayTicks);

        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var completion = new ScheduledCompletion(tcs);

        _scheduledCompletions.Enqueue(completion, (targetTick, ++_sequence));

        if (cancellationToken.CanBeCanceled)
        {
            var reg = cancellationToken.Register(static state =>
            {
                var (source, ct) = ((TaskCompletionSource, CancellationToken))state!;
                source.TrySetCanceled(ct);
            }, (tcs, cancellationToken));

            _ = tcs.Task.ContinueWith(
                static (_, state) => ((CancellationTokenRegistration)state!).Dispose(),
                reg,
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }

        return new ValueTask(tcs.Task);
    }

    public ValueTask RunAsync(Func<CancellationToken, ValueTask> action, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(action);

        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        RunDelayTicks(0, () =>
        {
            if (cancellationToken.IsCancellationRequested)
            {
                tcs.TrySetCanceled(cancellationToken);
                return;
            }

            _ = ExecuteAsyncRunner(action, cancellationToken, tcs);
        });

        return new ValueTask(tcs.Task);
    }

    public ValueTask RunDelayTicksAsync(
        long delayTicks,
        Func<CancellationToken, ValueTask> action,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(action);

        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        RunDelayTicks(delayTicks, () =>
        {
            if (cancellationToken.IsCancellationRequested)
            {
                tcs.TrySetCanceled(cancellationToken);
                return;
            }

            _ = ExecuteAsyncRunner(action, cancellationToken, tcs);
        });

        return new ValueTask(tcs.Task);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ValueTask YieldAsync(CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
            return ValueTask.FromCanceled(cancellationToken);

        if (!cancellationToken.CanBeCanceled)
        {
            var source = PooledTickDelaySource.Rent(0);
            Post(static s => ((PooledTickDelaySource)s!).OnTick(), source);
            return new ValueTask(source, source.Version);
        }

        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var reg = cancellationToken.Register(static state =>
        {
            var (source, ct) = ((TaskCompletionSource, CancellationToken))state!;
            source.TrySetCanceled(ct);
        }, (tcs, cancellationToken));

        Post(static state =>
        {
            var (source, registration) = ((TaskCompletionSource, CancellationTokenRegistration))state!;
            registration.Dispose();
            source.TrySetResult();
        }, (tcs, reg));

        return new ValueTask(tcs.Task);
    }

    private static async ValueTask ExecuteAsyncRunner(
        Func<CancellationToken, ValueTask> action,
        CancellationToken ct,
        TaskCompletionSource tcs)
    {
        try
        {
            await action(ct);
            tcs.TrySetResult();
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            tcs.TrySetCanceled(ct);
        }
        catch (Exception ex)
        {
            Logger.Error($"Unhandled exception in RunAsync: {ex}");
            tcs.TrySetException(ex);
        }
    }

    public void Spawn(Func<ValueTask> action)
    {
        ArgumentNullException.ThrowIfNull(action);

        Post(static state =>
        {
            var func = (Func<ValueTask>)state!;
            _ = ExecuteSafeSpawnAsync(func);
        }, action);
    }

    public void Spawn(Func<CancellationToken, ValueTask> action, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(action);

        Post(static state =>
        {
            var (func, ct) = ((Func<CancellationToken, ValueTask>, CancellationToken))state!;
            if (ct.IsCancellationRequested)
                return;

            _ = ExecuteSafeSpawnAsync(() => func(ct));
        }, (action, cancellationToken));
    }

    private static async ValueTask ExecuteSafeSpawnAsync(Func<ValueTask> func)
    {
        try
        {
            await func();
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            Logger.Error($"[PumpkinTask.Spawn Unhandled Exception]: {ex}");
        }
    }

    public override void Post(SendOrPostCallback d, object? state)
    {
        _immediateActions.Enqueue(() => d(state));
    }

    public override void Send(SendOrPostCallback d, object? state) => d(state);

    public override SynchronizationContext CreateCopy() => this;

    public void CancelAll()
    {
        _immediateActions.Clear();
        _deferredMicrotasks.Clear();

        while (_scheduledCompletions.TryDequeue(out var completion, out _))
            completion.TryCancel();

        while (_scheduledSyncActions.TryDequeue(out var scheduled, out _))
            scheduled.TaskHandle.Cancel();
    }

    public void Dispose() => CancelAll();

    private sealed class TickSequenceComparer : IComparer<(long Tick, long Seq)>
    {
        public int Compare((long Tick, long Seq) x, (long Tick, long Seq) y)
        {
            int tickCmp = x.Tick.CompareTo(y.Tick);
            return tickCmp != 0 ? tickCmp : x.Seq.CompareTo(y.Seq);
        }
    }

    private sealed class ScheduledAction
    {
        public readonly ScheduledTask TaskHandle;
        public readonly Action Action;
        public long TargetTick;
        public readonly long PeriodTicks;
        public readonly bool IsRepeating;

        public ScheduledAction(ScheduledTask taskHandle, Action action, long targetTick, long periodTicks, bool isRepeating)
        {
            TaskHandle = taskHandle;
            Action = action;
            TargetTick = targetTick;
            PeriodTicks = periodTicks;
            IsRepeating = isRepeating;
        }
    }

    private sealed class ScheduledCompletion
    {
        private readonly TaskCompletionSource _tcs;

        public ScheduledCompletion(TaskCompletionSource tcs) => _tcs = tcs;

        public void Complete() => _tcs.TrySetResult();
        public void TryCancel() => _tcs.TrySetCanceled();
    }
}