using PluginWorld.wit.Imports.pumpkin.plugin.v0_1_0;
using Pumpkin.DotNet.Api.Events;
using Pumpkin.DotNet.Api.Events.Player;
using Pumpkin.DotNet.Runtime.Entities.Players;

namespace Pumpkin.DotNet.Runtime.Events.Adapters;

internal sealed class PlayerChatEventAdapter : BaseEventAdapter<PlayerChatEvent>
{
    protected override ushort ExpectedTag => IEventImports.Event.Tags.PlayerChatEvent;

    protected override void PackBackCore(PlayerChatEvent apiEvent, ref IEventImports.Event rawEvent)
    {
        var oldData = rawEvent.AsPlayerChatEvent;

        var recipients = apiEvent.Recipients;
        var nativeRecipients = new List<IPlayerImports.Player>(recipients.Count);
        for (int i = 0; i < recipients.Count; i++)
        {
            if (recipients[i] is Player p)
            {
                nativeRecipients.Add(p.WitHandle);
            }
        }

        var eventData = new IEventImports.PlayerChatEventData()
        {
            player = oldData.player,
            message = apiEvent.Message,
            recipients = nativeRecipients,
            signature = apiEvent.Signature,
            cancelled = apiEvent.IsCancelled
        };

        rawEvent = IEventImports.Event.PlayerChatEvent(eventData);
    }

    protected override PlayerChatEvent UnpackCore(IEventImports.Event rawEvent)
    {
        var data = rawEvent.AsPlayerChatEvent;

        return new PlayerChatEvent()
        {
            Player = PlayerCache.GetOrBorrow(data.player),
            Message = data.message,
            Recipients = [.. data.recipients.Select(PlayerCache.GetOrBorrow)],
            Signature = data.signature,
            IsCancelled = data.cancelled
        };
    }
}