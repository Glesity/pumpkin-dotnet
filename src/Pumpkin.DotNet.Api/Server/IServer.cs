using Pumpkin.DotNet.Api.Entities;

namespace Pumpkin.DotNet.Api.Server;

public interface IServer
{
    // ==========================================
    // System
    // ==========================================

    SystemInfo SystemInfo { get; }


    // ==========================================
    // Metrics
    // ==========================================

    double Mspt { get; }

    double Tps { get; }


    // ==========================================
    // World
    // ==========================================

    Difficulty Difficulty { get; }


    // ==========================================
    // Players
    // ==========================================

    IPlayer? GetPlayer(string name);
    IPlayer? GetPlayer(Guid id);
    IQueryable<IPlayer> Players { get; }


    void Broadcast(string message);
}
