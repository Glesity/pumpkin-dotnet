using System.Linq.Expressions;
using PluginWorld.wit.Imports.pumpkin.plugin.v0_1_0;
using Pumpkin.DotNet.Api.Entities;
using Pumpkin.DotNet.Runtime.Entities.Players;
using Pumpkin.DotNet.Runtime.Interop;

namespace Pumpkin.DotNet.Runtime.Entities.Query;

internal sealed class PlayerQueryProvider : IQueryProvider
{
    public IQueryable CreateQuery(Expression expression) => 
        new PlayerQueryable(this, expression);

    public IQueryable<TElement> CreateQuery<TElement>(Expression expression)
    {
        if (typeof(TElement) != typeof(IPlayer))
        {
            throw new NotSupportedException(
                $"Query translation is supported only for {nameof(IPlayer)}. " +
                "For projections (Select), materialize the query first with AsEnumerable() or ToList().");
        }

        return (IQueryable<TElement>)(object)new PlayerQueryable(this, expression);
    }

    public object? Execute(Expression expression) => Execute<object>(expression);

    public TResult Execute<TResult>(Expression expression)
    {
        var items = ExecuteCollection(expression);

        if (typeof(TResult) == typeof(IPlayer))
            return (TResult)(object)items.FirstOrDefault()!;

        if (typeof(TResult) == typeof(int))
            return (TResult)(object)items.Count();

        if (typeof(TResult) == typeof(bool))
            return (TResult)(object)items.Any();

        return (TResult)(object)items.FirstOrDefault()!;
    }

    internal IEnumerable<IPlayer> ExecuteCollection(Expression expression)
    {
        var visitor = new PlayerExpressionVisitor();
        visitor.Visit(expression);

        if (visitor.TargetName != null)
        {
            var player = PumpkinRuntime.Instance.Server.GetPlayer(visitor.TargetName);
            if (player is not null)
                return [player];

            return [];
        }

        // 2. Lookup by UUID: p.Uuid == ...
        if (visitor.TargetUuid.HasValue)
        {
            var player = PumpkinRuntime.Instance.Server.GetPlayer(visitor.TargetUuid.Value);
            if (player is not null)
                return [player];

            return [];
        }

        // 3. Fetch all handles from the host.
        var allHandles = PumpkinRuntime.Instance.Server.WitHandle.GetAllPlayers() ?? Enumerable.Empty<IPlayerImports.Player>();
        IEnumerable<IPlayer> players = allHandles.Select(h => (IPlayer)PlayerCache.GetOrAddOwned(h));

        if (visitor.Limit.HasValue)
            players = players.Take(visitor.Limit.Value);

        return players;
    }
}