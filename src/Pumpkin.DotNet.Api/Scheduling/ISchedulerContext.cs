namespace Pumpkin.DotNet.Api.Scheduling;

public interface ISchedulerContext
{
    ISyncScheduler SyncScheduler { get; }
    IAsyncScheduler AsyncScheduler { get; }

    void CancelAll();
}