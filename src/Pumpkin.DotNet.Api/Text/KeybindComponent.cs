namespace Pumpkin.DotNet.Api.Text;

public sealed record KeybindComponent : Component
{
    public string Key { get; init; }

    public KeybindComponent(string keybind, TextStyle? style = null)
    {
        Key = keybind ?? string.Empty;
        Style = style ?? TextStyle.Empty;
    }

    public override string ToString() => Key;
}