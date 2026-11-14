namespace Pumpkin.DotNet.Api.Text;

using System;
using System.Collections.Immutable;

public sealed record TranslatableComponent : Component
{
    public string Key { get; init; }
    public string? BedrockKey { get; init; }
    public string? Fallback { get; init; }
    public ImmutableList<Component> Arguments { get; init; }

    public TranslatableComponent(
        string key, 
        ImmutableList<Component>? arguments = null, 
        string? fallback = null, 
        string? bedrockKey = null,
        TextStyle? style = null) 
    {
        Key = key ?? string.Empty;
        BedrockKey = bedrockKey;
        Fallback = fallback;
        Arguments = arguments ?? ImmutableList<Component>.Empty;
        Style = style ?? TextStyle.Empty;
    }

    /// <summary>
    /// Adds a positional argument to insert into the string (the "with" parameter).
    /// </summary>
    public TranslatableComponent WithArgument(Component argument)
    {
        ArgumentNullException.ThrowIfNull(argument);
        return this with { Arguments = Arguments.Add(argument) };
    }

    public TranslatableComponent WithArgument(string argument)
        => WithArgument(new TextComponent(argument));
}