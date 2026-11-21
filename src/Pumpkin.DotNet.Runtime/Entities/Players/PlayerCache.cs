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
    /// Any duplicate owned handle is immediately disposed.
    /// </summary>  
    public static Player GetOrAddOwned(IPlayerImports.Player incoming)
    {
        ArgumentNullException.ThrowIfNull(incoming);

        var uuid = incoming.GetId().ToGuid();

        if (_byUuid.TryGetValue(uuid, out var cached))
        {
            DisposeNativePlayer(incoming);
            return cached;
        }

        var player = Player.CreateOwned(incoming);
        _byUuid.Add(uuid, player);
        return player;
    }

    /// <summary>
    /// Returns a cached owned <see cref="Player"/> if available, or creates a transient
    /// borrowed wrapper around <paramref name="eventPlayer"/> for the duration of the callback.
    /// </summary>
    /// <remarks>
    /// Do NOT dispose the returned <see cref="Player"/> (e.g. via <c>using</c>): if it came from
    /// the cache, disposing it will invalidate the shared instance for the entire plugin; if it is
    /// borrowed, the host releases the underlying handle automatically when the callback returns.
    /// </remarks>
    public static Player GetOrBorrow(IPlayerImports.Player eventPlayer)
    {
        ArgumentNullException.ThrowIfNull(eventPlayer);
        if (_byUuid.TryGetValue(eventPlayer.GetId().ToGuid(), out var cached))
        {
            return cached;
        }

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