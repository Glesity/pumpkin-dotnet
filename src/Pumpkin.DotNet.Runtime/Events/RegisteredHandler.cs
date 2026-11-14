using Pumpkin.DotNet.Api.Events;

namespace Pumpkin.DotNet.Runtime.Events;

internal sealed class RegisteredHandler(uint id, Type eventType, Delegate originalHandler, Action<IEvent> invoker)
{
    public uint Id { get; } = id;
    public Type EventType { get; } = eventType;
    public Delegate OriginalHandler { get; } = originalHandler;

    public Action<IEvent> Invoker { get; } = invoker;
}