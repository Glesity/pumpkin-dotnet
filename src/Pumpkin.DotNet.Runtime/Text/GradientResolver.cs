using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using Pumpkin.DotNet.Api.Text;

namespace Pumpkin.DotNet.Runtime.Text;

internal static class GradientResolver
{
    private static readonly TextColor[] RainbowColors =
    [
        TextColor.FromRgb(255, 0, 0),     // Red
        TextColor.FromRgb(255, 127, 0),   // Orange
        TextColor.FromRgb(255, 255, 0),   // Yellow
        TextColor.FromRgb(0, 255, 0),     // Green
        TextColor.FromRgb(0, 0, 255),     // Blue
        TextColor.FromRgb(75, 0, 130),    // Indigo
        TextColor.FromRgb(148, 0, 211)    // Violet
    ];

    public static Component FlattenGradients(Component comp)
    {
        if (comp is TextComponent textComp && !string.IsNullOrEmpty(textComp.Content))
        {
            var colors = textComp.Style.GradientColors;
            bool isRainbow = textComp.Style.IsRainbow;

            if (isRainbow)
                colors = RainbowColors;

            if (colors != null && colors.Length > 0 && textComp.Content.Length > 1)
            {
                var baseStyle = textComp.Style with { GradientColors = null, IsRainbow = false };
                string content = textComp.Content;
                int len = content.Length;

                var builder = new TextComponent(string.Empty, baseStyle);
                var children = new List<Component>(len + textComp.Children.Count);

                for (int i = 0; i < len; i++)
                {
                    float factor = (float)i / (len - 1);
                    var charColor = InterpolateColor(colors, factor);
                    var charStyle = baseStyle.WithColor(charColor);

                    children.Add(new TextComponent(content[i].ToString(), charStyle));
                }

                // Add the original children, recursively expanding them.
                foreach (var child in textComp.Children)
                {
                    children.Add(FlattenGradients(child));
                }

                return builder with { Children = children.ToImmutableList() };
            }
        }

        // Recursively process child nodes.
        if (!comp.Children.IsEmpty)
        {
            var updatedChildren = comp.Children.Select(FlattenGradients).ToImmutableList();
            return comp with { Children = updatedChildren };
        }

        return comp;
    }

    private static TextColor InterpolateColor(TextColor[] colors, float factor)
    {
        if (colors.Length == 1) return colors[0];

        float scaledFactor = factor * (colors.Length - 1);
        int index = (int)scaledFactor;
        if (index >= colors.Length - 1) return colors[^1];

        float localFactor = scaledFactor - index;
        var c1 = colors[index];
        var c2 = colors[index + 1];

        byte r = (byte)(c1.R + (c2.R - c1.R) * localFactor);
        byte g = (byte)(c1.G + (c2.G - c1.G) * localFactor);
        byte b = (byte)(c1.B + (c2.B - c1.B) * localFactor);

        return TextColor.FromRgb(r, g, b);
    }
}