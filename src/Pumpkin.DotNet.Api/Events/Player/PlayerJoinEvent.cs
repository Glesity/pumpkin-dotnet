using Pumpkin.DotNet.Api.Entities;
using Pumpkin.DotNet.Api.Text;

namespace Pumpkin.DotNet.Api.Events.Player;

public sealed class PlayerJoinEvent : ICancellableEvent
{
    public required IPlayer Player { get; init; }
    public required Component JoinMessage { get; set; }
    public bool IsCancelled { get; set; }
}