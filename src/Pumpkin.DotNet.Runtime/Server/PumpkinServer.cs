using PluginWorld.wit.Imports.pumpkin.plugin.v0_1_0;
using Pumpkin.DotNet.Api.Entities;
using Pumpkin.DotNet.Api.Server;
using Pumpkin.DotNet.Runtime.Entities.Players;
using Pumpkin.DotNet.Runtime.Entities.Query;
using Pumpkin.DotNet.Runtime.Interop;

namespace Pumpkin.DotNet.Runtime.Server;

internal sealed class PumpkinServer : WitResource<IServerImports.Server>, IServer
{
    private readonly PlayerQueryable _playerQueryable = new(new PlayerQueryProvider());

    private PumpkinServer(IServerImports.Server handle, bool ownsHandle = true) 
        : base(handle, ownsHandle)
    {    
    }

    internal static PumpkinServer CreateOwned(IServerImports.Server server) =>
        new(server, ownsHandle: true);

    internal static PumpkinServer CreateBorrowed(IServerImports.Server server) =>
        new(server, ownsHandle: false);

    public SystemInfo SystemInfo => Handle.GetSysInfo().ToApi();

    public double Mspt => Handle.GetMspt();

    public double Tps => Handle.GetTps();

    public Difficulty Difficulty => Handle.GetDifficulty() switch
    {
        IServerImports.Difficulty.PEACEFUL => Difficulty.Peaceful,
        IServerImports.Difficulty.EASY => Difficulty.Easy,
        IServerImports.Difficulty.NORMAL => Difficulty.Normal,
        IServerImports.Difficulty.HARD => Difficulty.Hard,
        _ => default
    };

    public IQueryable<IPlayer> Players => _playerQueryable;
    
    public IPlayer? GetPlayer(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return null;

        var nativePlayer = Handle.GetPlayerByName(name);

        if (nativePlayer is null)
            return null;
        
        return PlayerCache.GetOrAddOwned(nativePlayer);
    }
    
    public IPlayer? GetPlayer(Guid id)
    {
        if (id == Guid.Empty)
            return null;

        var nativePlayer = Handle.GetPlayerByUuid(id.ToWit());

        if (nativePlayer is null)
            return null;
        
        return PlayerCache.GetOrAddOwned(nativePlayer);
    }

    public void Broadcast(string message)
    {
        Handle.Broadcast(message);
    }
}
