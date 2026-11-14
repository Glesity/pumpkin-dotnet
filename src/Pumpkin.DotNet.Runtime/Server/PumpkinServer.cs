using PluginWorld.wit.Imports.pumpkin.plugin.v0_1_0;
using Pumpkin.DotNet.Api.Entities;
using Pumpkin.DotNet.Api.Server;
using Pumpkin.DotNet.Runtime.Entities;
using Pumpkin.DotNet.Runtime.Entities.Players;
using Pumpkin.DotNet.Runtime.Entities.Query;
using Pumpkin.DotNet.Runtime.Interop;

namespace Pumpkin.DotNet.Runtime.Server;

internal class PumpkinServer : IServer
{
    public IServerImports.Server NativeServer => PumpkinRuntime.Instance.NativeServer;
    private readonly PlayerQueryable _playerQuerable = new(new PlayerQueryProvider());

    public SystemInfo SystemInfo => NativeServer.GetSysInfo().ToApi();

    public double Mspt => NativeServer.GetMspt();

    public double Tps => NativeServer.GetTps();

    public Difficulty Difficulty => NativeServer.GetDifficulty() switch
    {
        IServerImports.Difficulty.PEACEFUL => Difficulty.Peaceful,
        IServerImports.Difficulty.EASY => Difficulty.Easy,
        IServerImports.Difficulty.NORMAL => Difficulty.Normal,
        IServerImports.Difficulty.HARD => Difficulty.Hard,
        _ => default
    };

    public IQueryable<IPlayer> Players => _playerQuerable;
    
    public IPlayer? GetPlayer(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return null;

        var nativePlayer = NativeServer.GetPlayerByName(name);

        if (nativePlayer is null)
            return null;
        
        return PlayerCache.GetOrAddOwned(nativePlayer);
    }
    
    public IPlayer? GetPlayer(Guid id)
    {
        if (id == Guid.Empty)
            return null;

        var nativePlayer = NativeServer.GetPlayerByUuid(id.ToWit());

        if (nativePlayer is null)
            return null;
        
        return PlayerCache.GetOrAddOwned(nativePlayer);
    }

    public void Broadcast(string message)
    {
        NativeServer.Broadcast(message);
    }
}
