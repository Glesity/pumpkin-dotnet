namespace Pumpkin.DotNet.Runtime.Events;

using System;
using System.Collections.Generic;
using PluginWorld.wit.Imports.pumpkin.plugin.v0_1_0;
using Pumpkin.DotNet.Api.Events;

internal sealed class EventManager
{
    private static readonly EventManager _instance = new();
    public static EventManager Instance => _instance;

    private uint _nextHandlerId = 1;

    private readonly Dictionary<uint, RegisteredHandler> _handlers = new(capacity: 64);

    private EventManager() { }

    public uint RegisterHandler<TEvent>(Action<TEvent> handler) where TEvent : class, IEvent
    {
        uint handlerId = _nextHandlerId++;
        var eventType = typeof(TEvent);

        Action<IEvent> invoker = e => handler((TEvent)e);

        var registration = new RegisteredHandler(handlerId, eventType, handler, invoker);
        _handlers[handlerId] = registration;

        return handlerId;
    }

    public bool HasHandler(uint handlerId) => _handlers.ContainsKey(handlerId);

    public void Dispatch(uint handlerId, IEvent apiEvent)
    {
        if (_handlers.TryGetValue(handlerId, out var registration))
        {
            registration.Invoker(apiEvent);
        }
    }

    public void Unregister(uint handlerId)
    {
        _handlers.Remove(handlerId);
    }

    public void Clear()
    {
        _handlers.Clear();
    }
}