using PluginWorld.wit.Imports.pumpkin.plugin.v0_1_0;
using Pumpkin.DotNet.Api.Events.Player;
using Pumpkin.DotNet.Runtime.Entities.Players;
using Pumpkin.DotNet.Runtime.Text;

namespace Pumpkin.DotNet.Runtime.Events.Adapters;

internal sealed class PlayerLeaveEventAdapter : BaseEventAdapter<PlayerLeaveEvent>
{
    protected override ushort ExpectedTag => IEventImports.Event.Tags.PlayerLeaveEvent;

    protected override void PackBackCore(PlayerLeaveEvent apiEvent, ref IEventImports.Event rawEvent)
    {
        var oldData = rawEvent.AsPlayerLeaveEvent;

        var eventData = new IEventImports.PlayerLeaveEventData()
        {
            player = oldData.player,
            leaveMessage = apiEvent.LeaveMessage.ToNative(),
            cancelled = apiEvent.IsCancelled
        };

        rawEvent = IEventImports.Event.PlayerLeaveEvent(eventData);
    }

    protected override PlayerLeaveEvent UnpackCore(IEventImports.Event rawEvent)
    {
        var data = rawEvent.AsPlayerLeaveEvent;

        return new PlayerLeaveEvent()
        {
            Player = PlayerCache.GetBorrowed(data.player),
            LeaveMessage = data.leaveMessage.ToComponent(),
            IsCancelled = data.cancelled
        };
    }
}