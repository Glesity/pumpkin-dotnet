using System.Threading.Tasks.Sources;

namespace Pumpkin.DotNet.Runtime.Scheduling.Tasks;

internal sealed class PooledTickDelaySource : IValueTaskSource
{
    private const int MaxPoolSize = 64;
    
    private static readonly Stack<PooledTickDelaySource> Pool = new(64);
    private ManualResetValueTaskSourceCore<bool> _core;
    private int _remainingTicks;

    public short Version => _core.Version;

    public static PooledTickDelaySource Rent(int ticks)
    {
        PooledTickDelaySource? source;

        source = Pool.TryPop(out var item) ? item : null;

        source ??= new PooledTickDelaySource();
        source._remainingTicks = ticks;
        return source;
    }

    public bool OnTick()
    {
        if (--_remainingTicks <= 0)
        {
            _core.SetResult(true);
            return true;
        }
        return false;
    }

    public void GetResult(short token)
    {
        try
        {
            _core.GetResult(token);
        }
        finally
        {
            _core.Reset();

            if (Pool.Count < MaxPoolSize)
                Pool.Push(this);
        }
    }

    public ValueTaskSourceStatus GetStatus(short token) => _core.GetStatus(token);

    public void OnCompleted(Action<object?> continuation, object? state, short token, ValueTaskSourceOnCompletedFlags flags)
        => _core.OnCompleted(continuation, state, token, flags);
}
