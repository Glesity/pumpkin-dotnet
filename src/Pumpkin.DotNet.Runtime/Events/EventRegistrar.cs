using PluginWorld.wit.Imports.pumpkin.plugin.v0_1_0;
using Pumpkin.DotNet.Api.Events;

namespace Pumpkin.DotNet.Runtime.Events;

internal sealed class EventRegistrar : IEventRegistrar, IDisposable
{
    private IContextImports.Context? _context;
    private readonly EventManager _eventManager;

    public EventRegistrar(IContextImports.Context context, EventManager eventManager)
    {
        _context = context;
        _eventManager = eventManager;
    }

    public uint Register<TEvent>(
        Action<TEvent> handler,
        EventPriority priority = EventPriority.Normal,
        bool? blocking = null) where TEvent : class, IEvent
    {
        ObjectDisposedException.ThrowIf(_context is null, this);
        ArgumentNullException.ThrowIfNull(handler);

        var eventType = typeof(TEvent);
        var nativeType = EventTypeMapper.GetNativeType(eventType);
        var nativePriority = (IEventImports.EventPriority)priority;
        bool isBlocking = blocking ?? EventTypeMapper.IsDefaultBlocking(eventType);

        uint handlerId = _eventManager.RegisterHandler(handler);
        try
        {
            _context.RegisterEvent(handlerId, nativeType, nativePriority, isBlocking);
            return handlerId;
        }
        catch
        {
            _eventManager.Unregister(handlerId);
            throw;
        }
    }

    public void Unregister(uint handlerId)
    {
        _eventManager.Unregister(handlerId);
    }

    public void Dispose()
    {
        _context = null;
    }
}