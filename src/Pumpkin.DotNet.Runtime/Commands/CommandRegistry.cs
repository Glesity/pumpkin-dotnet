namespace Pumpkin.DotNet.Runtime.Commands;

using System;
using System.Collections.Generic;
using System.Linq;
using PluginWorld.wit.Imports.pumpkin.plugin.v0_1_0;
using Pumpkin.DotNet.Api.Commands;
using Pumpkin.DotNet.Api.Permissions;
using Pumpkin.DotNet.Runtime.Server;

internal sealed class CommandRegistry
{
    public static readonly CommandRegistry Instance = new();

    private sealed record BoundExecutor(Func<ICommandContext, int> Handler, string[] ArgNames);

    private uint _nextHandlerId = 1;
    private readonly Dictionary<uint, BoundExecutor> _executors = [];
    private readonly Dictionary<uint, Func<ICommandContext, string, IEnumerable<string>>> _suggesters = [];
    private readonly Dictionary<uint, Func<ICommandSender, bool>> _requirements = [];

    public void RegisterToHost(IContextImports.Context context, CommandRootModel rootModel, string pluginNamespace)
    {
        var witCommand = new ICommandImports.Command([.. rootModel.Names], rootModel.Description);

        if (rootModel.Executor is not null)
        {
            var executor = rootModel.Executor;
            if (rootModel.PermissionLevel > PermissionLevel.Zero)
            {
                var requiredLevel = rootModel.PermissionLevel;
                var innerExecutor = executor;
                executor = ctx =>
                {
                    if (!ctx.Sender.HasPermissionLevel(requiredLevel))
                        return 0;

                    return innerExecutor(ctx);
                };
            }

            uint execId = RegisterExecutor(executor, []);
            witCommand.ExecuteWithHandlerId(execId);
        }

        foreach (var child in rootModel.Children)
        {
            witCommand.Then(BuildWitNode(child));
        }

        string permNode;
        if (string.IsNullOrWhiteSpace(rootModel.Permission))
        {
            permNode = $"{pluginNamespace}:command.{rootModel.Names[0].ToLowerInvariant()}";
            var defaultPerm = rootModel.PermissionLevel > PermissionLevel.Zero
                ? IPermissionImports.PermissionDefault.Op((IPermissionImports.PermissionLevel)(byte)rootModel.PermissionLevel)
                : IPermissionImports.PermissionDefault.Allow();

            context.RegisterPermission(
                new IPermissionImports.Permission(
                    node: permNode,
                    description: string.IsNullOrWhiteSpace(rootModel.Description) ? "Command permission" : rootModel.Description,
                    @default: defaultPerm,
                    children: []
                )
            );
        }
        else
        {
            permNode = rootModel.Permission.Contains(':')
                ? rootModel.Permission
                : $"{pluginNamespace}:{rootModel.Permission}";
        }

        context.RegisterCommand(witCommand, permNode);
    }

    private ICommandImports.CommandNode BuildWitNode(CommandNodeModel model)
    {
        var node = model.IsLiteral
            ? ICommandImports.CommandNode.Literal(model.Name)
            : ICommandImports.CommandNode.Argument(model.Name, model.WitArgType!);

        if (model.PermissionLevel > PermissionLevel.Zero)
        {
            uint reqId = RegisterRequirement(sender => sender.HasPermissionLevel(model.PermissionLevel));
            node.RequireWithHandlerId(reqId);
        }

        if (model.Executor is not null)
        {
            uint execId = RegisterExecutor(model.Executor, model.PathArgNames);
            node.ExecuteWithHandlerId(execId);
        }

        if (model.Suggester is not null)
        {
            uint sugId = RegisterSuggester(model.Suggester);
            node.SuggestWithHandlerId(sugId);
        }

        foreach (var child in model.Children)
        {
            var witChild = BuildWitNode(child);
            node.Then(witChild); // The node takes ownership of witChild.
        }

        return node;
    }

    private uint RegisterExecutor(Func<ICommandContext, int> handler, IReadOnlyList<string> argNames)
    {
        uint id = _nextHandlerId++;
        _executors[id] = new BoundExecutor(handler, [.. argNames]);
        return id;
    }

    private uint RegisterSuggester(Func<ICommandContext, string, IEnumerable<string>> handler)
    {
        uint id = _nextHandlerId++;
        _suggesters[id] = handler;
        return id;
    }

    private uint RegisterRequirement(Func<ICommandSender, bool> predicate)
    {
        uint id = _nextHandlerId++;
        _requirements[id] = predicate;
        return id;
    }

    public bool DispatchRequirement(
        uint handlerId,
        ICommandImports.CommandSender nativeSender,
        PumpkinServer server)
    {
        if (!_requirements.TryGetValue(handlerId, out var predicate))
            return false;

        var sender = new CommandSenderWrapper(nativeSender, server);
        return predicate(sender);
    }

    public int DispatchExecute(
        uint commandId,
        ICommandImports.CommandSender nativeSender,
        PumpkinServer server,
        ICommandImports.ConsumedArgs nativeArgs)
    {
        if (!_executors.TryGetValue(commandId, out var bound))
            return 0;

        var sender = new CommandSenderWrapper(nativeSender, server);
        var extractedArgs = new Dictionary<string, object?>(bound.ArgNames.Length, StringComparer.OrdinalIgnoreCase);

        foreach (var argName in bound.ArgNames)
        {
            try
            {
                var rawWitArg = nativeArgs.GetValue(argName);
                extractedArgs[argName] = WitArgumentMapper.ExtractValue(rawWitArg);
            }
            catch
            {
                // The optional argument was not provided on this branch.
            }
        }

        var context = new CommandContext(sender, extractedArgs);
        return bound.Handler(context);
    }

    public ICommandImports.CommandSuggestions DispatchSuggestion(
        uint handlerId,
        ICommandImports.CommandSender nativeSender,
        PumpkinServer server,
        ICommandImports.SuggestionRequest request)
    {
        if (!_suggesters.TryGetValue(handlerId, out var suggester))
        {
            return new ICommandImports.CommandSuggestions(request.start, (uint)request.remaining.Length, []);
        }

        var sender = new CommandSenderWrapper(nativeSender, server);
        var context = new CommandContext(sender, new Dictionary<string, object?>());

        var items = suggester(context, request.remaining)
            .Select(val => new ICommandImports.CommandSuggestion(val, null))
            .ToList();

        return new ICommandImports.CommandSuggestions(
            request.start,
            (uint)request.remaining.Length,
            items
        );
    }

    public void Clear()
    {
        _executors.Clear();
        _requirements.Clear();
        _suggesters.Clear();
    }
}