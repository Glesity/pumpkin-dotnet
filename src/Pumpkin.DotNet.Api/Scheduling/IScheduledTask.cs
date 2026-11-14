namespace Pumpkin.DotNet.Api.Scheduling;

public interface IScheduledTask
{
    ulong Id { get; }
    bool IsRepeating { get; }
    bool IsCancel { get; }
    bool IsActive { get; }
    void Cancel();
}