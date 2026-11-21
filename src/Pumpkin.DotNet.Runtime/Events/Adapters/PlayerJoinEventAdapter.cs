using PluginWorld.wit.Imports.pumpkin.plugin.v0_1_0;
using Pumpkin.DotNet.Api.Events;
using Pumpkin.DotNet.Api.Events.Player;
using Pumpkin.DotNet.Api.Text;
using Pumpkin.DotNet.Runtime.Entities.Players;
using Pumpkin.DotNet.Runtime.Text;

namespace Pumpkin.DotNet.Runtime.Events.Adapters;

internal sealed class PlayerJoinEventAdapter : BaseEventAdapter<PlayerJoinEvent>
{
    protected override ushort ExpectedTag => IEventImports.Event.Tags.PlayerJoinEvent;

    protected override void PackBackCore(PlayerJoinEvent apiEvent, ref IEventImports.Event rawEvent)
    {
        var oldData = rawEvent.AsPlayerJoinEvent;

        var eventData = new IEventImports.PlayerJoinEventData
        {
            player = oldData.player,
            joinMessage = apiEvent.JoinMessage.ToNative(),
            cancelled = apiEvent.IsCancelled
        };

        rawEvent = IEventImports.Event.PlayerJoinEvent(eventData);
    }

    protected override PlayerJoinEvent UnpackCore(IEventImports.Event rawEvent)
    {
        var @event = rawEvent.AsPlayerJoinEvent;

        return new PlayerJoinEvent()
        {
            Player = PlayerCache.GetOrBorrow(@event.player),
            JoinMessage = @event.joinMessage.ToComponent(),
            IsCancelled = @event.cancelled
        };
    }
}