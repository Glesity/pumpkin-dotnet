using Pumpkin.DotNet.Api.Permissions;

namespace Pumpkin.DotNet.Api.Commands;

[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class CommandAttribute : Attribute
{
    public string Name { get; }
    public string[] Aliases { get; init; } = [];
    public string Description { get; init; } = string.Empty;
    public string Permission { get; init; } = string.Empty;
    public PermissionLevel PermissionLevel { get; init; } = PermissionLevel.Zero;

    public CommandAttribute(string name, params string[] aliases)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Name = name.TrimStart('/');
        Aliases = aliases;
    }
}