using System.Collections.Immutable;

namespace Pumpkin.DotNet.Api.Text;

public sealed record TextStyle
{
    public static readonly TextStyle Empty = new();

    public TextColor? Color { get; init; }
    public bool? Bold { get; init; }
    public bool? Italic { get; init; }
    public bool? Underlined { get; init; }
    public bool? Strikethrough { get; init; }
    public bool? Obfuscated { get; init; }
    public string? Font { get; init; }
    public string? Insertion { get; init; }
    public uint? ShadowColorArgb { get; init; }

    public ClickEvent? Click { get; init; }
    public HoverEvent? Hover { get; init; }

    // --- Gradient and rainbow fields ---
    public TextColor[]? GradientColors { get; init; }
    public bool IsRainbow { get; init; }

    // --- Style update methods (with) ---
    public TextStyle WithColor(TextColor color) => this with { Color = color, GradientColors = null, IsRainbow = false };
    
    public TextStyle WithGradient(params TextColor[] colors) => this with { GradientColors = colors, IsRainbow = false, Color = null };
    
    public TextStyle WithRainbow() => this with { IsRainbow = true, GradientColors = null, Color = null };

    public TextStyle WithBold(bool state = true) => this with { Bold = state };
    public TextStyle WithItalic(bool state = true) => this with { Italic = state };
    public TextStyle WithUnderlined(bool state = true) => this with { Underlined = state };
    public TextStyle WithStrikethrough(bool state = true) => this with { Strikethrough = state };
    public TextStyle WithObfuscated(bool state = true) => this with { Obfuscated = state };
    public TextStyle WithFont(string font) => this with { Font = font };
    public TextStyle WithInsertion(string insertion) => this with { Insertion = insertion };
    public TextStyle WithShadowColor(uint argb) => this with { ShadowColorArgb = argb };
    public TextStyle WithClick(ClickAction action, string value) => this with { Click = new ClickEvent(action, value) };
    public TextStyle WithHover(HoverEvent hover) => this with { Hover = hover };
}

public enum ClickAction
{
    OpenUrl,
    OpenFile,
    RunCommand,
    SuggestCommand,
    ChangePage,
    CopyToClipboard
}

public readonly record struct ClickEvent(ClickAction Action, string Value);

public abstract record HoverEvent
{
    public sealed record ShowText(Component Text) : HoverEvent;
    public sealed record ShowItem(string Snbt) : HoverEvent;
    public sealed record ShowEntity(string EntityType, string Id, Component? Name = null) : HoverEvent;
}