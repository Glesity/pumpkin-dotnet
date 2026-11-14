namespace Pumpkin.DotNet.Api.Text;

[Flags]
public enum TextDecorations : byte
{
    None          = 0,
    Bold          = 1 << 0,
    Italic        = 1 << 1,
    Underlined    = 1 << 2,
    Strikethrough = 1 << 3,
    Obfuscated    = 1 << 4
}