using System.Runtime.CompilerServices;

namespace Pumpkin.DotNet.Runtime.Scheduling.Tasks;
public struct PumpkinAsyncMethodBuilder
{
    private IPumpkinStateMachineBox? _box;
    private short _token;

    public static PumpkinAsyncMethodBuilder Create() => default;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Start<TStateMachine>(ref TStateMachine stateMachine)
        where TStateMachine : IAsyncStateMachine
    {
        // Start synchronously.
        stateMachine.MoveNext();
    }

    public void SetStateMachine(IAsyncStateMachine stateMachine) { }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void SetResult()
    {
        _box?.SetResult();
    }

    public void SetException(Exception exception)
    {
        _box ??= RentBox<EmptyStateMachine>();
        _box.SetException(exception);
    }

    public PumpkinTask Task => new(_box, _token);

    public void AwaitOnCompleted<TAwaiter, TStateMachine>(ref TAwaiter awaiter, ref TStateMachine stateMachine)
        where TAwaiter : INotifyCompletion
        where TStateMachine : IAsyncStateMachine
    {
        var action = GetAction(ref stateMachine);
        awaiter.OnCompleted(action);
    }

    public void AwaitUnsafeOnCompleted<TAwaiter, TStateMachine>(ref TAwaiter awaiter, ref TStateMachine stateMachine)
        where TAwaiter : ICriticalNotifyCompletion
        where TStateMachine : IAsyncStateMachine
    {
        var action = GetAction(ref stateMachine);
        awaiter.UnsafeOnCompleted(action);
    }

    private Action GetAction<TStateMachine>(ref TStateMachine stateMachine)
        where TStateMachine : IAsyncStateMachine
    {
        if (_box is null)
        {
            var typedBox = StateMachineBox<TStateMachine, byte>.Rent();
            _token = typedBox.Version;
            typedBox.StateMachine = stateMachine;
            _box = typedBox;
            return typedBox.MoveNextAction;
        }

        return ((StateMachineBox<TStateMachine, byte>)_box).MoveNextAction;
    }

    private IPumpkinStateMachineBox RentBox<TStateMachine>() where TStateMachine : IAsyncStateMachine
    {
        var box = StateMachineBox<TStateMachine, byte>.Rent();
        _token = box.Version;
        return box;
    }

    private struct EmptyStateMachine : IAsyncStateMachine
    {
        public void MoveNext() { }
        public void SetStateMachine(IAsyncStateMachine stateMachine) { }
    }
}

public struct PumpkinAsyncMethodBuilder<TResult>
{
    private IPumpkinStateMachineBox<TResult>? _box;
    private TResult? _result;
    private short _token;

    public static PumpkinAsyncMethodBuilder<TResult> Create() => default;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Start<TStateMachine>(ref TStateMachine stateMachine)
        where TStateMachine : IAsyncStateMachine
    {
        // Synchronously start the first part of the state machine.
        stateMachine.MoveNext();
    }

    public void SetStateMachine(IAsyncStateMachine stateMachine) { }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void SetResult(TResult result)
    {
        if (_box is null)
        {
            // Complete synchronously without switching to an asynchronous context (zero allocation).
            _result = result;
        }
        else
        {
            _box.SetResult(result);
        }
    }

    public void SetException(Exception exception)
    {
        _box ??= RentBox<EmptyStateMachine>();
        _box.SetException(exception);
    }

    public PumpkinTask<TResult> Task =>
        _box is null ? new PumpkinTask<TResult>(_result!) : new PumpkinTask<TResult>(_box, _token);

    public void AwaitOnCompleted<TAwaiter, TStateMachine>(ref TAwaiter awaiter, ref TStateMachine stateMachine)
        where TAwaiter : INotifyCompletion
        where TStateMachine : IAsyncStateMachine
    {
        var action = GetAction(ref stateMachine);
        awaiter.OnCompleted(action);
    }

    public void AwaitUnsafeOnCompleted<TAwaiter, TStateMachine>(ref TAwaiter awaiter, ref TStateMachine stateMachine)
        where TAwaiter : ICriticalNotifyCompletion
        where TStateMachine : IAsyncStateMachine
    {
        var action = GetAction(ref stateMachine);
        awaiter.UnsafeOnCompleted(action);
    }

    private Action GetAction<TStateMachine>(ref TStateMachine stateMachine)
        where TStateMachine : IAsyncStateMachine
    {
        if (_box is null)
        {
            var typedBox = StateMachineBox<TStateMachine, TResult>.Rent();
            _token = typedBox.Version;
            typedBox.StateMachine = stateMachine;
            _box = typedBox;
            return typedBox.MoveNextAction;
        }

        return ((StateMachineBox<TStateMachine, TResult>)_box).MoveNextAction;
    }

    private IPumpkinStateMachineBox<TResult> RentBox<TStateMachine>() where TStateMachine : IAsyncStateMachine
    {
        var box = StateMachineBox<TStateMachine, TResult>.Rent();
        _token = box.Version;
        return box;
    }

    private struct EmptyStateMachine : IAsyncStateMachine
    {
        public void MoveNext() { }
        public void SetStateMachine(IAsyncStateMachine stateMachine) { }
    }
}