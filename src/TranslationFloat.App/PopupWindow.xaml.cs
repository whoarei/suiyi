using System;
using System.ComponentModel;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using TranslationFloat.Core;

namespace TranslationFloat.App;

public partial class PopupWindow : Window
{
    private const int HotKeyId = 0x5401;
    private readonly WindowsInsertionPlatform _platform;
    private readonly InsertionCoordinator _insertion;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly DeepSeekSettingsStore _settings = new();
    private readonly HttpClient _http = new(new SocketsHttpHandler { AllowAutoRedirect = false }) { Timeout = Timeout.InfiniteTimeSpan };
    private DeepSeekConfiguration _configuration = new(DeepSeekTranslationProvider.DefaultModel, "");
    private TranslationController? _translation;
    private TargetSession? _target;
    private HwndSource? _source;
    private nint _handle;
    private bool _busy, _sourceComposing, _resultComposing, _disposed, _hotKeyRegistered, _rendering, _openingSettings, _capturing;
    private bool IsComposing => _sourceComposing || _resultComposing;

    public PopupWindow()
    {
        InitializeComponent();
        _platform = new WindowsInsertionPlatform(target => _target?.Id == target.Id);
        _insertion = new InsertionCoordinator(_platform);
        _translation = new TranslationController();
        _translation.Changed += Translation_Changed;
        ApplyConfiguration(_settings.Load());
        ObserveComposition(DraftText, false);
        ObserveComposition(TranslationText, true);
    }

    private void ObserveComposition(System.Windows.Controls.TextBox editor, bool result)
    {
        TextCompositionManager.AddPreviewTextInputStartHandler(editor, (_, _) => StartComposition(result));
        TextCompositionManager.AddPreviewTextInputUpdateHandler(editor, (_, e) =>
        {
            if (string.IsNullOrEmpty(e.TextComposition.CompositionText)) FinishComposition(result);
            else StartComposition(result);
        });
        TextCompositionManager.AddPreviewTextInputHandler(editor, (_, _) => FinishComposition(result));
        editor.LostKeyboardFocus += (_, _) => FinishComposition(result);
        editor.PreviewKeyUp += (_, e) =>
        {
            if (e.Key == Key.Escape || (e.Key == Key.ImeProcessed && e.ImeProcessedKey == Key.Escape)) FinishComposition(result);
        };
    }

    private void ApplyConfiguration(DeepSeekConfiguration configuration)
    {
        _configuration = configuration;
        ITranslationProvider? provider = null;
        if (configuration.IsConfigured)
        {
            try { provider = new DeepSeekTranslationProvider(_http, configuration.ApiKey, configuration.Model); }
            catch (ArgumentException) { _configuration = configuration with { ApiKey = "" }; }
        }
        _translation?.Configure(provider);
        RefreshActions();
    }

    private void StartComposition(bool result)
    {
        if (result) _resultComposing = true;
        else { _sourceComposing = true; _translation?.SetComposing(true); }
        RefreshActions();
    }

    private void FinishComposition(bool result)
    {
        Dispatcher.BeginInvoke(() =>
        {
            if (_disposed) return;
            if (result)
            {
                _resultComposing = false;
                if (_translation?.State.Result != TranslationText.Text) _translation?.EditResult(TranslationText.Text);
            }
            else
            {
                _sourceComposing = false;
                _translation?.UpdateSource(DraftText.Text);
                _translation?.SetComposing(false);
            }
            RefreshActions();
        }, DispatcherPriority.Background);
    }

    private void Translation_Changed()
    {
        if (_disposed || Dispatcher.HasShutdownStarted) return;
        if (Dispatcher.CheckAccess()) RefreshActions();
        else Dispatcher.BeginInvoke(RefreshActions);
    }

