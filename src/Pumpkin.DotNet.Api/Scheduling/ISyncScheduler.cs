namespace Pumpkin.DotNet.Api.Scheduling;

public interface ISyncScheduler
{
    long CurrentTick { get; }

    IScheduledTask Run(Action action);
    IScheduledTask RunDelayTicks(long delayTicks, Action action);
    IScheduledTask RunRepeatingTicks(long delayTicks, long periodTicks, Action action);
    IScheduledTask RunRepeatingTicks(long delayTicks, long periodTicks, Action<IScheduledTask> action);

    void CancelAll();
}