namespace Pumpkin.DotNet.Api.Events;

public interface IEventRegistrar
{
    uint Register<TEvent>(
        Action<TEvent> handler, 
        EventPriority priority = EventPriority.Normal, 
        bool? blocking = null) where TEvent : class, IEvent;

    void Unregister(uint handlerId);
}