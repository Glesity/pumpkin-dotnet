namespace Pumpkin.DotNet.Api.Commands;

using System;
using System.Collections.Generic;
using Pumpkin.DotNet.Api.Permissions;

public sealed class CommandBuilder
{
    internal string[] Names { get; }
    internal string Description { get; private set; } = string.Empty;
    internal string Permission { get; private set; } = string.Empty;
    internal PermissionLevel PermissionLevel { get; private set; } = PermissionLevel.Zero;
    internal Func<ICommandContext, int>? Executor { get; private set; }
    internal List<NodeBuilder> Children { get; } = [];

    internal CommandBuilder(string name, string[] aliases)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Names = [name.TrimStart('/'), .. aliases];
    }

    public CommandBuilder WithDescription(string description) { Description = description; return this; }
    public CommandBuilder WithPermission(string permission) { Permission = permission; return this; }
    public CommandBuilder Requires(PermissionLevel level) { PermissionLevel = level; return this; }

    public CommandBuilder Executes(Action<ICommandContext> action)
    {
        Executor = ctx => { action(ctx); return ctx.Result; };
        return this;
    }

    public CommandBuilder Executes(Func<ICommandContext, int> handler)
    {
        Executor = handler;
        return this;
    }

    public CommandBuilder Then(NodeBuilder node)
    {
        Children.Add(node);
        return this;
    }
}

public abstract class NodeBuilder
{
    internal string Name { get; }
    internal PermissionLevel PermissionLevel { get; private set; } = PermissionLevel.Zero;
    internal Func<ICommandContext, int>? Executor { get; private set; }
    internal Func<ICommandContext, string, IEnumerable<string>>? Suggester { get; private set; }
    internal List<NodeBuilder> Children { get; } = [];

    protected NodeBuilder(string name) => Name = name;

    public NodeBuilder Requires(PermissionLevel level) { PermissionLevel = level; return this; }

    public NodeBuilder Executes(Action<ICommandContext> action)
    {
        Executor = ctx => { action(ctx); return ctx.Result; };
        return this;
    }

    public NodeBuilder Executes(Func<ICommandContext, int> handler)
    {
        Executor = handler;
        return this;
    }

    public NodeBuilder Suggests(Func<ICommandContext, string, IEnumerable<string>> suggester)
    {
        Suggester = suggester;
        return this;
    }

    public NodeBuilder Then(NodeBuilder node)
    {
        Children.Add(node);
        return this;
    }
}

public sealed class LiteralBuilder : NodeBuilder
{
    internal LiteralBuilder(string name) : base(name) { }
}

public sealed class ArgumentBuilder<T> : NodeBuilder, IArgumentNode
{
    public Type ValueType => typeof(T);
    internal ArgAttribute? Constraints { get; private set; }

    ArgAttribute? IArgumentNode.Constraints => Constraints;

    internal ArgumentBuilder(string name) : base(name) { }

    public ArgumentBuilder<T> WithConstraints(double min = double.NaN, double max = double.NaN, StringArgMode stringMode = StringArgMode.SingleWord)
    {
        Constraints = new ArgAttribute { Min = min, Max = max, StringMode = stringMode };
        return this;
    }
}