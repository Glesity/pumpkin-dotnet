namespace Pumpkin.DotNet.Api.Text;

using System;
using System.Collections.Immutable;
using System.Linq;
using System.Text;

public abstract record Component
{
    public TextStyle Style { get; init; } = TextStyle.Empty;
    public ImmutableList<Component> Children { get; init; } = ImmutableList<Component>.Empty;

    // --- Styling ---
    public Component WithStyle(TextStyle style) => this with { Style = style ?? TextStyle.Empty };
    public Component WithColor(TextColor color) => this with { Style = Style.WithColor(color) };
    public Component Gradient(params TextColor[] colors) => this with { Style = Style.WithGradient(colors) };
    public Component Rainbow() => this with { Style = Style.WithRainbow() };

    public Component Bold(bool state = true) => this with { Style = Style.WithBold(state) };
    public Component Italic(bool state = true) => this with { Style = Style.WithItalic(state) };
    public Component Underlined(bool state = true) => this with { Style = Style.WithUnderlined(state) };
    public Component Strikethrough(bool state = true) => this with { Style = Style.WithStrikethrough(state) };
    public Component Obfuscated(bool state = true) => this with { Style = Style.WithObfuscated(state) };

    public Component Insertion(string text) => this with { Style = Style.WithInsertion(text) };
    public Component Font(string font) => this with { Style = Style.WithFont(font) };
    public Component ShadowColor(uint argb) => this with { Style = Style.WithShadowColor(argb) };

    // --- Click and hover events ---
    public Component ClickOpenUrl(string url) => this with { Style = Style.WithClick(ClickAction.OpenUrl, url) };
    public Component ClickOpenFile(string path) => this with { Style = Style.WithClick(ClickAction.OpenFile, path) };
    public Component ClickRunCommand(string command) => this with { Style = Style.WithClick(ClickAction.RunCommand, command) };
    public Component ClickSuggestCommand(string command) => this with { Style = Style.WithClick(ClickAction.SuggestCommand, command) };
    public Component ClickChangePage(uint page) => this with { Style = Style.WithClick(ClickAction.ChangePage, page.ToString()) };
    public Component ClickCopy(string text) => this with { Style = Style.WithClick(ClickAction.CopyToClipboard, text) };

    public Component HoverShowText(Component text) => this with { Style = Style.WithHover(new HoverEvent.ShowText(text)) };
    public Component HoverShowItem(string snbt) => this with { Style = Style.WithHover(new HoverEvent.ShowItem(snbt)) };
    public Component HoverShowEntity(string entityType, string id, Component? name = null) 
        => this with { Style = Style.WithHover(new HoverEvent.ShowEntity(entityType, id, name)) };

    // --- Child hierarchy ---
    public Component Append(Component child)
    {
        ArgumentNullException.ThrowIfNull(child);
        return this with { Children = Children.Add(child) };
    }

    public Component Append(string text, Func<TextStyle, TextStyle>? styleConfig = null)
    {
        var style = styleConfig != null ? styleConfig(TextStyle.Empty) : TextStyle.Empty;
        return Append(new TextComponent(text, style));
    }

    // --- Plain text extraction ---
    public string ToPlainText()
    {
        var sb = new StringBuilder();
        AppendPlainText(this, sb);
        return sb.ToString();

        static void AppendPlainText(Component node, StringBuilder builder)
        {
            switch (node)
            {
                case TextComponent tc:
                    builder.Append(tc.Content);
                    break;
                case TranslatableComponent tr:
                    builder.Append(tr.Fallback ?? tr.Key);
                    break;
                case KeybindComponent kc:
                    builder.Append($"[{kc.Key}]");
                    break;
                case SelectorComponent sc:
                    builder.Append(sc.Pattern);
                    break;
            }

            foreach (var child in node.Children)
            {
                AppendPlainText(child, builder);
            }
        }
    }

    // --- Text factories ---
    public static TextComponent Text(string plain) => new(plain);
    public static TextComponent Text(string plain, TextColor color) => new(plain, color);
    public static TextComponent Text(string plain, TextStyle style) => new(plain, style);
    public static TextComponent Empty => new(string.Empty);
    public static TextComponent NewLine { get; } = new("\n");
    public static TextComponent Space { get; } = new(" ");

    public Component AppendNewLine() => Append(NewLine);
    public Component AppendSpace() => Append(Space);

    // --- Translation factories ---
    public static TranslatableComponent Translatable(string key) 
        => new(key);

    public static TranslatableComponent Translatable(string key, params Component[] args) 
        => new(key, args.ToImmutableList());

    public static TranslatableComponent Translatable(string key, params string[] args) 
        => new(key, args.Select(a => (Component)new TextComponent(a)).ToImmutableList());

    public static TranslatableComponent Translatable(string key, string? fallback, params Component[] args) 
        => new(key, args.ToImmutableList(), fallback: fallback);

    public static TranslatableComponent Translatable(string key, string? fallback, params string[] args) 
        => new(key, args.Select(a => (Component)new TextComponent(a)).ToImmutableList(), fallback: fallback);

    public static TranslatableComponent TranslatableCross(string javaKey, string bedrockKey, params Component[] args) 
        => new(javaKey, args.ToImmutableList(), bedrockKey: bedrockKey);

    public static TranslatableComponent TranslatableCross(string javaKey, string bedrockKey, params string[] args) 
        => new(javaKey, args.Select(a => (Component)new TextComponent(a)).ToImmutableList(), bedrockKey: bedrockKey);

    // --- Keybind and selector factories ---
    public static KeybindComponent Keybind(string keybind) => new(keybind);
    public static KeybindComponent Keybind(string keybind, TextStyle style) => new(keybind, style);

    public static SelectorComponent Selector(string selector, Component? separator = null) => new(selector, separator);
    public static SelectorComponent Selector(string selector, TextStyle style) => new(selector, null, style);

    public static CustomTranslatableComponent CustomTranslate(string @namespace, string key, string locale, params Component[] args)
        => new(@namespace, key, locale, args.ToImmutableList());

    public static CustomTranslatableComponent CustomTranslate(string @namespace, string key, string locale, params string[] args)
        => new(@namespace, key, locale, args.Select(a => (Component)new TextComponent(a)).ToImmutableList());

    // --- Operators ---
    public static Component operator +(Component left, Component right) => left.Append(right);
    public static Component operator +(Component left, string right) => left.Append(new TextComponent(right));
    public static implicit operator Component(string text) => new TextComponent(text);
}