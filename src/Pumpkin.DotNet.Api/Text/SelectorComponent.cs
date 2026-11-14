namespace Pumpkin.DotNet.Api.Text;

public sealed record SelectorComponent : Component
{
    public string Pattern { get; init; }
    public Component? Separator { get; init; }

    public SelectorComponent(string selector, Component? separator = null, TextStyle? style = null)
    {
        Pattern = selector ?? string.Empty;
        Separator = separator;
        Style = style ?? TextStyle.Empty;
    }

    public override string ToString() => Pattern;
}