extern alias AppRuntime;

using Microsoft.Win32;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using TranslationFloat.Core;
using ThemeManager = AppRuntime::TranslationFloat.App.ThemeManager;
using ThemeSettingsStore = AppRuntime::TranslationFloat.App.ThemeSettingsStore;
using ThemePalette = AppRuntime::TranslationFloat.App.ThemePalette;
using ThemeColor = AppRuntime::TranslationFloat.App.ThemeColor;
using ThemeAppearance = AppRuntime::TranslationFloat.App.ThemeAppearance;

internal static class ThemeChecks
{
    public static void Run(string? previewDirectory = null)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            var branch = @"Software\TranslationFloat.Tests.Themes\" + Guid.NewGuid().ToString("N");
            var folder = Path.Combine(Path.GetTempPath(), "TranslationFloat.Themes.Tests." + Guid.NewGuid().ToString("N"));
            Window? window = null;
            try
            {
                var store = new ThemeSettingsStore(branch);
                Check(store.Load() == ThemeColor.Orange, "首次主题默认橙色");
                using var client = new HttpClient();
                var config = new AppRuntime::TranslationFloat.App.TranslationConfiguration("https://theme.test/v1", "demo-model", "");
                window = new AppRuntime::TranslationFloat.App.SettingsWindow(
                    new AppRuntime::TranslationFloat.App.TranslationSettingsStore(folder, false), client, config, HotKeyBinding.Default,
                    true, (_, _, _, _, _) => config, _ => { });
                window.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/TranslationFloat;component/Themes/Controls.xaml", UriKind.Relative) });
                var dark = false;
                using var theme = new ThemeManager(window.Resources, store, () => dark, false);
                using var menu = new AppRuntime::TranslationFloat.App.TrayMenu(() => theme.Appearance);
                var choices = AppRuntime::TranslationFloat.App.ThemeMenu.Create(theme, theme.Select);
                menu.Items.Add(choices); theme.Changed += menu.RefreshTheme;
                var colors = choices.DropDownItems.Cast<System.Windows.Forms.ToolStripMenuItem>().ToArray();
                Check(colors.Length == 10 && colors.Count(item => item.Checked) == 1 && choices.Text?.EndsWith("橙色") == true, "托盘提供十种主题色且默认只勾选橙色");
                var address = (TextBox)window.FindName("AddressBox");
                var save = (Button)window.FindName("SaveButton");
                address.Select(8, 5);
                Layout(window);
                colors.Single(item => item.Text == "蓝色").PerformClick();
                Check(store.Load() == ThemeColor.Blue && choices.Text?.EndsWith("蓝色") == true && colors.Count(item => item.Checked) == 1
                    && ((SolidColorBrush)save.Background).Color == theme.Appearance["Accent"]
                    && address.Text == config.BaseUrl && address.SelectionStart == 8 && address.SelectionLength == 5,
                    "托盘切换立即同步窗口与勾选，持久化颜色并保留输入与选区");
                dark = true; theme.RefreshSystemTheme(); Layout(window);
                var root = (Border)window.Content;
                var model = (ComboBox)window.FindName("ModelBox"); model.ApplyTemplate();
                var editor = (TextBox)model.Template.FindName("PART_EditableTextBox", model);
                var modelFrame = (Border)model.Template.FindName("Frame", model);
                Check(theme.IsDark && theme.Selected.Id == ThemeColor.Blue && ((SolidColorBrush)root.Background).Color == theme.Appearance["Surface"]
                    && ((SolidColorBrush)modelFrame.Background).Color == theme.Appearance["Field"]
                    && ((SolidColorBrush)editor.Foreground).Color == theme.Appearance["Text"],
                    "系统深色变化同步真实设置和可编辑模型控件，保留主题色");
                dark = false; theme.RefreshSystemTheme();
                Check(!theme.IsDark && ((SolidColorBrush)root.Background).Color == theme.Appearance["Surface"] && store.Load() == ThemeColor.Blue,
                    "系统恢复浅色时窗口同步且不改主题偏好");
                foreach (var palette in ThemePalette.All)
                foreach (var isDark in new[] { false, true })
                {
                    var appearance = new ThemeAppearance(palette, isDark);
                    if (ThemeAppearance.Contrast(appearance["OnAccent"], appearance["Accent"]) < 4.5
                        || ThemeAppearance.Contrast(appearance["AccentText"], appearance["Surface"]) < 4.5
                        || ThemeAppearance.Contrast(appearance["Text"], appearance["Surface"]) < 4.5
                        || ThemeAppearance.Contrast(appearance["Text"], appearance["Selection"]) < 4.5)
                        throw new InvalidOperationException("Unreadable palette: " + palette.Name);
                }
                Check(true, "十种配色的深浅模式均保持主按钮和文字对比度");
                using (var key = Registry.CurrentUser.CreateSubKey(branch)) key.SetValue("ThemeColor", "unknown-theme");
                Check(new ThemeSettingsStore(branch).Load() == ThemeColor.Orange, "无效主题偏好回退橙色");
                using (var key = Registry.CurrentUser.CreateSubKey(branch)) key.SetValue("AppsUseLightTheme", 0);
                var systemDark = ThemeManager.ReadSystemDark(branch);
                using (var key = Registry.CurrentUser.CreateSubKey(branch)) key.SetValue("AppsUseLightTheme", 1);
                Check(systemDark && !ThemeManager.ReadSystemDark(branch), "系统深浅读取使用应用模式且不修改系统配置");
                if (previewDirectory is not null)
                {
                    Directory.CreateDirectory(previewDirectory);
                    theme.Select(ThemeColor.Orange);
                    Save(window, Path.Combine(previewDirectory, "orange-light-settings.png"));
                    dark = true; theme.RefreshSystemTheme();
                    Save(window, Path.Combine(previewDirectory, "orange-dark-settings.png"));
                }
            }
            catch (Exception error) { failure = error; }
            finally
            {
                window?.Close();
                Registry.CurrentUser.DeleteSubKeyTree(branch, false);
                if (Directory.Exists(folder)) { foreach (var file in Directory.GetFiles(folder)) File.Delete(file); Directory.Delete(folder); }
            }
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
        if (failure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
        Console.WriteLine("8 theme integration checks passed; isolated preferences and hidden test controls only.");
    }

    private static void Layout(Window window)
    {
        var root = (FrameworkElement)window.Content;
        root.Measure(new Size(window.Width, double.PositiveInfinity));
        root.Arrange(new Rect(0, 0, window.Width, root.DesiredSize.Height)); root.UpdateLayout();
    }
    private static void Save(Window window, string path)
    {
        Layout(window);
        var root = (FrameworkElement)window.Content;
        var image = new RenderTargetBitmap((int)Math.Ceiling(window.Width), (int)Math.Ceiling(root.DesiredSize.Height), 96, 96, PixelFormats.Pbgra32);
        image.Render(root);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image));
        using var stream = File.Create(path); encoder.Save(stream);
    }
    private static void Check(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); Console.WriteLine("PASS " + message); }
}
