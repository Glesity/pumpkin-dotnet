using System.Threading;
using PluginWorld.wit.Imports.pumpkin.plugin.v0_1_0;
using Pumpkin.DotNet.Api.Text;
using Pumpkin.DotNet.Api.Entities;
using Pumpkin.DotNet.Runtime.Interop;
using Pumpkin.DotNet.Runtime.Text;

namespace Pumpkin.DotNet.Runtime.Entities.Players;

internal sealed class Player : IPlayer, IDisposable
{
    private readonly NativePlayerOwnership _ownership;
    private int _disposed;

    internal IPlayerImports.Player NativePlayer { get; }

    public Guid Id 
    {
        get
        {
            if (field == Guid.Empty)
                field = NativePlayer.GetId().ToGuid();

            return field;
        }
    }

    public string Name => field ??= NativePlayer.GetName();

    private Player(IPlayerImports.Player player, NativePlayerOwnership ownership)
    {
        NativePlayer = player;
        _ownership = ownership;
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
        if (_ownership == NativePlayerOwnership.Owned &&
            Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            NativePlayer.Dispose();
        }
    }

    private enum NativePlayerOwnership
    {
        Owned,
        Borrowed
    }
}