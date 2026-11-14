using System.Threading.Tasks.Sources;
using ImportsWorld.wit.Imports.wasi.io.v0_2_12;

namespace Pumpkin.DotNet.Runtime.Scheduling.Tasks;

internal sealed class PooledPollableTaskSource : IValueTaskSource
{
    private static readonly Stack<PooledPollableTaskSource> Pool = new(64);
    private ManualResetValueTaskSourceCore<bool> _core;

    public IPollImports.Pollable? Pollable { get; private set; }
    public bool OwnsPollable { get; private set; }
    public short Version => _core.Version;

    public static PooledPollableTaskSource Rent(IPollImports.Pollable pollable, bool ownsPollable)
    {
        var source = Pool.TryPop(out var item) ? item : new PooledPollableTaskSource();
        source.Pollable = pollable;
        source.OwnsPollable = ownsPollable;
        return source;
    }

    public void Complete()
    {
        if (OwnsPollable)
        {
            Pollable?.Dispose();
        }
        Pollable = null;
        _core.SetResult(true);
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
            Pool.Push(this);
        }
    }

    public ValueTaskSourceStatus GetStatus(short token) => _core.GetStatus(token);

    public void OnCompleted(Action<object?> continuation, object? state, short token, ValueTaskSourceOnCompletedFlags flags)
        => _core.OnCompleted(continuation, state, token, flags);
}