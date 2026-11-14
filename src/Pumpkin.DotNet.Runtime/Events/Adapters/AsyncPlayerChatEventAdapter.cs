using PluginWorld.wit.Imports.pumpkin.plugin.v0_1_0;
using Pumpkin.DotNet.Api.Events.Player;
using Pumpkin.DotNet.Runtime.Entities.Players;
using Pumpkin.DotNet.Runtime.Text;

namespace Pumpkin.DotNet.Runtime.Events.Adapters;

internal sealed class AsyncPlayerChatEventAdapter : BaseEventAdapter<AsyncPlayerChatEvent>
{
    protected override ushort ExpectedTag => IEventImports.Event.Tags.AsyncPlayerChatEvent;

    protected override void PackBackCore(AsyncPlayerChatEvent apiEvent, ref IEventImports.Event rawEvent)
    {
        var oldData = rawEvent.AsAsyncPlayerChatEvent;

        var eventData = new IEventImports.AsyncPlayerChatEventData()
        {
            player = oldData.player,
            message = apiEvent.Message,
            format = apiEvent.Format.ToNative(),
            cancelled = apiEvent.IsCancelled
        };

        rawEvent = IEventImports.Event.AsyncPlayerChatEvent(eventData);
    }

    protected override AsyncPlayerChatEvent UnpackCore(IEventImports.Event rawEvent)
    {
        var data = rawEvent.AsAsyncPlayerChatEvent;

        return new AsyncPlayerChatEvent()
        {
            Player = PlayerCache.GetBorrowed(data.player),
            Message = data.message,
            Format = data.format.ToComponent(),
            IsCancelled = data.cancelled
        };
    }
}