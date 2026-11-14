using Pumpkin.DotNet.Api.Commands;
using Pumpkin.DotNet.Api.Permissions;

namespace Pumpkin.DotNet.Runtime.Commands;

internal sealed class CommandRootModel
{
    public required string[] Names { get; init; }
    public string Description { get; init; } = string.Empty;
    public string Permission { get; init; } = string.Empty;
    public PermissionLevel PermissionLevel { get; init; } = PermissionLevel.Zero;
    public Func<ICommandContext, int>? Executor { get; set; }
    public List<CommandNodeModel> Children { get; } = [];
}