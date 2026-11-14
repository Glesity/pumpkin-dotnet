using System.Text.Json.Serialization;

namespace Pumpkin.DotNet.Api.Text;

public sealed record TextComponent : Component
{
    public string Content { get; init; } = string.Empty;

    public TextComponent(string content)
    {
        Content = content ?? string.Empty;
    }

    public TextComponent(string content, TextColor color) : this(content)
    {
        Style = TextStyle.Empty.WithColor(color);
    }

    public TextComponent(string content, TextStyle style) : this(content)
    {
        Style = style ?? TextStyle.Empty;
    }

    public static implicit operator TextComponent(string text) => new(text);
}