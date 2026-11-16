using System;
using System.Collections.Generic;
using PluginWorld.wit.Imports.pumpkin.plugin.v0_1_0;
using Pumpkin.DotNet.Api.Entities;
using Pumpkin.DotNet.Api.Logging;
using Pumpkin.DotNet.Runtime.Interop;
using Pumpkin.DotNet.Runtime.Logging;

namespace Pumpkin.DotNet.Runtime.Entities.Players;

internal static class PlayerCache
{
    private static readonly ILogger Logger = PumpkinLogger.CreateLogger("PlayerCache");

    private static readonly Dictionary<Guid, Player> _byUuid = [];

    /// <summary>
    /// Returns the cached owned Player or takes ownership of the incoming handle.
    /// A duplicate owned handle is disposed if it is a separate wrapper.
    /// </summary>
    public static Player GetOrAddOwned(IPlayerImports.Player incoming)
    {
        ArgumentNullException.ThrowIfNull(incoming);

        var uuid = incoming.GetId().ToGuid();

        if (_byUuid.TryGetValue(uuid, out var cached))
        {
            if (!ReferenceEquals(cached.NativePlayer, incoming))
                DisposeNativePlayer(incoming);

            return cached;
        }

        var player = Player.CreateOwned(incoming);
        _byUuid.Add(uuid, player);
        return player;
    }

    public static Player GetBorrowed(IPlayerImports.Player eventPlayer)
    {
        ArgumentNullException.ThrowIfNull(eventPlayer);
        return Player.CreateBorrowed(eventPlayer);
    }

    /// <summary>
    /// Looks up a cached player by UUID without making an FFI call to the host.
    /// </summary>
    public static bool TryGetByUuid(Guid uuid, out Player? player)
    {
        return _byUuid.TryGetValue(uuid, out player);
    }

    /// <summary>
    /// Removes a player by API interface without unnecessary FFI calls.
    /// </summary>
    public static void Remove(IPlayer player)
    {
        Remove(player.Id);
    }

    /// <summary>
    /// Removes a player directly by Guid and safely releases its handle.
    /// </summary>
    public static void Remove(Guid uuid)
    {
        if (_byUuid.Remove(uuid, out var player))
        {
            DisposePlayer(player);
        }
    }

    /// <summary>
    /// Clears the cache when the plugin is unloaded.
    /// </summary>
    public static void Clear()
    {
        foreach (var player in _byUuid.Values)
        {
            DisposePlayer(player);
        }

        _byUuid.Clear();
    }

    private static void DisposePlayer(Player player)
    {
        try
        {
            player.Dispose();
        }
        catch (Exception ex)
        {
            Logger.Error($"Failed to release player handle: {ex}");
        }
    }

    private static void DisposeNativePlayer(IPlayerImports.Player nativePlayer)
    {
        try
        {
            nativePlayer.Dispose();
        }
        catch (ObjectDisposedException) {}
        catch (Exception ex)
        {
            Logger.Error($"Failed to release handle: {ex}");
        }
    }
}