using PluginWorld.wit.Imports.pumpkin.plugin.v0_1_0;
using Pumpkin.DotNet.Api.Text;
using Pumpkin.DotNet.Api.Entities;
using Pumpkin.DotNet.Runtime.Interop;
using Pumpkin.DotNet.Runtime.Text;

namespace Pumpkin.DotNet.Runtime.Entities.Players;

internal sealed class Player : IPlayer, IDisposable
{
    private readonly NativePlayerOwnership _ownership;
    private IPlayerImports.Player? _nativePlayer;
    private bool _disposed;

    internal IPlayerImports.Player NativePlayer
    {
        get
        {
            var player = _nativePlayer;
            if (_disposed || player is null || player.Handle == 0)
            {
                throw new ObjectDisposedException(
                    nameof(IPlayer),
                    "IPlayer resource handle is no longer valid (likely accessed outside the synchronous event scope or across an 'await'). Capture Player.Id before awaiting and resolve the player via IServer.GetPlayer(id)."
                );
            }

            return player;
        }
    }

    public Guid Id { get; }

    public string Name { get; }

    private Player(IPlayerImports.Player player, NativePlayerOwnership ownership)
    {
        _nativePlayer = player;
        _ownership = ownership;

        Id = player.GetId().ToGuid();
        Name = player.GetName();
    }

    internal static Player CreateOwned(IPlayerImports.Player player) =>
        new(player, NativePlayerOwnership.Owned);

    internal static Player CreateBorrowed(IPlayerImports.Player player) =>
        new(player, NativePlayerOwnership.Borrowed);

    public void SendMessage(Component message)
    {
        using var scope = new NativeTextScope(message);
        NativePlayer.SendSystemMessage(scope.Native, false);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        var player = _nativePlayer;

        if (_ownership == NativePlayerOwnership.Owned && player is not null && player.Handle != 0)
        {
            player.Dispose();
        }
    }

    private enum NativePlayerOwnership
    {
        Owned,
        Borrowed
    }
}