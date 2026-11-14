using PluginWorld.wit.Imports.pumpkin.plugin.v0_1_0;
using Pumpkin.DotNet.Api.Events;
using Pumpkin.DotNet.Api.Events.Player;

namespace Pumpkin.DotNet.Runtime.Events;

internal static class EventTypeMapper
{
    private sealed record EventMetadata(IEventImports.EventType NativeType, bool DefaultBlocking);

    private static readonly Dictionary<Type, EventMetadata> Registry = new()
    {
        [typeof(PlayerJoinEvent)] = new(
            NativeType: IEventImports.EventType.PLAYER_JOIN_EVENT,
            DefaultBlocking: true
        ),
        [typeof(PlayerLeaveEvent)] = new(
            NativeType: IEventImports.EventType.PLAYER_LEAVE_EVENT,
            DefaultBlocking: true
        ),
        [typeof(PlayerChatEvent)] = new(
            NativeType: IEventImports.EventType.PLAYER_CHAT_EVENT,
            DefaultBlocking: true 
        ),
        [typeof(AsyncPlayerChatEvent)] = new(
            NativeType: IEventImports.EventType.ASYNC_PLAYER_CHAT_EVENT,
            DefaultBlocking: true
        )
    };

    public static IEventImports.EventType GetNativeType(Type eventType)
    {
        if (Registry.TryGetValue(eventType, out var meta))
            return meta.NativeType;

        throw new NotSupportedException($"Event type '{eventType.Name}' is not registered in Runtime.");
    }

    public static bool IsDefaultBlocking(Type eventType)
    {
        if (Registry.TryGetValue(eventType, out var meta))
            return meta.DefaultBlocking;

        // Events implementing ICancellable are blocking by default.
        return typeof(ICancellableEvent).IsAssignableFrom(eventType);
    }
}