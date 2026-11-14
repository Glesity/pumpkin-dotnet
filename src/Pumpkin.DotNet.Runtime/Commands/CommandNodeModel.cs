using PluginWorld.wit.Imports.pumpkin.plugin.v0_1_0;
using Pumpkin.DotNet.Api.Commands;
using Pumpkin.DotNet.Api.Permissions;

namespace Pumpkin.DotNet.Runtime.Commands;

internal sealed class CommandNodeModel
{
    public required string Name { get; init; }
    public bool IsLiteral { get; init; }
    public ICommandImports.ArgumentType? WitArgType { get; init; }
    public PermissionLevel PermissionLevel { get; init; } = PermissionLevel.Zero;

    public Func<ICommandContext, int>? Executor { get; set; }
    public Func<ICommandContext, string, IEnumerable<string>>? Suggester { get; set; }

    public List<string> PathArgNames { get; } = [];
    public List<CommandNodeModel> Children { get; } = [];
}