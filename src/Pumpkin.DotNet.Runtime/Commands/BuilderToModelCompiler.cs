namespace Pumpkin.DotNet.Runtime.Commands;

using System.Collections.Generic;
using Pumpkin.DotNet.Api.Commands;

internal static class BuilderToModelCompiler
{
    public static CommandRootModel Compile(CommandBuilder builder)
    {
        var root = new CommandRootModel
        {
            Names = builder.Names,
            Description = builder.Description,
            Permission = builder.Permission,
            PermissionLevel = builder.PermissionLevel,
            Executor = builder.Executor
        };

        foreach (var child in builder.Children)
        {
            root.Children.Add(CompileNode(child, []));
        }

        return root;
    }

    private static CommandNodeModel CompileNode(NodeBuilder node, List<string> parentArgs)
    {
        var currentArgs = new List<string>(parentArgs);
        bool isLiteral = node is LiteralBuilder;

        if (!isLiteral)
        {
            currentArgs.Add(node.Name);
        }

        var model = new CommandNodeModel
        {
            Name = node.Name,
            IsLiteral = isLiteral,
            WitArgType = node is IArgumentNode argNode
                ? WitArgumentMapper.MapType(argNode.ValueType, argNode.Constraints)
                : null,
            PermissionLevel = node.PermissionLevel,
            Executor = node.Executor,
            Suggester = node.Suggester
        };
        model.PathArgNames.AddRange(currentArgs);

        foreach (var child in node.Children)
        {
            model.Children.Add(CompileNode(child, currentArgs));
        }

        return model;
    }
}