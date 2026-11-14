namespace Pumpkin.DotNet.Runtime.Commands;

using System;
using PluginWorld.wit.Imports.pumpkin.plugin.v0_1_0;
using Pumpkin.DotNet.Api.Commands;
using Pumpkin.DotNet.Api.Entities;
using Pumpkin.DotNet.Api.Math;
using Pumpkin.DotNet.Api.Permissions;
using Pumpkin.DotNet.Api.Text;
using Pumpkin.DotNet.Runtime.Entities.Players;
using Pumpkin.DotNet.Runtime.Text;

internal sealed class CommandSenderWrapper : ICommandSender
{
    private readonly ICommandImports.CommandSender _nativeSender;
    private readonly IServerImports.Server? _nativeServer;

    public CommandSenderWrapper(
        ICommandImports.CommandSender nativeSender,
        IServerImports.Server? nativeServer = null)
    {
        _nativeSender = nativeSender ?? throw new ArgumentNullException(nameof(nativeSender));
        _nativeServer = nativeServer;
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
            var playerResource = _nativeSender.AsPlayer();
            return playerResource is not null ? PlayerCache.GetBorrowed(playerResource) : null;
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
        if (_nativeServer is null || string.IsNullOrWhiteSpace(node))
            return false;

        return _nativeSender.HasPermission(_nativeServer, node);
    }

    public void SendMessage(Component message)
    {
        string json = ComponentBridge.ToJson(message);
        var witText = ITextImports.TextComponent.FromJson(json);
        _nativeSender.SendMessage(witText); // SendMessage takes ownership (Handle = 0).
    }

    public void SendSystemMessage(Component message)
    {
        string json = ComponentBridge.ToJson(message);
        var witText = ITextImports.TextComponent.FromJson(json);
        _nativeSender.SendSystemMessage(witText);
    }

    public void SendError(Component message)
    {
        string json = ComponentBridge.ToJson(message);
        var witText = ITextImports.TextComponent.FromJson(json);
        _nativeSender.SendError(witText);
    }

    public void SetSuccessCount(int count)
        => _nativeSender.SetSuccessCount(count);
}