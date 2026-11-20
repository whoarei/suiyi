using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Xml.Linq;

namespace TranslationFloat.App;

internal sealed class ThemedIcons : IDisposable
{
    public ImageSource WindowImage { get; }
    public System.Drawing.Icon TrayIcon { get; }
    public byte[] WindowBytes { get; }
    internal byte[] TrayBytes { get; }

    internal ThemedIcons(byte[] window, byte[] tray)
    {
        WindowBytes = window; TrayBytes = tray;
        using var stream = new MemoryStream(window, false);
        var decoder = new IconBitmapDecoder(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        var image = decoder.Frames[0]; image.Freeze(); WindowImage = image;
        using var trayStream = new MemoryStream(tray, false);
        using var icon = new System.Drawing.Icon(trayStream, System.Windows.Forms.SystemInformation.SmallIconSize);
        TrayIcon = (System.Drawing.Icon)icon.Clone();
    }

    public void Dispose() => TrayIcon.Dispose();
}

internal sealed class ThemeIconFactory
{
    internal static readonly int[] WindowSizes = [16, 20, 24, 32, 40, 48, 64, 96, 128, 256];
    internal static readonly int[] TraySizes = [16, 20, 24, 32, 40, 48, 64];
    private readonly XDocument _app = Load("TranslationFloat.Assets.Suiyi.svg");
    private readonly Dictionary<(Color, Color), (byte[] Window, byte[] Tray)> _cache = new();

    public ThemedIcons Create(ThemeAppearance appearance)
    {
        var key = (appearance["Accent"], appearance["OnAccent"]);
        if (!_cache.TryGetValue(key, out var bytes))
        {
            bytes = (Encode(WindowSizes, appearance), Encode(TraySizes, appearance));
            _cache.Add(key, bytes);
        }
        return new(bytes.Window, bytes.Tray);
    }

    private static XDocument Load(string name)
    {
        using var stream = typeof(ThemeIconFactory).Assembly.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException("Missing icon source: " + name);
        return XDocument.Load(stream);
    }

    private byte[] Encode(int[] sizes, ThemeAppearance appearance)
    {
        var frames = new List<byte[]>();
        foreach (var size in sizes)
        {
            var bitmap = Render(_app, size, appearance);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var png = new MemoryStream(); encoder.Save(png); frames.Add(png.ToArray());
        }
        using var result = new MemoryStream();
        using var writer = new BinaryWriter(result, System.Text.Encoding.UTF8, true);
        writer.Write((ushort)0); writer.Write((ushort)1); writer.Write((ushort)sizes.Length);
        var offset = 6 + 16 * sizes.Length;
        for (var i = 0; i < sizes.Length; i++)
        {
            writer.Write((byte)(sizes[i] == 256 ? 0 : sizes[i])); writer.Write((byte)(sizes[i] == 256 ? 0 : sizes[i]));
            writer.Write((byte)0); writer.Write((byte)0); writer.Write((ushort)1); writer.Write((ushort)32);
            writer.Write(frames[i].Length); writer.Write(offset); offset += frames[i].Length;
        }
        foreach (var frame in frames) writer.Write(frame);
        return result.ToArray();
    }

    private static BitmapSource Render(XDocument source, int size, ThemeAppearance appearance)
    {
        var root = source.Root!;
        var units = double.Parse(root.Attribute("viewBox")!.Value.Split(' ')[2], CultureInfo.InvariantCulture);
        var visual = new DrawingVisual();
        using (var draw = visual.RenderOpen())
        {
            draw.PushTransform(new ScaleTransform(size / units, size / units));
            var foreground = new SolidColorBrush(appearance["OnAccent"]);
            var background = new SolidColorBrush(appearance["Accent"]);
            foreach (var node in root.Elements())
            {
                double Number(string key) => double.Parse(node.Attribute(key)!.Value, CultureInfo.InvariantCulture);
                switch (node.Name.LocalName)
                {
                    case "title": break;
                    case "rect":
                        draw.DrawRoundedRectangle(background, null,
                            new Rect(Number("x"), Number("y"), Number("width"), Number("height")), Number("rx"), Number("rx"));
                        break;
                    case "path": draw.DrawGeometry(foreground, null, Geometry.Parse("F1 " + node.Attribute("d")!.Value)); break;
                    default: throw new InvalidOperationException("Unsupported icon shape");
                }
            }
            draw.Pop();
        }
        var image = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32); image.Render(visual); image.Freeze();
        return image;
    }
}
