namespace Pumpkin.DotNet.Runtime.Commands;

using System;
using System.Collections.Generic;
using PluginWorld.wit.Imports.pumpkin.plugin.v0_1_0;
using Pumpkin.DotNet.Api.Commands;

internal static class LegacyCommandAdapter
{
    public static CommandRootModel ToRootModel(Command command)
    {
        var names = new List<string>(1 + command.Aliases.Length) { command.Name };
        names.AddRange(command.Aliases);

        var root = new CommandRootModel
        {
            Names = [.. names],
            Description = command.Description,
            Permission = command.Permission,
            PermissionLevel = command.PermissionLevel,
            Executor = ctx =>
            {
                command.OnExecute(ctx);
                return ctx.Result;
            }
        };

        var argsNode = new CommandNodeModel
        {
            Name = "args",
            IsLiteral = false,
            WitArgType = ICommandImports.ArgumentType.@string(ICommandImports.StringType.GREEDY),
            Executor = ctx =>
            {
                command.OnExecute(ctx);
                return ctx.Result;
            },
            Suggester = command.OnSuggest
        };
        argsNode.PathArgNames.Add("args");

        root.Children.Add(argsNode);
        return root;
    }
}