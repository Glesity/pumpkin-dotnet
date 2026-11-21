using PluginWorld.wit.Imports.pumpkin.plugin.v0_1_0;
using Pumpkin.DotNet.Api.Commands;
using Pumpkin.DotNet.Api.Entities;
using Pumpkin.DotNet.Api.Math;
using Pumpkin.DotNet.Api.Permissions;
using Pumpkin.DotNet.Api.Text;
using Pumpkin.DotNet.Runtime.Entities.Players;
using Pumpkin.DotNet.Runtime.Server;
using Pumpkin.DotNet.Runtime.Text;

namespace Pumpkin.DotNet.Runtime.Commands;

internal sealed class CommandSenderWrapper : ICommandSender
{
    private readonly ICommandImports.CommandSender _nativeSender;
    private readonly PumpkinServer _server;
    private IPlayer? _cachedPlayer;
    private bool _playerResolved;

    public CommandSenderWrapper(
        ICommandImports.CommandSender nativeSender,
        PumpkinServer server)
    {
        ArgumentNullException.ThrowIfNull(server);

        _nativeSender = nativeSender ?? throw new ArgumentNullException(nameof(nativeSender));
        _server = server;
    }

    public string Name => _nativeSender.GetName();

    public CommandSenderType SenderType
    {
        get
        {
            var witType = _nativeSender.GetCommandSenderType();
            switch (witType.Tag)
            {
                case ICommandImports.CommandSenderType.Tags.Rcon:
                    return CommandSenderType.Rcon;

                case ICommandImports.CommandSenderType.Tags.Console:
                    return CommandSenderType.Console;

                case ICommandImports.CommandSenderType.Tags.Player:
                    witType.AsPlayer.Dispose(); // Release the temporary handle from the variant.
                    return CommandSenderType.Player;

                case ICommandImports.CommandSenderType.Tags.CommandBlock:
                    var (cb, world) = witType.AsCommandBlock;
                    cb.Dispose();
                    world.Dispose();
                    return CommandSenderType.CommandBlock;

                default:
                    return CommandSenderType.Dummy;
            }
        }
    }

    public PermissionLevel PermissionLevel => (PermissionLevel)(byte)_nativeSender.PermissionLevel();

    public bool IsPlayer => _nativeSender.IsPlayer();

    public bool IsConsole => _nativeSender.IsConsole();

    public IPlayer? AsPlayer
    {
        get
        {
            if (!_playerResolved)
            {
                var playerResource = _nativeSender.AsPlayer();
                _cachedPlayer = playerResource is not null
                    ? PlayerCache.GetOrAddOwned(playerResource)
                    : null;
                _playerResolved = true;
            }

            return _cachedPlayer;
        }
    }

    public Vector3? Position
    {
        get
        {
            var pos = _nativeSender.Position();
            return pos.HasValue ? new Vector3(pos.Value.Item1, pos.Value.Item2, pos.Value.Item3) : null;
        }
    }

    public bool HasPermissionLevel(PermissionLevel level)
        => _nativeSender.HasPermissionLevel((ICommandImports.PermissionLevel)(byte)level);

    public bool HasPermission(string node)
    {
        if (string.IsNullOrWhiteSpace(node))
            return false;

        var server = _server ?? PumpkinRuntime.Instance.Server;
        return _nativeSender.HasPermission(server.WitHandle, node);
    }

    public void SendMessage(Component message)
    {
        using var text = new WitTextScope(message);
        _nativeSender.SendMessage(text.Handle);
    }

    public void SendSystemMessage(Component message)
    {
        using var text = new WitTextScope(message);
        _nativeSender.SendSystemMessage(text.Handle);
    }

    public void SendError(Component message)
    {
        using var text = new WitTextScope(message);
        _nativeSender.SendError(text.Handle);
    }

    public void SetSuccessCount(int count)
        => _nativeSender.SetSuccessCount(count);
}