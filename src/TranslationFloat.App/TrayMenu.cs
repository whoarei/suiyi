using System;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace TranslationFloat.App;

internal enum TrayMenuGlyph { Open, Settings, Quick, Startup, Exit }

// Keep native menu navigation, dismissal and accessibility; draw the surface ourselves.
internal sealed class TrayMenu : ContextMenuStrip
{
    private readonly Font _menuFont = new("Microsoft YaHei UI", 9f);

    public TrayMenu()
    {
        AutoSize = false;
        ShowImageMargin = ShowCheckMargin = false;
        DropShadowEnabled = true;
        Font = _menuFont;
        BackColor = Color.White;
        ForeColor = Color.FromArgb(41, 66, 51);
        Renderer = new TrayMenuRenderer();
    }

    public ToolStripMenuItem AddAction(string text, TrayMenuGlyph glyph, EventHandler action)
    {
        var item = new ToolStripMenuItem(text) { Tag = glyph, AutoSize = false, Margin = Padding.Empty };
        item.Click += action;
        Items.Add(item);
        return item;
    }

    protected override void OnOpening(CancelEventArgs e)
    {
        base.OnOpening(e);
        if (!e.Cancel) MeasureMenu();
    }

    protected override void OnOpened(EventArgs e)
    {
        MeasureMenu();
        base.OnOpened(e);
    }

    protected override void OnItemAdded(ToolStripItemEventArgs e)
    { base.OnItemAdded(e); MeasureMenu(); }

    private void MeasureMenu()
    {
        var scale = DeviceDpi / 96f;
        int Dip(float value) => (int)Math.Round(value * scale);
        Padding = new Padding(Dip(8));
        var height = Padding.Vertical;
        foreach (ToolStripItem item in Items)
        {
            item.AutoSize = false;
            item.Margin = Padding.Empty;
            item.Size = new Size(Dip(224) - Padding.Horizontal, Dip(item is ToolStripSeparator ? 8 : 34));
            if (item.Available) height += item.Height;
        }
        Size = new Size(Dip(224), height);
        RoundWindow();
    }

    protected override void OnSizeChanged(EventArgs e)
    { base.OnSizeChanged(e); RoundWindow(); }

    private void RoundWindow()
    {
        if (Width < 2 || Height < 2) return;
        using var path = TrayMenuRenderer.Rounded(new RectangleF(0, 0, Width, Height), 10 * DeviceDpi / 96f);
        var previous = Region;
        Region = new Region(path);
        previous?.Dispose();
    }

    protected override void Dispose(bool disposing)
    { base.Dispose(disposing); if (disposing) _menuFont.Dispose(); }
}

internal sealed class TrayMenuRenderer : ToolStripProfessionalRenderer
{
    private static readonly Color Ink = Color.FromArgb(41, 66, 51);
    private static readonly Color Muted = Color.FromArgb(102, 126, 111);
    private static readonly Color Disabled = Color.FromArgb(169, 180, 173);
    private static readonly Color Green = Color.FromArgb(8, 122, 80);
    private static readonly Color ExitInk = Color.FromArgb(143, 89, 79);

    protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var background = new SolidBrush(Color.White);
        e.Graphics.FillRectangle(background, new Rectangle(Point.Empty, e.ToolStrip.Size));
    }

    protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
    {
        var strip = e.ToolStrip;
        if (strip is null) return;
        var scale = strip.DeviceDpi / 96f;
        var inset = scale / 2;
        using var path = Rounded(new RectangleF(inset, inset, strip.Width - scale, strip.Height - scale), 10 * scale);
        using var pen = new Pen(Color.FromArgb(220, 230, 223), scale);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        e.Graphics.DrawPath(pen, path);
    }

    protected override void OnRenderImageMargin(ToolStripRenderEventArgs e) { }
    protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e) { }

    protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
    {
        if (!e.Item.Selected || !e.Item.Enabled) return;
        var scale = (e.ToolStrip?.DeviceDpi ?? 96) / 96f;
        var exit = e.Item.Tag is TrayMenuGlyph.Exit;
        using var path = Rounded(new RectangleF(0, scale, e.Item.Width, e.Item.Height - 2 * scale), 6 * scale);
        using var brush = new SolidBrush(exit ? Color.FromArgb(252, 242, 239) : Color.FromArgb(234, 245, 238));
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        e.Graphics.FillPath(brush, path);
    }

    protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
    {
        var scale = (e.ToolStrip?.DeviceDpi ?? 96) / 96f;
        using var pen = new Pen(Color.FromArgb(235, 240, 237), scale);
        e.Graphics.DrawLine(pen, 11 * scale, e.Item.Height / 2f, e.Item.Width - 11 * scale, e.Item.Height / 2f);
    }

    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        if (e.Item is not ToolStripMenuItem item) return;
        var scale = (e.ToolStrip?.DeviceDpi ?? 96) / 96f;
        var glyph = item.Tag is TrayMenuGlyph value ? value : TrayMenuGlyph.Open;
        var toggle = glyph is TrayMenuGlyph.Quick or TrayMenuGlyph.Startup;
        var color = !item.Enabled ? Disabled : glyph == TrayMenuGlyph.Exit ? ExitInk : item.Selected ? Green : Ink;
        DrawGlyph(e.Graphics, glyph, 11 * scale, (item.Height - 16 * scale) / 2, scale,
            !item.Enabled ? Disabled : item.Selected ? color : glyph == TrayMenuGlyph.Exit ? ExitInk : Muted);
        var text = Rectangle.Round(new RectangleF(38 * scale, 0, item.Width - (toggle ? 87 : 49) * scale, item.Height));
        TextRenderer.DrawText(e.Graphics, e.Text, e.TextFont ?? item.Font, text, color,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        if (toggle) DrawSwitch(e.Graphics, new RectangleF(item.Width - 40 * scale, (item.Height - 16 * scale) / 2, 28 * scale, 16 * scale), item.Checked, item.Enabled, scale);
    }

    private static void DrawSwitch(Graphics graphics, RectangleF bounds, bool enabled, bool available, float scale)
    {
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var path = Rounded(bounds, bounds.Height / 2);
        using var track = new SolidBrush(enabled && available ? Green : Color.FromArgb(215, 225, 219));
        graphics.FillPath(track, path);
        var diameter = bounds.Height - 4 * scale;
        var left = enabled ? bounds.Right - diameter - 2 * scale : bounds.Left + 2 * scale;
        using var knob = new SolidBrush(Color.White);
        graphics.FillEllipse(knob, left, bounds.Top + 2 * scale, diameter, diameter);
    }

    private static void DrawGlyph(Graphics graphics, TrayMenuGlyph glyph, float x, float y, float scale, Color color)
    {
        var state = graphics.Save();
        graphics.TranslateTransform(x, y);
        graphics.ScaleTransform(scale, scale);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var pen = new Pen(color, 1.35f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
        switch (glyph)
        {
            case TrayMenuGlyph.Open:
                using (var frame = Rounded(new RectangleF(1.5f, 2.5f, 13, 11), 2)) graphics.DrawPath(pen, frame);
                graphics.DrawLine(pen, 2, 6, 14, 6);
                graphics.DrawLine(pen, 5, 9, 10, 9);
                break;
            case TrayMenuGlyph.Settings:
                graphics.DrawEllipse(pen, 3.5f, 3.5f, 9, 9);
                graphics.DrawEllipse(pen, 6.2f, 6.2f, 3.6f, 3.6f);
                for (var tooth = 0; tooth < 8; tooth++)
                {
                    var angle = tooth * Math.PI / 4;
                    graphics.DrawLine(pen, 8 + (float)Math.Cos(angle) * 5, 8 + (float)Math.Sin(angle) * 5,
                        8 + (float)Math.Cos(angle) * 7, 8 + (float)Math.Sin(angle) * 7);
                }
                break;
            case TrayMenuGlyph.Quick:
                graphics.DrawLines(pen, [new PointF(9, 1.5f), new(3, 9), new(7.5f, 9), new(6.5f, 14.5f), new(13, 6.5f), new(8.5f, 6.5f), new(9, 1.5f)]);
                break;
            case TrayMenuGlyph.Startup:
                graphics.DrawArc(pen, 2.5f, 3, 11, 11, -45, 270);
                graphics.DrawLine(pen, 8, 1, 8, 7);
                break;
            case TrayMenuGlyph.Exit:
                graphics.DrawLines(pen, [new PointF(6, 2), new(2.5f, 2), new(2.5f, 14), new(6, 14)]);
                graphics.DrawLine(pen, 6, 8, 14, 8);
                graphics.DrawLines(pen, [new PointF(11, 5), new(14, 8), new(11, 11)]);
                break;
        }
        graphics.Restore(state);
    }

    internal static GraphicsPath Rounded(RectangleF bounds, float radius)
    {
        radius = Math.Min(radius, Math.Min(bounds.Width, bounds.Height) / 2);
        var path = new GraphicsPath();
        var diameter = radius * 2;
        path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }
}
