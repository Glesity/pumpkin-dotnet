namespace Pumpkin.DotNet.Api.Scheduling.Extensions;

public static class SchedulerExtensions
{
    [Obsolete("Use the asynchronous scheduler API for ValueTask-based actions.", error: true)]
    public static IScheduledTask Run(this ISyncScheduler scheduler, Func<ValueTask> action) 
        => throw new InvalidOperationException();
}