namespace Pumpkin.DotNet.Runtime.Text;

using System;
using System.Buffers;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using PluginWorld.wit.Imports.pumpkin.plugin.v0_1_0;
using Pumpkin.DotNet.Api.Text;

internal static class ComponentBridge
{
    private static readonly JsonWriterOptions WriterOptions = new()
    {
        Indented = false,
        SkipValidation = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    /// <summary>
    /// Parses a Minecraft Chat Component JSON string into a tree of C# objects.
    /// </summary>
    public static Component FromJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return Component.Empty;

        using var doc = JsonDocument.Parse(json);
        return ParseElement(doc.RootElement);
    }

    /// <summary>
    /// Serializes a Component tree to a Minecraft Chat Component JSON string
    /// compatible with the Pumpkin Rust host's serde representation.
    /// </summary>
    public static string ToJson(Component? root)
    {
        if (root is null)
            return "{\"text\":\"\"}";

        // Expand gradients and rainbows before exporting to Minecraft JSON.
        root = GradientResolver.FlattenGradients(root);

        var buffer = new ArrayBufferWriter<byte>(512);
        using (var writer = new Utf8JsonWriter(buffer, WriterOptions))
        {
            WriteComponent(writer, root);
        }

        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    /// <summary>
    /// Creates a C# Component from a Minecraft legacy string (using '§' or '&' codes)
    /// with the native Pumpkin host parser.
    /// </summary>
    public static Component FromLegacy(string? legacy, char symbol = '§')
    {
        if (string.IsNullOrEmpty(legacy))
            return Component.Empty;

        using var native = symbol == '§'
            ? ITextImports.TextComponent.FromLegacyString(legacy)
            : ITextImports.TextComponent.FromLegacyStringWithCode(legacy, symbol);

        return FromJson(native.ToJson());
    }

    private static void WriteComponent(Utf8JsonWriter writer, Component comp)
    {
        // Delegate building and resolving server-side custom translations to the host through WIT.
        if (comp is CustomTranslatableComponent custom)
        {
            WriteCustomTranslatable(writer, custom);
            return;
        }

        writer.WriteStartObject();

        // 1. Polymorphic node content (exactly one variant of the host's untagged TextContent enum).
        switch (comp)
        {
            case TranslatableComponent trans:
                writer.WriteString("translate", trans.Key);
                if (trans.Fallback != null)
                {
                    writer.WriteString("fallback", trans.Fallback);
                }

                if (!trans.Arguments.IsEmpty)
                {
                    writer.WriteStartArray("with");
                    foreach (var arg in trans.Arguments)
                    {
                        WriteComponent(writer, arg);
                    }
                    writer.WriteEndArray();
                }
                break;

            case KeybindComponent keybind:
                writer.WriteString("keybind", keybind.Key);
                break;

            case SelectorComponent sel:
                writer.WriteString("selector", sel.Pattern);
                if (sel.Separator != null)
                {
                    // The Pumpkin WIT/Rust host expects separator as a string (Option<String>).
                    string sepStr = sel.Separator is TextComponent textSep 
                        ? textSep.Content 
                        : sel.Separator.ToString() ?? string.Empty;
                    writer.WriteString("separator", sepStr);
                }
                break;

            case TextComponent textNode:
                writer.WriteString("text", textNode.Content ?? string.Empty);
                break;

            default:
                writer.WriteString("text", string.Empty);
                break;
        }

        var style = comp.Style;

        // 2. Color (hex or named).
        if (style.Color.HasValue)
        {
            writer.WriteString("color", style.Color.Value.IsNamed 
                ? style.Color.Value.Name 
                : style.Color.Value.Hex);
        }

        // 3. Text style modifiers.
        if (style.Bold == true) writer.WriteBoolean("bold", true);
        if (style.Italic == true) writer.WriteBoolean("italic", true);
        if (style.Underlined == true) writer.WriteBoolean("underlined", true);
        if (style.Strikethrough == true) writer.WriteBoolean("strikethrough", true);
        if (style.Obfuscated == true) writer.WriteBoolean("obfuscated", true);

        if (!string.IsNullOrEmpty(style.Font)) writer.WriteString("font", style.Font);
        if (!string.IsNullOrEmpty(style.Insertion)) writer.WriteString("insertion", style.Insertion);

        // 4. Text shadow [A, R, G, B].
        if (style.ShadowColorArgb.HasValue)
        {
            uint argb = style.ShadowColorArgb.Value;
            writer.WriteStartArray("shadow_color");
            writer.WriteNumberValue((byte)((argb >> 24) & 0xFF));
            writer.WriteNumberValue((byte)((argb >> 16) & 0xFF));
            writer.WriteNumberValue((byte)((argb >> 8) & 0xFF));
            writer.WriteNumberValue((byte)(argb & 0xFF));
            writer.WriteEndArray();
        }

        // 5. Click Event
        if (style.Click.HasValue && !string.IsNullOrEmpty(style.Click.Value.Value))
        {
            var click = style.Click.Value;
            writer.WriteStartObject("click_event");
            switch (click.Action)
            {
                case ClickAction.OpenUrl:
                    writer.WriteString("action", "open_url");
                    writer.WriteString("url", click.Value);
                    break;
                case ClickAction.OpenFile:
                    writer.WriteString("action", "open_file");
                    writer.WriteString("path", click.Value);
                    break;
                case ClickAction.RunCommand:
                    writer.WriteString("action", "run_command");
                    writer.WriteString("command", click.Value);
                    break;
                case ClickAction.SuggestCommand:
                    writer.WriteString("action", "suggest_command");
                    writer.WriteString("command", click.Value);
                    break;
                case ClickAction.ChangePage:
                    writer.WriteString("action", "change_page");
                    if (uint.TryParse(click.Value, out uint page))
                        writer.WriteNumber("page", page);
                    else
                        writer.WriteNumber("page", 1);
                    break;
                case ClickAction.CopyToClipboard:
                    writer.WriteString("action", "copy_to_clipboard");
                    writer.WriteString("value", click.Value);
                    break;
            }
            writer.WriteEndObject();
        }

        // 6. Hover event (sequence arrays are required for show_text and show_entity.name by host serde).
        if (style.Hover != null)
        {
            writer.WriteStartObject("hover_event");
            switch (style.Hover)
            {
                case HoverEvent.ShowText textHover:
                    writer.WriteString("action", "show_text");
                    writer.WriteStartArray("value");
                    WriteComponent(writer, textHover.Text);
                    writer.WriteEndArray();
                    break;

                case HoverEvent.ShowItem itemHover:
                    writer.WriteString("action", "show_item");
                    writer.WriteString("id", itemHover.Snbt);
                    break;

                case HoverEvent.ShowEntity entHover:
                    writer.WriteString("action", "show_entity");
                    writer.WriteString("id", entHover.EntityType);
                    writer.WriteString("uuid", entHover.Id);
                    if (entHover.Name != null)
                    {
                        writer.WriteStartArray("name");
                        WriteComponent(writer, entHover.Name);
                        writer.WriteEndArray();
                    }
                    break;
            }
            writer.WriteEndObject();
        }

        // 7. Extra / child nodes.
        if (!comp.Children.IsEmpty)
        {
            writer.WriteStartArray("extra");
            foreach (var child in comp.Children)
            {
                WriteComponent(writer, child);
            }
            writer.WriteEndArray();
        }

        writer.WriteEndObject();
    }

    private static void WriteCustomTranslatable(Utf8JsonWriter writer, CustomTranslatableComponent custom)
    {
        var witArgs = new List<ITextImports.TextComponent>(custom.Arguments.Count);
        foreach (var arg in custom.Arguments)
        {
            string argJson = ToJson(arg);
            witArgs.Add(ITextImports.TextComponent.FromJson(argJson));
        }

        using var nativeCustom = ITextImports.TextComponent.Custom(
            custom.Namespace,
            custom.Key,
            custom.Locale,
            witArgs
        );

        // Apply the C# node styles to the resolved component by parsing it again.
        var resolved = FromJson(nativeCustom.ToJson());
        var mergedChildren = resolved.Children.AddRange(custom.Children);
        var styledResolved = resolved with
        {
            Style = custom.Style != TextStyle.Empty ? custom.Style : resolved.Style,
            Children = mergedChildren
        };

        WriteComponent(writer, styledResolved);
    }

    private static Component ParseElement(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.String)
            return new TextComponent(element.GetString() ?? string.Empty);

        if (element.ValueKind == JsonValueKind.Number)
            return new TextComponent(element.GetRawText());

        if (element.ValueKind is JsonValueKind.True or JsonValueKind.False)
            return new TextComponent(element.GetBoolean() ? "true" : "false");

        if (element.ValueKind == JsonValueKind.Array)
        {
            var arrayBuilder = ImmutableList.CreateBuilder<Component>();
            foreach (var item in element.EnumerateArray())
            {
                arrayBuilder.Add(ParseElement(item));
            }

            if (arrayBuilder.Count == 0) 
                return Component.Empty;

            var root = arrayBuilder[0];
            return arrayBuilder.Count > 1
                ? root with { Children = root.Children.AddRange(arrayBuilder.GetRange(1, arrayBuilder.Count - 1)) }
                : root;
        }

        if (element.ValueKind == JsonValueKind.Object)
        {
            var style = ParseStyle(element);

            var children = ImmutableList<Component>.Empty;
            if (element.TryGetProperty("extra", out var extraProp) && extraProp.ValueKind == JsonValueKind.Array)
            {
                var childrenBuilder = ImmutableList.CreateBuilder<Component>();
                foreach (var childEl in extraProp.EnumerateArray())
                {
                    childrenBuilder.Add(ParseElement(childEl));
                }
                children = childrenBuilder.ToImmutable();
            }

            // 1. translate
            if (element.TryGetProperty("translate", out var translateProp) && translateProp.ValueKind == JsonValueKind.String)
            {
                string key = translateProp.GetString() ?? string.Empty;
                string? fallback = element.TryGetProperty("fallback", out var fbProp) && fbProp.ValueKind == JsonValueKind.String
                    ? fbProp.GetString()
                    : null;

                var args = ImmutableList<Component>.Empty;
                if (element.TryGetProperty("with", out var withProp) && withProp.ValueKind == JsonValueKind.Array)
                {
                    var argsBuilder = ImmutableList.CreateBuilder<Component>();
                    foreach (var argItem in withProp.EnumerateArray())
                    {
                        argsBuilder.Add(ParseElement(argItem));
                    }
                    args = argsBuilder.ToImmutable();
                }

                return new TranslatableComponent(key, args, fallback, null, style) { Children = children };
            }

            // 2. keybind
            if (element.TryGetProperty("keybind", out var keybindProp) && keybindProp.ValueKind == JsonValueKind.String)
            {
                return new KeybindComponent(keybindProp.GetString() ?? string.Empty, style) { Children = children };
            }

            // 3. Selector (separator may be either a string or an object).
            if (element.TryGetProperty("selector", out var selectorProp) && selectorProp.ValueKind == JsonValueKind.String)
            {
                Component? separator = null;
                if (element.TryGetProperty("separator", out var sepProp))
                {
                    separator = sepProp.ValueKind == JsonValueKind.String 
                        ? new TextComponent(sepProp.GetString() ?? string.Empty) 
                        : ParseElement(sepProp);
                }

                return new SelectorComponent(selectorProp.GetString() ?? string.Empty, separator, style) { Children = children };
            }

            // 4. text fallback
            string content = string.Empty;
            if (element.TryGetProperty("text", out var textProp) && textProp.ValueKind == JsonValueKind.String)
            {
                content = textProp.GetString() ?? string.Empty;
            }

            return new TextComponent(content, style) { Children = children };
        }

        return Component.Empty;
    }

