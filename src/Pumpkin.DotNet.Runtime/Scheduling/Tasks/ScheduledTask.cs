
using System;
using System.Threading;
using Pumpkin.DotNet.Api.Scheduling;

namespace Pumpkin.DotNet.Runtime.Scheduling.Tasks;
internal sealed class ScheduledTask : IScheduledTask
{
    private readonly CancellationTokenSource _cts;
    private int _state; // 0 = Active, 1 = Cancelled, 2 = Completed

    public ulong Id { get; }
    public bool IsRepeating { get; }

    public bool IsCancel => Volatile.Read(ref _state) == 1 || _cts.IsCancellationRequested;
    public bool IsActive => Volatile.Read(ref _state) == 0 && !_cts.IsCancellationRequested;

    public CancellationToken Token => _cts.Token;

    public ScheduledTask(ulong id, bool isRepeating, CancellationToken externalToken = default)
    {
        Id = id;
        IsRepeating = isRepeating;

        _cts = externalToken.CanBeCanceled
            ? CancellationTokenSource.CreateLinkedTokenSource(externalToken)
            : new CancellationTokenSource();
    }

    public void Cancel()
    {
        if (Interlocked.CompareExchange(ref _state, 1, 0) == 0)
        {
            try
            {
                _cts.Cancel();
            }
            finally
            {
                _cts.Dispose();
            }
        }
    }

    internal void MarkCompleted()
    {
        if (Interlocked.CompareExchange(ref _state, 2, 0) == 0)
        {
            _cts.Dispose();
        }
    }
}