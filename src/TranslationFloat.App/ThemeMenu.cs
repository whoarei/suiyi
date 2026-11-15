using System;
using System.Windows.Forms;

namespace TranslationFloat.App;

internal static class ThemeMenu
{
    public static ToolStripMenuItem Create(ThemeManager theme, Action<ThemeColor> select)
    {
        var parent = new ToolStripMenuItem { Tag = TrayMenuGlyph.Theme, ToolTipText = "浅色与深色自动跟随 Windows。" };
        var colors = new TrayMenu(() => theme.Appearance);
        parent.DropDown = colors;
        foreach (var palette in ThemePalette.All)
        {
            var item = new ToolStripMenuItem(palette.Name) { Tag = palette };
            item.Click += (_, _) => select(palette.Id);
            colors.Items.Add(item);
        }
        void Refresh()
        {
            parent.Text = "主题 · " + theme.Selected.Name;
            foreach (ToolStripMenuItem item in colors.Items) item.Checked = ((ThemePalette)item.Tag!).Id == theme.Selected.Id;
            colors.RefreshTheme();
        }
        theme.Changed += Refresh;
        parent.Disposed += (_, _) => theme.Changed -= Refresh;
        Refresh();
        return parent;
    }
}
