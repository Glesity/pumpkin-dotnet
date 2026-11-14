using System.Collections;
using System.Linq.Expressions;
using Pumpkin.DotNet.Api.Entities;

namespace Pumpkin.DotNet.Runtime.Entities.Query;

internal sealed class PlayerQueryable : IOrderedQueryable<IPlayer>
{
    public PlayerQueryProvider Provider { get; }
    public Expression Expression { get; }

    public PlayerQueryable(PlayerQueryProvider provider, Expression? expression = null)
    {
        Provider = provider;
        Expression = expression ?? Expression.Constant(this);
    }

    public Type ElementType => typeof(IPlayer);
    IQueryProvider IQueryable.Provider => Provider;

    public IEnumerator<IPlayer> GetEnumerator() => 
        Provider.ExecuteCollection(Expression).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}