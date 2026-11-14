using System.Runtime.CompilerServices;
using System.Threading.Tasks.Sources;

namespace Pumpkin.DotNet.Runtime.Scheduling.Tasks;

[AsyncMethodBuilder(typeof(PumpkinAsyncMethodBuilder))]
public readonly struct PumpkinTask
{
    private readonly IValueTaskSource? _source;
    private readonly short _token;

    internal PumpkinTask(IValueTaskSource? source, short token)
    {
        _source = source;
        _token = token;
    }

    public static PumpkinTask CompletedTask => default;

    public bool IsCompleted => _source is null || _source.GetStatus(_token) != ValueTaskSourceStatus.Pending;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ValueTask AsValueTask() => _source is null ? ValueTask.CompletedTask : new ValueTask(_source, _token);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ValueTaskAwaiter GetAwaiter() => AsValueTask().GetAwaiter();
}

/// <summary>
/// A lightweight, typed alternative to Task&lt;TResult&gt; that does not depend on the ThreadPool.
/// </summary>
[AsyncMethodBuilder(typeof(PumpkinAsyncMethodBuilder<>))]
public readonly struct PumpkinTask<TResult>
{
    private readonly IValueTaskSource<TResult>? _source;
    private readonly short _token;
    private readonly TResult? _result;

    internal PumpkinTask(TResult result)
    {
        _source = null;
        _token = 0;
        _result = result;
    }

    internal PumpkinTask(IValueTaskSource<TResult> source, short token)
    {
        _source = source;
        _token = token;
        _result = default;
    }

    public bool IsCompleted => _source is null || _source.GetStatus(_token) != ValueTaskSourceStatus.Pending;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ValueTask<TResult> AsValueTask() =>
        _source is null ? new ValueTask<TResult>(_result!) : new ValueTask<TResult>(_source, _token);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ValueTaskAwaiter<TResult> GetAwaiter() => AsValueTask().GetAwaiter();
}

public interface IPumpkinStateMachineBox : IValueTaskSource
{
    void MoveNext();
    void SetResult();
    void SetException(Exception ex);
}

public interface IPumpkinStateMachineBox<TResult> : IValueTaskSource<TResult>
{
    void MoveNext();
    void SetResult(TResult result);
    void SetException(Exception ex);
}

internal sealed class StateMachineBox<TStateMachine, TResult> : IPumpkinStateMachineBox<TResult>, IPumpkinStateMachineBox
    where TStateMachine : IAsyncStateMachine
{
    private static readonly Stack<StateMachineBox<TStateMachine, TResult>> Pool = new(32);

    private ManualResetValueTaskSourceCore<TResult> _core;
    private readonly Action _moveNextAction;
    private readonly SendOrPostCallback _postCallback;

    // The state machine lives here and is mutated directly on the heap.
    public TStateMachine StateMachine = default!;

    public short Version => _core.Version;

    private StateMachineBox()
    {
        _postCallback = static state =>
        {
            var box = (StateMachineBox<TStateMachine, TResult>)state!;
            box.MoveNext();
        };

        // Cached delegate to this instance.
        _moveNextAction = () => PumpkinRuntime.Instance.Scheduler.Post(_postCallback, this);
    }

    public static StateMachineBox<TStateMachine, TResult> Rent() =>
        Pool.TryPop(out var box) ? box : new StateMachineBox<TStateMachine, TResult>();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void MoveNext()
    {
        var prev = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(PumpkinRuntime.Instance.Scheduler);
        try
        {
            StateMachine.MoveNext();
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(prev);
        }
    }

    public Action MoveNextAction => _moveNextAction;

    public void SetResult(TResult result) => _core.SetResult(result);
    public void SetResult() => _core.SetResult(default!);
    public void SetException(Exception error) => _core.SetException(error);

    public TResult GetResult(short token)
    {
        try
        {
            return _core.GetResult(token);
        }
        finally
        {
            ReturnToPool();
        }
    }

    void IValueTaskSource.GetResult(short token)
    {
        try
        {
            _ = _core.GetResult(token);
        }
        finally
        {
            ReturnToPool();
        }
    }

    private void ReturnToPool()
    {
        StateMachine = default!;
        _core.Reset();
        Pool.Push(this);
    }

    public ValueTaskSourceStatus GetStatus(short token) => _core.GetStatus(token);

    public void OnCompleted(Action<object?> continuation, object? state, short token, ValueTaskSourceOnCompletedFlags flags)
        => _core.OnCompleted(continuation, state, token, flags);
}