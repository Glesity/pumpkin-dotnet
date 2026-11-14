using Pumpkin.DotNet.Api.Entities;
using Pumpkin.DotNet.Api.Text;

namespace Pumpkin.DotNet.Api.Events.Player;

[Obsolete("This event is not yet dispatched by the Pumpkin host. Use PlayerChatEvent instead.")]
public sealed class AsyncPlayerChatEvent : ICancellableEvent
{
    public required IPlayer Player { get; init; }
    public required string Message { get; set; }
    public required Component Format { get; set; }
    public bool IsCancelled { get; set; }
} 