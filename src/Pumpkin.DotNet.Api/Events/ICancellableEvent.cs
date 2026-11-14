namespace Pumpkin.DotNet.Api.Events;

public interface ICancellableEvent : IEvent
{
    bool IsCancelled { get; set; }
    void Cancel() => IsCancelled = true;
}