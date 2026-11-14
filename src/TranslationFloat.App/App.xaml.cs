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
    private Icon? _trayIcon;
    private PopupWindow? _popup;
    private StartupRegistration? _startup;
    private System.Windows.Forms.ToolStripMenuItem? _startupItem;

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
        menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
        _startupItem = new System.Windows.Forms.ToolStripMenuItem("开机启动");
        _startupItem.Click += (_, _) => ToggleStartup();
        menu.Items.Add(_startupItem);
        menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
        menu.Items.Add("退出", null, (_, _) => Shutdown());
        _trayIcon = LoadTrayIcon();
        _tray = new System.Windows.Forms.NotifyIcon
        {
            Text = "随译 · Ctrl+Alt+T",
            Icon = _trayIcon,
            ContextMenuStrip = menu,
            Visible = true
        };
        _tray.DoubleClick += (_, _) => _popup.OpenWithoutTarget();
        menu.Opening += (_, _) => RefreshStartupItem();
        try
        {
            _startup = StartupRegistration.ForCurrentProcess();
            _startup.Initialize();
            RefreshStartupItem();
            DiagnosticLog.Write("startup", _startup.IsEnabled ? "enabled" : "disabled");
        }
        catch (Exception error) { StartupFailed(error); }
        if (e.Args.Contains("--settings")) _popup.OpenSettings();
        else if (e.Args.Contains("--show")) _popup.OpenWithoutTarget();
    }

    private static Icon LoadTrayIcon()
    {
        using var stream = typeof(App).Assembly.GetManifestResourceStream("TranslationFloat.Assets.Suiyi.Tray.ico")
            ?? throw new InvalidOperationException("缺少随译托盘图标资源。");
        using var icon = new Icon(stream, System.Windows.Forms.SystemInformation.SmallIconSize);
        return (Icon)icon.Clone();
    }

    private void RefreshStartupItem()
    {
        if (_startupItem is null) return;
        _startupItem.Enabled = _startup is not null;
        try { _startupItem.Checked = _startup?.IsEnabled == true; }
        catch (Exception) { _startupItem.Checked = false; }
    }

    private void ToggleStartup()
    {
        if (_startup is null || _startupItem is null) return;
        try
        {
            _startup.SetEnabled(!_startupItem.Checked);
            RefreshStartupItem();
            DiagnosticLog.Write("startup", _startupItem.Checked ? "enabled" : "disabled");
        }
        catch (Exception error) { StartupFailed(error); }
    }

    private void StartupFailed(Exception error)
    {
        RefreshStartupItem();
        DiagnosticLog.Write("startup-error", error.GetType().Name);
        _tray?.ShowBalloonTip(6000, "随译", "未能保存开机启动设置。请稍后从托盘菜单重试；路径过长时请先移到较短的固定目录。", System.Windows.Forms.ToolTipIcon.Warning);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _popup?.ReleaseResources();
        _tray?.Dispose();
        _trayIcon?.Dispose();
        _mutex?.Dispose();
        base.OnExit(e);
    }
}
