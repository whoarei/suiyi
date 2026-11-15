using System;
using System.Collections.Generic;
using System.Windows.Media;

namespace TranslationFloat.App;

internal enum ThemeColor { Blue, Cyan, Green, Lime, Yellow, Orange, Pink, Magenta, Purple, Black }

internal sealed record ThemePalette(ThemeColor Id, string Name, string Hex)
{
    public Color Color => (Color)ColorConverter.ConvertFromString(Hex);
    public static IReadOnlyList<ThemePalette> All { get; } = Array.AsReadOnly(new ThemePalette[]
    {
        new ThemePalette(ThemeColor.Blue, "蓝色", "#2E68ED"), new(ThemeColor.Cyan, "青色", "#01B2FE"),
        new(ThemeColor.Green, "绿色", "#00BB9E"), new(ThemeColor.Lime, "青柠绿", "#B8D811"),
        new(ThemeColor.Yellow, "黄色", "#FECB3C"), new(ThemeColor.Orange, "橙色", "#FF8067"),
        new(ThemeColor.Pink, "粉色", "#FA70AB"), new(ThemeColor.Magenta, "洋红色", "#DF68DC"),
        new(ThemeColor.Purple, "紫色", "#A35BFD"), new(ThemeColor.Black, "黑色", "#000000")
    });

    public static ThemePalette Get(ThemeColor id)
    {
        foreach (var palette in All) if (palette.Id == id) return palette;
        throw new ArgumentOutOfRangeException(nameof(id));
    }
}

internal sealed class ThemeAppearance
{
    public IReadOnlyDictionary<string, Color> Colors { get; }
    public Color this[string key] => Colors[key];

    public ThemeAppearance(ThemePalette palette, bool dark)
    {
        var surface = Parse(dark ? "#222831" : "#FFFFFF");
        var field = Parse(dark ? "#2B323C" : "#F7F8FA");
        var text = Parse(dark ? "#EDF0F5" : "#28312E");
        var accent = palette.Id == ThemeColor.Black && dark ? Parse("#E5E9EF") : palette.Color;
        var accentText = Readable(dark ? Mix(System.Windows.Media.Colors.White, accent, .30) : accent, surface);
        var notice = dark ? Mix(accent, surface, .16) : Mix(accent, System.Windows.Media.Colors.Black, .28);
        Colors = new Dictionary<string, Color>
        {
            ["Surface"] = surface, ["Field"] = field, ["Text"] = text,
            ["Border"] = Parse(dark ? "#414A57" : "#E1E6E2"),
            ["Muted"] = Parse(dark ? "#A5AFBC" : "#68746F"),
            ["Placeholder"] = Parse(dark ? "#909BA9" : "#909B94"),
            ["Separator"] = Parse(dark ? "#353E49" : "#E8EDE9"),
            ["Control"] = Parse(dark ? "#303844" : "#F4F6F5"),
            ["Accent"] = accent, ["OnAccent"] = On(accent), ["AccentText"] = accentText,
            ["AccentSoft"] = Mix(accent, surface, dark ? .18 : .12),
            ["AccentBorder"] = Mix(accent, surface, dark ? .55 : .45),
            ["Selection"] = Mix(accent, field, .28),
            ["Notice"] = notice, ["OnNotice"] = On(notice),
            ["Error"] = Parse(dark ? "#FF9A90" : "#A4392F"),
            ["DangerSoft"] = Parse(dark ? "#49322F" : "#FCF2EF"),
            ["Track"] = Parse(dark ? "#596573" : "#D7E1DB"),
            ["Disabled"] = Parse(dark ? "#798593" : "#A9B4AD")
        };
    }

    private static Color Parse(string hex) => (Color)ColorConverter.ConvertFromString(hex);
    private static Color Mix(Color accent, Color background, double amount) => Color.FromRgb(
        (byte)Math.Round(accent.R * amount + background.R * (1 - amount)),
        (byte)Math.Round(accent.G * amount + background.G * (1 - amount)),
        (byte)Math.Round(accent.B * amount + background.B * (1 - amount)));
    private static Color On(Color background) => Contrast(System.Windows.Media.Colors.White, background)
        > Contrast(Parse("#171717"), background) ? System.Windows.Media.Colors.White : Parse("#171717");
    private static Color Readable(Color accent, Color background)
    {
        var goal = On(background);
        for (var step = 0; step <= 20; step++)
        {
            var color = Mix(goal, accent, step / 20d);
            if (Contrast(color, background) >= 4.5) return color;
        }
        return goal;
    }
    internal static double Contrast(Color a, Color b)
    {
        static double Luminance(Color color)
        {
            static double Channel(byte channel) { var value = channel / 255d; return value <= .04045 ? value / 12.92 : Math.Pow((value + .055) / 1.055, 2.4); }
            return .2126 * Channel(color.R) + .7152 * Channel(color.G) + .0722 * Channel(color.B);
        }
        var first = Luminance(a); var second = Luminance(b);
        return (Math.Max(first, second) + .05) / (Math.Min(first, second) + .05);
    }
}
