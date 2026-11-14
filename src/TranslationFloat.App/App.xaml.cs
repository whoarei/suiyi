using System;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Windows;

namespace TranslationFloat.App;

public partial class App : Application
{
    private Mutex? _mutex;
    private System.Windows.Forms.NotifyIcon? _tray;
    private PopupWindow? _popup;

    protected override void OnStartup(StartupEventArgs e)
    {
        if (e.Args.Length == 2 && e.Args[0] == "--inspect-focus")
        {
            FocusInspector.Run(e.Args[1]);
            Shutdown();
            return;
        }
        base.OnStartup(e);
        _mutex = new Mutex(true, "Local\\TranslationFloat.M0", out var created);
        if (!created) { Shutdown(); return; }
        _popup = new PopupWindow { ShowInTaskbar = e.Args.Contains("--diagnostics") };
        MainWindow = _popup;
        _popup.InitializeHotKey();
        var menu = new System.Windows.Forms.ContextMenuStrip();
        menu.Items.Add("打开浮窗（复制模式）", null, (_, _) => _popup.OpenWithoutTarget());
        menu.Items.Add("DeepSeek 设置", null, (_, _) => _popup.OpenSettings());
        menu.Items.Add("退出", null, (_, _) => Shutdown());
        _tray = new System.Windows.Forms.NotifyIcon
        {
            Text = "翻译浮窗 · Ctrl+Alt+T",
            Icon = SystemIcons.Application,
            ContextMenuStrip = menu,
            Visible = true
        };
        _tray.DoubleClick += (_, _) => _popup.OpenWithoutTarget();
        if (e.Args.Contains("--settings")) _popup.OpenSettings();
        else if (e.Args.Contains("--show")) _popup.OpenWithoutTarget();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _popup?.ReleaseResources();
        _tray?.Dispose();
        _mutex?.Dispose();
        base.OnExit(e);
    }
}
