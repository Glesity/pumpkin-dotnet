using System.Globalization;

namespace Pumpkin.DotNet.Api.Text;

public enum NamedTextColor : byte
{
    Black,
    DarkBlue,
    DarkGreen,
    DarkAqua,
    DarkRed,
    DarkPurple,
    Gold,
    Gray,
    DarkGray,
    Blue,
    Green,
    Aqua,
    Red,
    LightPurple,
    Yellow,
    White
}

public readonly record struct TextColor
{
    public string Name { get; }
    public uint Value { get; }
    public bool IsNamed { get; }
    public NamedTextColor? NamedType { get; }

    public byte R => (byte)((Value >> 16) & 0xFF);
    public byte G => (byte)((Value >> 8) & 0xFF);
    public byte B => (byte)(Value & 0xFF);

    public string Hex => $"#{Value:X6}";

    private TextColor(string name, uint value, bool isNamed, NamedTextColor? namedType)
    {
        Name = name;
        Value = value;
        IsNamed = isNamed;
        NamedType = namedType;
    }

    // --- Color factories ---

    public static TextColor FromRgb(byte r, byte g, byte b)
    {
        uint value = ((uint)r << 16) | ((uint)g << 8) | b;
        return new TextColor($"#{value:X6}", value, isNamed: false, namedType: null);
    }

    public static TextColor FromHex(string hex)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(hex);

        ReadOnlySpan<char> span = hex.StartsWith('#') ? hex.AsSpan(1) : hex.AsSpan();
        if (span.Length != 6 || !uint.TryParse(span, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint rgb))
            throw new ArgumentException("Hex color must be in format #RRGGBB", nameof(hex));

        return new TextColor($"#{rgb:X6}", rgb, isNamed: false, namedType: null);
    }

    public static TextColor FromNamed(NamedTextColor named) => named switch
    {
        NamedTextColor.Black       => Black,
        NamedTextColor.DarkBlue    => DarkBlue,
        NamedTextColor.DarkGreen   => DarkGreen,
        NamedTextColor.DarkAqua    => DarkAqua,
        NamedTextColor.DarkRed     => DarkRed,
        NamedTextColor.DarkPurple  => DarkPurple,
        NamedTextColor.Gold        => Gold,
        NamedTextColor.Gray        => Gray,
        NamedTextColor.DarkGray    => DarkGray,
        NamedTextColor.Blue        => Blue,
        NamedTextColor.Green       => Green,
        NamedTextColor.Aqua        => Aqua,
        NamedTextColor.Red         => Red,
        NamedTextColor.LightPurple => LightPurple,
        NamedTextColor.Yellow      => Yellow,
        NamedTextColor.White       => White,
        _                          => White
    };

    // --- Standard Minecraft palette colors ---

    public static readonly TextColor Black       = new("black",        0x000000, true, NamedTextColor.Black);
    public static readonly TextColor DarkBlue    = new("dark_blue",    0x0000AA, true, NamedTextColor.DarkBlue);
    public static readonly TextColor DarkGreen   = new("dark_green",   0x00AA00, true, NamedTextColor.DarkGreen);
    public static readonly TextColor DarkAqua    = new("dark_aqua",    0x00AAAA, true, NamedTextColor.DarkAqua);
    public static readonly TextColor DarkRed     = new("dark_red",     0xAA0000, true, NamedTextColor.DarkRed);
    public static readonly TextColor DarkPurple  = new("dark_purple",  0xAA00AA, true, NamedTextColor.DarkPurple);
    public static readonly TextColor Gold        = new("gold",         0xFFAA00, true, NamedTextColor.Gold);
    public static readonly TextColor Gray        = new("gray",         0xAAAAAA, true, NamedTextColor.Gray);
    public static readonly TextColor DarkGray    = new("dark_gray",    0x555555, true, NamedTextColor.DarkGray);
    public static readonly TextColor Blue        = new("blue",         0x5555FF, true, NamedTextColor.Blue);
    public static readonly TextColor Green       = new("green",        0x55FF55, true, NamedTextColor.Green);
    public static readonly TextColor Aqua        = new("aqua",         0x55FFFF, true, NamedTextColor.Aqua);
    public static readonly TextColor Red         = new("red",          0xFF5555, true, NamedTextColor.Red);
    public static readonly TextColor LightPurple = new("light_purple", 0xFF55FF, true, NamedTextColor.LightPurple);
    public static readonly TextColor Yellow      = new("yellow",       0xFFFF55, true, NamedTextColor.Yellow);
    public static readonly TextColor White       = new("white",        0xFFFFFF, true, NamedTextColor.White);

    public override string ToString() => IsNamed ? Name : Hex;

    public static implicit operator TextColor(NamedTextColor named) => FromNamed(named);
}