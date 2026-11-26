using Pumpkin.DotNet.Api.Entities;

namespace Pumpkin.DotNet.Api.Events.Player;

public sealed class PlayerChatEvent : ICancellableEvent
{
    public required IPlayer Player { get; init; }
    public required string Message { get; set; }
    /// <summary>
    /// List of potential message recipients provided by the server.
    /// Note: modifying this collection is currently not supported by the PumpkinMC host.
    /// </summary>
    [Obsolete("Recipients is currently ignored by the PumpkinMC host. Use IsCanceled and manual player.SendMessage for selective delivery.")]
    public IReadOnlyList<IPlayer> Recipients { get; init; } = [];
    public byte[]? Signature { get; init; }
    public bool IsCancelled { get; set; }
}