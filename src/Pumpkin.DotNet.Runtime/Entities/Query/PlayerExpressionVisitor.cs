using System.Linq.Expressions;
using System.Reflection;
using Pumpkin.DotNet.Api.Entities;

namespace Pumpkin.DotNet.Runtime.Entities.Query;

internal sealed class PlayerExpressionVisitor : ExpressionVisitor
{
    public string? TargetName { get; private set; }
    public Guid? TargetUuid { get; private set; }
    public int? Limit { get; private set; }
    public string? TerminalMethod { get; private set; }
    public bool HasConflictingFilters { get; private set; }

    private int _whereDepth;

    protected override Expression VisitMethodCall(MethodCallExpression node)
    {
        if (node.Method.DeclaringType != typeof(Queryable))
        {
            throw new NotSupportedException(
                $"Method '{node.Method.DeclaringType?.Name}.{node.Method.Name}' is not supported by the player query provider. " +
                "Use AsEnumerable() to execute arbitrary methods in memory.");
        }

        // Visit the source first (the first argument of Queryable extension methods).
        if (node.Arguments.Count > 0)
        {
            Visit(node.Arguments[0]);
        }

        switch (node.Method.Name)
        {
            case nameof(Queryable.Take) when node.Arguments.Count == 2:
            {
                if (TryEvaluateValue(node.Arguments[1], out var val) && val is int count)
                {
                    if (count < 0)
                        count = 0;

                    Limit = Limit.HasValue ? Math.Min(Limit.Value, count) : count;
                    return node;
                }
                throw new NotSupportedException("The Take() argument must evaluate to an int.");
            }

            case nameof(Queryable.Where) when node.Arguments.Count == 2:
            {
                _whereDepth++;
                try
                {
                    VisitPredicate(node.Arguments[1]);
                }
                finally
                {
                    _whereDepth--;
                }
                return node;
            }

            case nameof(Queryable.First):
            case nameof(Queryable.FirstOrDefault):
            case nameof(Queryable.Single):
            case nameof(Queryable.SingleOrDefault):
            case nameof(Queryable.Any):
            case nameof(Queryable.Count):
            case nameof(Queryable.LongCount):
            {
                TerminalMethod = node.Method.Name;
                if (node.Arguments.Count == 2)
                {
                    _whereDepth++;
                    try
                    {
                        VisitPredicate(node.Arguments[1]);
                    }
                    finally
                    {
                        _whereDepth--;
                    }
                }
                return node;
            }

            default:
                throw new NotSupportedException(
                    $"LINQ operator '{node.Method.Name}' is not supported by the IServer.Players translator. " +
                    "Call AsEnumerable() before using this operator.");
        }
    }

    private void VisitPredicate(Expression expr)
    {
        if (expr is UnaryExpression { NodeType: ExpressionType.Quote } quote)
            expr = quote.Operand;

        if (expr is not LambdaExpression lambda)
            throw new NotSupportedException("Expected a lambda expression in the query predicate.");

        VisitFilterBody(lambda.Body);
    }

    private void VisitFilterBody(Expression body)
    {
        switch (body)
        {
            case BinaryExpression { NodeType: ExpressionType.AndAlso } andExpr:
                VisitFilterBody(andExpr.Left);
                VisitFilterBody(andExpr.Right);
                return;

            case BinaryExpression { NodeType: ExpressionType.Equal } eqExpr:
                if (TryExtractFilter(eqExpr.Left, eqExpr.Right) || TryExtractFilter(eqExpr.Right, eqExpr.Left))
                    return;
                break;
        }

        throw new NotSupportedException(
            $"Condition '{body}' is not supported by the IServer.Players translator (only equality checks on Name and Id joined with && are supported). " +
            "For complex filtering, call AsEnumerable() first.");
    }

    private bool TryExtractFilter(Expression memberExpr, Expression valueExpr)
    {
        if (memberExpr is UnaryExpression { NodeType: ExpressionType.Convert } conv)
            memberExpr = conv.Operand;

        if (memberExpr is not MemberExpression { Expression: ParameterExpression } member)
            return false;

        if (!TryEvaluateValue(valueExpr, out var value))
            return false;

        if (member.Member.Name == nameof(IPlayer.Name) && value is string name)
        {
            if (TargetName is not null && !string.Equals(TargetName, name, StringComparison.Ordinal))
                HasConflictingFilters = true;

            TargetName = name;
            return true;
        }

        if (member.Member.Name == nameof(IPlayer.Id) && value is Guid uuid)
        {
            if (TargetUuid.HasValue && TargetUuid.Value != uuid)
                HasConflictingFilters = true;

            TargetUuid = uuid;
            return true;
        }

        return false;
    }

    private static bool TryEvaluateValue(Expression expr, out object? value)
    {
        switch (expr)
        {
            case ConstantExpression c:
                value = c.Value;
                return true;

            case UnaryExpression { NodeType: ExpressionType.Convert or ExpressionType.ConvertChecked } u:
                if (TryEvaluateValue(u.Operand, out var inner))
                {
                    value = inner is null ? null : Convert.ChangeType(inner, Nullable.GetUnderlyingType(u.Type) ?? u.Type);
                    return true;
                }
                break;

            case MemberExpression m:
            {
                object? target = null;
                if (m.Expression is not null && !TryEvaluateValue(m.Expression, out target))
                    break;

                if (m.Member is FieldInfo field)
                {
                    value = field.GetValue(target);
                    return true;
                }

                if (m.Member is PropertyInfo prop && prop.GetIndexParameters().Length == 0)
                {
                    value = prop.GetValue(target);
                    return true;
                }
                break;
            }
        }

        value = null;
        return false;
    }
}