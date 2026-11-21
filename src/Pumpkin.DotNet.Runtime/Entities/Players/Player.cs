using PluginWorld.wit.Imports.pumpkin.plugin.v0_1_0;
using Pumpkin.DotNet.Api.Text;
using Pumpkin.DotNet.Api.Entities;
using Pumpkin.DotNet.Runtime.Interop;
using Pumpkin.DotNet.Runtime.Text;

namespace Pumpkin.DotNet.Runtime.Entities.Players;

internal sealed class Player : WitResource<IPlayerImports.Player>, IPlayer, IEquatable<Player>
{
    public Guid Id { get; }

    public string Name { get; }

    private Player(IPlayerImports.Player handle, bool ownsHandle = true) 
        : base(handle, ownsHandle)
    {
        Id = handle.GetId().ToGuid();
        Name = handle.GetName();
    }

    internal static Player CreateOwned(IPlayerImports.Player player) =>
        new(player, true);

    internal static Player CreateBorrowed(IPlayerImports.Player player) =>
        new(player, false);

    public void SendMessage(Component message)
    {
        using var scope = new WitTextScope(message);
        Handle.SendSystemMessage(scope.Handle, false);
    }

    public bool Equals(Player? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;

        return Id.Equals(other.Id);
    }

    public override bool Equals(object? obj) => Equals(obj as Player);
    public override int GetHashCode() => Id.GetHashCode();
}