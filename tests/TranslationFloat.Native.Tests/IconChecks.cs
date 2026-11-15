extern alias AppRuntime;

using Microsoft.Win32;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ThemeManager = AppRuntime::TranslationFloat.App.ThemeManager;
using ThemeSettingsStore = AppRuntime::TranslationFloat.App.ThemeSettingsStore;
using ThemePalette = AppRuntime::TranslationFloat.App.ThemePalette;
using ThemeColor = AppRuntime::TranslationFloat.App.ThemeColor;
using ThemeAppearance = AppRuntime::TranslationFloat.App.ThemeAppearance;
using IconFactory = AppRuntime::TranslationFloat.App.ThemeIconFactory;
using IconController = AppRuntime::TranslationFloat.App.ThemeIconController;
using Shell = AppRuntime::TranslationFloat.App.ShellIconIntegration;

internal static class IconChecks
{
    [DllImport("user32.dll")] private static extern nint SendMessage(nint window, uint message, nint arg, nint data);
    [DllImport("user32.dll")] private static extern uint GetGuiResources(nint process, uint flags);

    public static void Run()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            var branch = @"Software\TranslationFloat.Tests.Icons\" + Guid.NewGuid().ToString("N");
            var root = Path.Combine(Path.GetTempPath(), "Suiyi.Icon.Tests." + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            Window? first = null, second = null, third = null;
            try
            {
                var factory = new IconFactory();
                foreach (var palette in ThemePalette.All)
                foreach (var dark in new[] { false, true })
                {
                    var appearance = new ThemeAppearance(palette, dark);
                    using var icons = factory.Create(appearance);
                    using var windowData = new MemoryStream(icons.WindowBytes);
                    var decoder = new IconBitmapDecoder(windowData, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                    if (!IconFactory.WindowSizes.All(size => decoder.Frames.Any(frame => frame.PixelWidth == size))) throw new InvalidOperationException("Missing window frame");
                    using var trayData = new MemoryStream(icons.TrayBytes);
                    var small = new IconBitmapDecoder(trayData, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                    if (!IconFactory.TraySizes.All(size => small.Frames.Any(frame => frame.PixelWidth == size))) throw new InvalidOperationException("Missing tray frame");
                    using var bitmap = icons.TrayIcon.ToBitmap();
                    if (!ContainsAccent(bitmap, appearance["Accent"])) throw new InvalidOperationException("Wrong icon color: " + palette.Name);
                }
                Check(true, "十种主题的深浅图标均含完整尺寸并能被原生托盘解码");
                var themeDark = false;
                using var theme = new ThemeManager(new ResourceDictionary(), new ThemeSettingsStore(branch), () => themeDark, false);
                using var tray = new System.Windows.Forms.NotifyIcon { Visible = false };
                var executable = Path.Combine(root, "Suiyi.exe"); File.WriteAllText(executable, "fixture; never executed");
                var otherExecutable = Path.Combine(root, "Other.exe"); File.WriteAllText(otherExecutable, "fixture; never executed");
                var ownShortcut = Path.Combine(root, "Own.lnk"); var otherShortcut = Path.Combine(root, "Other.lnk");
                MakeLink(ownShortcut, executable); MakeLink(otherShortcut, otherExecutable);
                using var controller = new IconController(theme, icon => tray.Icon = icon, Path.Combine(root, "icons"), executable, () => [ownShortcut, otherShortcut]);
                tray.Icon = controller.TrayIcon;
                first = new Window { ShowInTaskbar = false }; second = new Window { ShowInTaskbar = false };
                controller.Track(first); controller.Track(second);
                var handle = new WindowInteropHelper(first).EnsureHandle(); new WindowInteropHelper(second).EnsureHandle();
                var orange = first.Icon;
                theme.Select(ThemeColor.Blue);
                using (var bitmap = tray.Icon.ToBitmap())
                    Check(!ReferenceEquals(orange, first.Icon) && ReferenceEquals(first.Icon, second.Icon) && ContainsAccent(bitmap, theme.Appearance["Accent"]),
                        "主题变化同步已有窗口与托盘图标");
                var nativeIcon = SendMessage(handle, 0x007F, 1, 0); // WM_GETICON / ICON_BIG on our own hidden fixture only.
                if (nativeIcon == 0) throw new InvalidOperationException("Missing native window icon");
                using (var wrapper = System.Drawing.Icon.FromHandle(nativeIcon))
                using (var bitmap = wrapper.ToBitmap())
                    Check(ContainsAccent(bitmap, theme.Appearance["Accent"]), "任务栏使用的原生窗口图标实际更新为主题色");
                var beforeBlack = controller.IconPath;
                theme.Select(ThemeColor.Black); themeDark = true; theme.RefreshSystemTheme();
                third = new Window { ShowInTaskbar = false }; controller.Track(third);
                Check(controller.IconPath != beforeBlack && File.Exists(controller.IconPath) && ReferenceEquals(first.Icon, third.Icon)
                    && ReadProperty(handle, 3) == controller.IconPath + ",0" && ReadProperty(handle, 5) == Shell.AppId,
                    "系统深浅变化更新任务栏资源，新窗口沿用当前图标");
                var ownIcon = Environment.ExpandEnvironmentVariables(ReadLinkIcon(ownShortcut));
                var otherIcon = Environment.ExpandEnvironmentVariables(ReadLinkIcon(otherShortcut));
                if (!Shell.SameFilePath(ownIcon, controller.IconPath!))
                    Console.WriteLine($"Fixture icon mismatch: actual={ownIcon}, expected={controller.IconPath}");
                Check(Shell.SameFilePath(ownIcon, controller.IconPath!) && !Shell.SameFilePath(otherIcon, controller.IconPath!),
                    "只刷新目标为本程序的快捷方式，不修改其他应用");
                var baseline = GetGuiResources(Process.GetCurrentProcess().Handle, 1);
                for (var i = 0; i < 40; i++) theme.Select(i % 2 == 0 ? ThemeColor.Orange : ThemeColor.Blue);
                GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
                Check(GetGuiResources(Process.GetCurrentProcess().Handle, 1) <= baseline + 12, "连续切换图标不累积原生图标句柄");
            }
            catch (Exception error) { failure = error; }
            finally
            {
                third?.Close(); second?.Close(); first?.Close();
                Registry.CurrentUser.DeleteSubKeyTree(branch, false);
                Directory.Delete(root, true);
            }
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
        if (failure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
        Console.WriteLine("6 icon integration checks passed; private windows and fixture shortcuts only.");
    }

    private static bool ContainsAccent(System.Drawing.Bitmap bitmap, Color accent)
    {
        var matching = 0;
        for (var y = 0; y < bitmap.Height; y++)
        for (var x = 0; x < bitmap.Width; x++)
        { var pixel = bitmap.GetPixel(x, y); if (pixel.A > 240 && Math.Abs(pixel.R - accent.R) < 3 && Math.Abs(pixel.G - accent.G) < 3 && Math.Abs(pixel.B - accent.B) < 3) matching++; }
        return matching > bitmap.Width * bitmap.Height / 5;
    }
    private static string? ReadProperty(nint handle, uint id)
    {
        var store = Shell.WindowStore(handle); var key = new Shell.PropertyKey(id);
        try { Marshal.ThrowExceptionForHR(store.GetValue(ref key, out var value)); try { return value.Text; } finally { value.Dispose(); } }
        finally { Marshal.FinalReleaseComObject(store); }
    }
    private static void MakeLink(string path, string target)
    {
        var link = Shell.CreateLink();
        try { ((Shell.IShellLink)link).SetPath(target); ((IPersistFile)link).Save(path, true); }
        finally { Marshal.FinalReleaseComObject(link); }
    }
    private static string ReadLinkIcon(string path)
    {
        var link = Shell.CreateLink();
        try { ((IPersistFile)link).Load(path, 0); var icon = new StringBuilder(32768); ((Shell.IShellLink)link).GetIconLocation(icon, icon.Capacity, out _); return icon.ToString(); }
        finally { Marshal.FinalReleaseComObject(link); }
    }
    private static void Check(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); Console.WriteLine("PASS " + message); }
}
