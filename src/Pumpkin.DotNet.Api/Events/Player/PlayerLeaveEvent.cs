using Pumpkin.DotNet.Api.Entities;
using Pumpkin.DotNet.Api.Text;

namespace Pumpkin.DotNet.Api.Events.Player;

public sealed class PlayerLeaveEvent : ICancellableEvent
{
    public required IPlayer Player { get; init; }
    public required Component LeaveMessage { get; set; }
    public bool IsCancelled { get; set; }
}