    public void InitializeHotKey()
    {
        _handle = new WindowInteropHelper(this).EnsureHandle();
        _source = HwndSource.FromHwnd(_handle);
        _source?.AddHook(WindowProc);
        _hotKeyRegistered = NativeMethods.RegisterHotKey(_handle, HotKeyId, 0x4000 | 0x0002 | 0x0001, 0x54);
        if (!_hotKeyRegistered) StatusText.Text = "Ctrl+Alt+T 已被占用。请关闭冲突程序后重新启动浮窗。";
        DiagnosticLog.Write("started", _hotKeyRegistered ? "hotkey-registered" : "hotkey-unavailable");
    }

    private nint WindowProc(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
    {
        if (message != 0x0312 || wParam != HotKeyId) return 0;
        DiagnosticLog.Write("hotkey", "received");
        handled = true;
        if (_busy || _capturing) return 0;
        if (IsVisible && IsActive) { HideDraft(); return 0; }
        // Capture BEFORE Show/Activate: otherwise the foreground window is this popup.
        _ = OpenFromForegroundAsync();
        return 0;
    }

    private async Task OpenFromForegroundAsync()
    {
        _capturing = true;
        try
        {
            var capture = await _platform.CaptureTargetAsync(_lifetime.Token);
            if (_disposed) return;
            _target = capture.Target;
            DiagnosticLog.Write("capture-result", capture.Target is null ? "copy-mode" : "bound");
            StatusText.Text = capture.Message;
            RefreshActions();
            OpenPopup();
        }
        finally { _capturing = false; }
    }

    public void OpenWithoutTarget()
    {
        if (_busy || _capturing) return;
        _target = null;
        StatusText.Text = _hotKeyRegistered
            ? "请在目标输入框按 Ctrl+Alt+T，绑定原输入位置。"
            : "Ctrl+Alt+T 已被占用；仍可复制文字。关闭冲突程序后重启。";
        RefreshActions();
        OpenPopup();
    }

    private void OpenPopup()
    {
        if (!IsVisible)
        {
            var position = _platform.GetPopupPosition(450, 640);
            Left = position.X; Top = position.Y;
            Show();
        }
        Activate();
        _translation?.Resume();
        RefreshActions();
        DiagnosticLog.Write("popup", "shown");
        Dispatcher.BeginInvoke(() => { DraftText.Focus(); Keyboard.Focus(DraftText); }, DispatcherPriority.Input);
    }

    private void RefreshActions()
    {
        if (InsertButton is null || _translation is null) return;
        var state = _translation.State;
        _rendering = true;
        try { if (!_resultComposing && TranslationText.Text != state.Result) TranslationText.Text = state.Result; }
        finally { _rendering = false; }
        TranslationStatusText.Text = state.Message;
        TranslationStatusText.Foreground = new SolidColorBrush(state.Phase == TranslationPhase.Error ? Color.FromRgb(164, 57, 47) : Color.FromRgb(94, 117, 102));
        TranslationText.IsReadOnly = _busy || !state.CanEdit;
        TranslationText.Opacity = state.CanEdit || string.IsNullOrEmpty(state.Result) ? 1 : 0.5;
        RetryButton.Visibility = state.Phase == TranslationPhase.Error ? Visibility.Visible : Visibility.Collapsed;
        RetryButton.IsEnabled = !_busy && !IsComposing;
        InsertButton.IsEnabled = !_busy && !IsComposing && state.CanOutput && _target is not null;
        CopyButton.IsEnabled = !_busy && !IsComposing && state.CanOutput;
        SettingsButton.IsEnabled = !_busy;
        ProviderText.Text = _configuration.IsConfigured ? "DeepSeek · " + _configuration.Model : "DeepSeek · 未配置";
        DraftText.IsReadOnly = _busy;
        TargetText.Text = _target is null ? "复制模式 · 尚未绑定输入位置" : "回填到 " + _target.DisplayName + " · 原输入位置";
        InsertButton.Content = "填入原输入框  Ctrl ↵";
    }

    private async Task InsertAsync()
    {
        if (_busy || IsComposing || _target is null || _translation?.State.CanOutput != true) return;
        _busy = true;
        var target = _target;
        var text = _translation.State.Result;
        RefreshActions();
        StatusText.Text = "正在恢复原输入位置…";
        var result = await _insertion.InsertAsync(target, text, _lifetime.Token);
        _target = null;
        _busy = false;
        StatusText.Text = result.Message;
        DiagnosticLog.Write("insertion", result.Outcome.ToString(), target.ProcessName);
        RefreshActions();
        if (result.WasSubmitted) { _translation.Suspend(); Hide(); }
        else if (!_disposed) { Show(); Activate(); DraftText.Focus(); }
    }

    private void HideDraft()
    {
        if (_busy) return;
        _target = null;
        _sourceComposing = _resultComposing = false;
        _translation?.Suspend();
        RefreshActions();
        Hide();
    }

    private async void Insert_Click(object sender, RoutedEventArgs e) => await InsertAsync();
    private async void Copy_Click(object sender, RoutedEventArgs e)
    {
        if (IsComposing || _translation?.State.CanOutput != true) return;
        try { await _platform.WriteClipboardAsync(_translation.State.Result, _lifetime.Token); StatusText.Text = "已复制译文，可以手动粘贴。"; }
        catch (Exception) { StatusText.Text = "剪贴板暂不可用，请稍后重试。"; }
    }
    private void Close_Click(object sender, RoutedEventArgs e) => HideDraft();
    private void Draft_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    { if (!_rendering) _translation?.UpdateSource(DraftText.Text); RefreshActions(); }
    private void Translation_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    { if (!_rendering && !_resultComposing) _translation?.EditResult(TranslationText.Text); }
    private void Retry_Click(object sender, RoutedEventArgs e) => _translation?.Retry();
    private void Settings_Click(object sender, RoutedEventArgs e) => OpenSettings();

    public void OpenSettings()
    {
        if (_busy || _openingSettings) return;
        if (!IsVisible) OpenWithoutTarget();
        _target = null;
        _translation?.Suspend();
        RefreshActions();
        _openingSettings = true;
        try
        {
            var dialog = new SettingsWindow(_settings, _http, _configuration) { Owner = this };
            if (dialog.ShowDialog() == true && dialog.Saved is { } saved)
            {
                ApplyConfiguration(saved);
                DiagnosticLog.Write("settings", "saved");
                StatusText.Text = "DeepSeek 设置已保存。请在目标输入框重新唤出以绑定目标。";
            }
        }
        finally { _openingSettings = false; _translation?.Resume(); Activate(); DraftText.Focus(); }
    }
    private void Title_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    { if (e.OriginalSource is System.Windows.Controls.TextBlock || e.OriginalSource is System.Windows.Controls.Grid) DragMove(); }

    private async void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (IsComposing || e.Key == Key.ImeProcessed) return;
        if (e.Key == Key.Escape) { e.Handled = true; HideDraft(); }
        else if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.Control)
        { e.Handled = true; await InsertAsync(); }
    }

    private void Window_Deactivated(object? sender, EventArgs e)
    {
        DiagnosticLog.Write("popup", _busy ? "restoring-target" : "deactivated");
        if (!_busy && !_openingSettings && IsVisible && _target is not null)
            Dispatcher.BeginInvoke(() => { if (!IsActive && !_busy && !_disposed && !_openingSettings) HideDraft(); });
    }
    private void Window_Closing(object? sender, CancelEventArgs e)
    { if (!_disposed) { e.Cancel = true; HideDraft(); } }

    public void ReleaseResources()
    {
        _disposed = true;
        _lifetime.Cancel();
        _translation?.Dispose();
        _http.Dispose();
        if (_hotKeyRegistered) NativeMethods.UnregisterHotKey(_handle, HotKeyId);
        _source?.RemoveHook(WindowProc);
        _lifetime.Dispose();
    }
}
