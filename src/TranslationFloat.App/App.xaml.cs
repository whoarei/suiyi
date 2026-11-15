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
    private TrayMenu? _trayMenu;
    private PopupWindow? _popup;
    private StartupRegistration? _startup;
    private System.Windows.Forms.ToolStripMenuItem? _startupItem;
    private System.Windows.Forms.ToolStripMenuItem? _quickModeItem;
    private readonly QuickModeSettingsStore _quickSettings = new();
    private QuickNoticeWindow? _quickNotice;
    private UpdateWindow? _updateWindow;
    private EventWaitHandle? _setupExit;
    private RegisteredWaitHandle? _setupExitWait;
    private ThemeManager? _theme;

    protected override void OnStartup(StartupEventArgs e)
    {
        if (e.Args.Length == 2 && e.Args[0] is "--inspect-focus" or "--inspect-selection")
        {
            FocusInspector.Run(e.Args[1], e.Args[0] == "--inspect-selection");
            Shutdown();
            return;
        }
        base.OnStartup(e);
        _mutex = new Mutex(true, "Local\\TranslationFloat.M0", out var created);
        if (!created) { Shutdown(); return; }
        _theme = new ThemeManager(Resources);
        _setupExit = new EventWaitHandle(false, EventResetMode.AutoReset, "Local\\Suiyi.ExitForSetup");
        _setupExitWait = ThreadPool.RegisterWaitForSingleObject(_setupExit, (_, _) => Dispatcher.BeginInvoke(() => Shutdown()), null, Timeout.Infinite, false);
        _popup = new PopupWindow { ShowInTaskbar = e.Args.Contains("--diagnostics") };
        _popup.QuickNotice += ShowQuickNotice;
        _popup.HotKeyChanged += RefreshShortcut;
        try { _popup.SetQuickModeEnabled(_quickSettings.Load()); }
        catch (Exception error) { DiagnosticLog.Write("quick-mode-settings", error.GetType().Name); }
        MainWindow = _popup;
        _popup.InitializeHotKey();
        var menu = _trayMenu = new TrayMenu(() => _theme.Appearance);
        var openItem = menu.AddAction("打开随译", TrayMenuGlyph.Open, (_, _) => _popup.OpenWithoutTarget());
        var settingsItem = menu.AddAction("设置", TrayMenuGlyph.Settings, (_, _) => _popup.OpenSettings());
        menu.AddAction("检查更新", TrayMenuGlyph.Update, (_, _) => OpenUpdateWindow());
        menu.Items.Add(ThemeMenu.Create(_theme, SelectTheme));
        _theme.Changed += menu.RefreshTheme;
        menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
        _quickModeItem = menu.AddAction("快速模式", TrayMenuGlyph.Quick, (_, _) => ToggleQuickMode());
        _quickModeItem.Checked = _popup.QuickModeEnabled;
        _quickModeItem.ToolTipText = "选中文字后按唤出快捷键，后台翻译并直接替换，不显示翻译浮窗。";
        _startupItem = menu.AddAction("开机启动", TrayMenuGlyph.Startup, (_, _) => ToggleStartup());
        menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
        menu.AddAction("退出", TrayMenuGlyph.Exit, (_, _) => Shutdown());
        _trayIcon = LoadTrayIcon();
        _tray = new System.Windows.Forms.NotifyIcon
        {
            Text = TrayText,
            Icon = _trayIcon,
            ContextMenuStrip = menu,
            Visible = true
        };
        _tray.DoubleClick += (_, _) => _popup.OpenWithoutTarget();
        menu.Opening += (_, _) =>
        {
            _theme.RefreshSystemTheme();
            RefreshStartupItem();
            openItem.Enabled = settingsItem.Enabled = !_popup.IsWorking;
            _quickModeItem.Checked = _popup.QuickModeEnabled;
        };
        try
        {
            _startup = StartupRegistration.ForCurrentProcess();
            _startup.Initialize();
            RefreshStartupItem();
            DiagnosticLog.Write("startup", _startup.IsEnabled ? "enabled" : "disabled");
        }
        catch (Exception error) { StartupFailed(error); }
        if (e.Args.Contains("--check-updates")) OpenUpdateWindow();
        else if (e.Args.Contains("--settings")) _popup.OpenSettings();
        else if (e.Args.Contains("--show")) _popup.OpenWithoutTarget();
    }

    private string TrayText => "随译 · " + (_popup?.QuickModeEnabled == true ? "快速模式 · " : "") + _popup?.Shortcut;

    private void SelectTheme(ThemeColor color)
    {
        try { _theme?.Select(color); }
        catch (Exception error)
        {
            DiagnosticLog.Write("theme-save", error.GetType().Name);
            ShowQuickNotice("未能保存主题，请稍后重试。");
        }
    }

    private void OpenUpdateWindow()
    {
        if (_updateWindow is not null) { _updateWindow.Activate(); return; }
        var window = new UpdateWindow(() => _popup?.CanInstallUpdate == true);
        _updateWindow = window;
        window.Closed += (_, _) => { if (ReferenceEquals(_updateWindow, window)) _updateWindow = null; };
        window.Show();
        window.Activate();
    }

    private void RefreshShortcut()
    {
        if (_tray is not null) _tray.Text = TrayText;
        if (_quickModeItem is not null) _quickModeItem.Checked = _popup?.QuickModeEnabled == true;
    }

    private void ToggleQuickMode()
    {
        if (_popup is null || _quickModeItem is null) return;
        try
        {
            var enabled = !_popup.QuickModeEnabled;
            _quickSettings.Save(enabled);
            _popup.SetQuickModeEnabled(enabled);
            _quickModeItem.Checked = enabled;
            if (_tray is not null) _tray.Text = TrayText;
            DiagnosticLog.Write("quick-mode", enabled ? "enabled" : "disabled");
        }
        catch (Exception error)
        {
            DiagnosticLog.Write("quick-mode-settings", error.GetType().Name);
            ShowQuickNotice("未能保存快速模式设置，请稍后重试。");
        }
    }

    private void ShowQuickNotice(string message)
    {
        _quickNotice?.Close();
        var notice = new QuickNoticeWindow(message);
        _quickNotice = notice;
        notice.Closed += (_, _) => { if (ReferenceEquals(_quickNotice, notice)) _quickNotice = null; };
        notice.Show();
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
        _quickNotice?.Close();
        _updateWindow?.Close();
        _tray?.Dispose();
        _trayMenu?.Dispose();
        _theme?.Dispose();
        _trayIcon?.Dispose();
        _setupExitWait?.Unregister(null);
        _setupExit?.Dispose();
        _mutex?.Dispose();
        base.OnExit(e);
    }
}
