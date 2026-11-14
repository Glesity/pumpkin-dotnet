using Pumpkin.DotNet.Api.Permissions;

namespace Pumpkin.DotNet.Api.Commands;

[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, Inherited = false)]
public sealed class SubcommandAttribute : Attribute
{
    public string Name { get; }
    public PermissionLevel PermissionLevel { get; init; } = PermissionLevel.Zero;

    public SubcommandAttribute(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Name = name;
    }
}