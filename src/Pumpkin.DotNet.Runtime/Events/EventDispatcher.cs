using PluginWorld.wit.Imports.pumpkin.plugin.v0_1_0;
using Pumpkin.DotNet.Api.Events;
using Pumpkin.DotNet.Api.Logging;
using Pumpkin.DotNet.Runtime.Events.Adapters;
using Pumpkin.DotNet.Runtime.Logging;

namespace Pumpkin.DotNet.Runtime.Events;

internal static class EventDispatcher
{
    private static readonly ILogger logger = PumpkinLogger.CreateLogger("EventDispatcher");

    private static readonly Dictionary<ushort, IEventAdapter> Adapters = new()
    {
        [IEventImports.Event.Tags.PlayerJoinEvent] = new PlayerJoinEventAdapter(),
        [IEventImports.Event.Tags.PlayerLeaveEvent] = new PlayerLeaveEventAdapter(),
        [IEventImports.Event.Tags.PlayerChatEvent] = new PlayerChatEventAdapter(),
        [IEventImports.Event.Tags.AsyncPlayerChatEvent] = new AsyncPlayerChatEventAdapter()
    };

    public static IEventImports.Event Dispatch(uint eventId, IEventImports.Event rawEvent)
    {
        if (!EventManager.Instance.HasHandler(eventId))
        {
            return rawEvent;
        }

        if (!Adapters.TryGetValue(rawEvent.Tag, out var adapter))
        {
            logger.Warn($"[EventDispatcher] No adapter registered for event tag '{rawEvent.Tag}'.");
            return rawEvent;
        }

        IEvent apiEvent;
        try
        {
            apiEvent = adapter.Unpack(rawEvent);
        }
        catch (Exception ex)
        {
            logger.Error($"[EventDispatcher] Failed to unpack event '{rawEvent.Tag}': {ex}");
            return rawEvent;
        }

        try
        {
            EventManager.Instance.Dispatch(eventId, apiEvent);

            adapter.PackBack(apiEvent, ref rawEvent);
        }
        catch (Exception ex)
        {
            logger.Error($"[EventDispatcher] Unhandled exception in event pipeline for '{apiEvent.GetType().Name}': {ex}");
        }

        return rawEvent;
    }
}