    private static TextStyle ParseStyle(JsonElement element)
    {
        var style = TextStyle.Empty;

        // Color.
        if (element.TryGetProperty("color", out var colorProp) && colorProp.ValueKind == JsonValueKind.String)
        {
            string colorStr = colorProp.GetString()!;
            style = style.WithColor(colorStr.StartsWith('#') 
                ? TextColor.FromHex(colorStr) 
                : ParseNamedColor(colorStr));
        }

        // Modifiers.
        if (element.TryGetProperty("bold", out var boldProp) && boldProp.ValueKind is JsonValueKind.True or JsonValueKind.False)
            style = style.WithBold(boldProp.GetBoolean());

        if (element.TryGetProperty("italic", out var italicProp) && italicProp.ValueKind is JsonValueKind.True or JsonValueKind.False)
            style = style.WithItalic(italicProp.GetBoolean());

        if (element.TryGetProperty("underlined", out var underProp) && underProp.ValueKind is JsonValueKind.True or JsonValueKind.False)
            style = style.WithUnderlined(underProp.GetBoolean());

        if (element.TryGetProperty("strikethrough", out var strikeProp) && strikeProp.ValueKind is JsonValueKind.True or JsonValueKind.False)
            style = style.WithStrikethrough(strikeProp.GetBoolean());

        if (element.TryGetProperty("obfuscated", out var obfProp) && obfProp.ValueKind is JsonValueKind.True or JsonValueKind.False)
            style = style.WithObfuscated(obfProp.GetBoolean());

        if (element.TryGetProperty("font", out var fontProp) && fontProp.ValueKind == JsonValueKind.String)
            style = style.WithFont(fontProp.GetString()!);

        if (element.TryGetProperty("insertion", out var insProp) && insProp.ValueKind == JsonValueKind.String)
            style = style.WithInsertion(insProp.GetString()!);

        // Shadow [A, R, G, B].
        if (element.TryGetProperty("shadow_color", out var shadowProp) && shadowProp.ValueKind == JsonValueKind.Array)
        {
            byte[] parts = new byte[4];
            int idx = 0;
            foreach (var b in shadowProp.EnumerateArray())
            {
                if (idx < 4 && b.TryGetByte(out byte val))
                {
                    parts[idx++] = val;
                }
            }

            if (idx == 4)
            {
                uint argb = ((uint)parts[0] << 24) | ((uint)parts[1] << 16) | ((uint)parts[2] << 8) | parts[3];
                style = style.WithShadowColor(argb);
            }
        }

        // Click Event
        if (element.TryGetProperty("click_event", out var clickProp) && clickProp.ValueKind == JsonValueKind.Object)
        {
            string action = clickProp.TryGetProperty("action", out var act) ? act.GetString() ?? "" : "";

            string val = "";
            if (clickProp.TryGetProperty("url", out var u)) val = u.GetString() ?? "";
            else if (clickProp.TryGetProperty("command", out var c)) val = c.GetString() ?? "";
            else if (clickProp.TryGetProperty("value", out var v)) val = v.GetString() ?? "";
            else if (clickProp.TryGetProperty("path", out var pth)) val = pth.GetString() ?? "";
            else if (clickProp.TryGetProperty("page", out var p)) val = p.ToString();

            var clickAction = action switch
            {
                "open_url" => ClickAction.OpenUrl,
                "open_file" => ClickAction.OpenFile,
                "run_command" => ClickAction.RunCommand,
                "suggest_command" => ClickAction.SuggestCommand,
                "change_page" => ClickAction.ChangePage,
                "copy_to_clipboard" => ClickAction.CopyToClipboard,
                _ => (ClickAction?)null
            };

            if (clickAction.HasValue)
            {
                style = style.WithClick(clickAction.Value, val);
            }
        }

        // Hover event (accept either an array or a single object).
        if (element.TryGetProperty("hover_event", out var hoverProp) && hoverProp.ValueKind == JsonValueKind.Object)
        {
            string action = hoverProp.TryGetProperty("action", out var hAct) ? hAct.GetString() ?? "" : "";

            switch (action)
            {
                case "show_text" when hoverProp.TryGetProperty("value", out var payload):
                    style = style.WithHover(new HoverEvent.ShowText(ParseElement(payload)));
                    break;

                case "show_item" when hoverProp.TryGetProperty("id", out var idProp):
                    style = style.WithHover(new HoverEvent.ShowItem(idProp.GetString() ?? ""));
                    break;

                case "show_entity":
                    string entType = hoverProp.TryGetProperty("id", out var t) ? t.GetString() ?? "" : "";
                    string entUuid = hoverProp.TryGetProperty("uuid", out var u) ? u.GetString() ?? "" : "";
                    Component? entName = null;
                    if (hoverProp.TryGetProperty("name", out var nProp))
                    {
                        entName = ParseElement(nProp);
                    }
                    style = style.WithHover(new HoverEvent.ShowEntity(entType, entUuid, entName));
                    break;
            }
        }

        return style;
    }

    private static TextColor ParseNamedColor(string name) => name.ToLowerInvariant() switch
    {
        "black"        => TextColor.Black,
        "dark_blue"    => TextColor.DarkBlue,
        "dark_green"   => TextColor.DarkGreen,
        "dark_aqua"    => TextColor.DarkAqua,
        "dark_red"     => TextColor.DarkRed,
        "dark_purple"  => TextColor.DarkPurple,
        "gold"         => TextColor.Gold,
        "gray"         => TextColor.Gray,
        "dark_gray"    => TextColor.DarkGray,
        "blue"         => TextColor.Blue,
        "green"        => TextColor.Green,
        "aqua"         => TextColor.Aqua,
        "red"          => TextColor.Red,
        "light_purple" => TextColor.LightPurple,
        "yellow"       => TextColor.Yellow,
        "white"        => TextColor.White,
        _              => TextColor.White
    };
}