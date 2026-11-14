using Pumpkin.DotNet.Api.Permissions;

namespace Pumpkin.DotNet.Api.Commands;

public abstract class Command
{
    public string Name { get; }
    public string[] Aliases { get; init; } = [];
    public string Description { get; init; } = string.Empty;
    public string Permission { get; init; } = string.Empty;
    public PermissionLevel PermissionLevel { get; init; } = PermissionLevel.Zero;

    protected Command(string name) => Name = name.TrimStart('/');

    public abstract void OnExecute(ICommandContext context);
    public virtual IEnumerable<string> OnSuggest(ICommandContext context, string currentArg) => [];

    public static CommandBuilder Create(string name, params string[] aliases)
        => new(name, aliases);

    public static ArgumentBuilder<T> Argument<T>(string name)
        => new(name);

    public static LiteralBuilder Literal(string name)
        => new(name);
}