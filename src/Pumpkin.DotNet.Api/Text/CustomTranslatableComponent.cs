namespace Pumpkin.DotNet.Api.Text;

using System;
using System.Collections.Immutable;

public sealed record CustomTranslatableComponent : Component
{
    public string Namespace { get; init; }
    public string Key { get; init; }
    public string Locale { get; init; }
    public ImmutableList<Component> Arguments { get; init; }

    public CustomTranslatableComponent(
        string @namespace,
        string key,
        string locale,
        ImmutableList<Component>? arguments = null,
        TextStyle? style = null)
    {
        Namespace = @namespace ?? string.Empty;
        Key = key ?? string.Empty;
        Locale = locale ?? "en_us";
        Arguments = arguments ?? ImmutableList<Component>.Empty;
        Style = style ?? TextStyle.Empty;
    }

    public CustomTranslatableComponent WithArgument(Component argument)
    {
        ArgumentNullException.ThrowIfNull(argument);
        return this with { Arguments = Arguments.Add(argument) };
    }

    public CustomTranslatableComponent WithArgument(string argument)
        => WithArgument(new TextComponent(argument));
}