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
    private readonly TranslationSettingsStore _settings = new();
    private readonly HotKeySettingsStore _hotKeySettings = new();
    private readonly QuickModeSettingsStore _quickSettings = new();
    private HotKeyBinding _hotKey = HotKeyBinding.Default;
    private int _registeredHotKeyId = HotKeyId;
    private readonly HttpClient _http = new(new SocketsHttpHandler { AllowAutoRedirect = false }) { Timeout = Timeout.InfiniteTimeSpan };
    private TranslationConfiguration _configuration = new("", "", "");
    private bool _shortcutRecording;
    private TranslationController? _translation;
    private ITranslationProvider? _provider;
    private CancellationTokenSource? _quickCancellation;
    private TargetSession? _target;
    private HwndSource? _source;
    private nint _handle;
    private string? _notice;
    private bool _noticeIsError;
    private bool _selectionDraft;
    private string _targetHint = "";
    private bool _busy, _sourceComposing, _resultComposing, _disposed, _hotKeyRegistered, _rendering, _openingSettings, _capturing;
    private bool IsComposing => _sourceComposing || _resultComposing;
    public bool QuickModeEnabled { get; private set; }
    public bool IsWorking => _busy || _capturing;
    public bool CanInstallUpdate => !IsWorking && !_openingSettings;
    public event Action<string>? QuickNotice;
    public event Action? HotKeyChanged;
    public string Shortcut => _hotKey.DisplayName;

    public void SetQuickModeEnabled(bool enabled)
    {
        QuickModeEnabled = enabled;
        if (!enabled) _quickCancellation?.Cancel();
    }

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

    private void ApplyConfiguration(TranslationConfiguration configuration)
    {
        _configuration = configuration;
        ITranslationProvider? provider = null;
        if (configuration.IsConfigured)
        {
            try { provider = new OpenAiTranslationProvider(_http, configuration.BaseUrl, configuration.ApiKey, configuration.Model); }
            catch (ArgumentException) { _configuration = configuration with { Model = "" }; }
        }
        _provider = provider;
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
        try { _hotKey = _hotKeySettings.Load(); }
        catch (Exception error) { DiagnosticLog.Write("hotkey-settings", error.GetType().Name); }
        _hotKeyRegistered = NativeMethods.RegisterHotKey(_handle, _registeredHotKeyId, 0x4000 | _hotKey.Modifiers, _hotKey.VirtualKey);
        if (!_hotKeyRegistered && _hotKey != HotKeyBinding.Default)
        {
            _hotKey = HotKeyBinding.Default;
            _hotKeyRegistered = NativeMethods.RegisterHotKey(_handle, _registeredHotKeyId, 0x4000 | _hotKey.Modifiers, _hotKey.VirtualKey);
        }
        if (!_hotKeyRegistered) SetNotice("快捷键已被占用，请从托盘设置更换。", true);
        _platform.TriggerKey = (int)_hotKey.VirtualKey;
        DiagnosticLog.Write("started", _hotKeyRegistered ? "hotkey-registered" : "hotkey-unavailable");
    }

    private nint WindowProc(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
    {
        if (message != 0x0312 || wParam != _registeredHotKeyId) return 0;
        DiagnosticLog.Write("hotkey", "received");
        handled = true;
        if (_openingSettings) return 0;
        if (_quickCancellation is not null) { _quickCancellation.Cancel(); return 0; }
        if (_busy || _capturing) return 0;
        if (IsVisible && IsActive) { HideDraft(); return 0; }
        // Capture BEFORE Show/Activate: otherwise the foreground window is this popup.
        _ = QuickModeEnabled ? TranslateSelectionQuicklyAsync() : OpenFromForegroundAsync();
        return 0;
    }

    private async Task TranslateSelectionQuicklyAsync()
    {
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        _quickCancellation = cancellation;
        _busy = _capturing = true;
        _translation?.Suspend();
        Hide();
        try
        {
            var capture = await _platform.CaptureTargetAsync(cancellation.Token);
            cancellation.Token.ThrowIfCancellationRequested();
            if (_disposed) return;
            _target = capture.Target;
            if (capture.Target is null)
            {
                QuickNotice?.Invoke(capture.Message);
                DiagnosticLog.Write("quick-translation", "no-target");
                return;
            }
            var result = await new QuickTranslationCoordinator(_provider, _insertion)
                .TranslateAndReplaceAsync(capture.Target, capture.SelectedText, cancellation.Token);
            if (_disposed) return;
            DiagnosticLog.Write("quick-translation", result.Outcome.ToString(), capture.Target.ProcessName);
            if (result.Outcome == InsertionOutcome.Submitted)
            {
                DraftText.Clear();
                _selectionDraft = false;
                SetNotice(null);
            }
            else QuickNotice?.Invoke(result.Message.Replace("Ctrl+Alt+T", Shortcut, StringComparison.Ordinal));
        }
        catch (OperationCanceledException)
        { if (!_disposed) QuickNotice?.Invoke("快速翻译已取消。"); }
        catch (Exception)
        {
            DiagnosticLog.Write("quick-translation", "failed");
            if (!_disposed) QuickNotice?.Invoke("快速翻译失败，未替换。请重试。");
        }
        finally
        {
            _target = null;
            _quickCancellation = null;
            _busy = _capturing = false;
            if (!_disposed) RefreshActions();
        }
    }

    private async Task OpenFromForegroundAsync()
    {
        _capturing = true;
        try
        {
            var capture = await _platform.CaptureTargetAsync(_lifetime.Token);
            if (_disposed) return;
            _target = capture.Target;
            _translation?.Suspend();
            if (capture.SelectedText is { } selected)
            {
                DraftText.Text = selected;
                _selectionDraft = true;
            }
            else if (_selectionDraft)
            {
                DraftText.Clear();
                _selectionDraft = false;
            }
            DiagnosticLog.Write("capture-result", capture.Target is null ? "copy-mode" : "bound");
            _targetHint = capture.Message;
            _notice = capture.Target is null ? capture.Message : null;
            _noticeIsError = capture.Target is null;
            RefreshActions();
            OpenPopup();
        }
        finally { _capturing = false; }
    }

    public void OpenWithoutTarget()
    {
        if (_busy || _capturing) return;
        _target = null;
        _notice = _hotKeyRegistered ? null : "快捷键已被占用，请从托盘设置更换。";
        _noticeIsError = !_hotKeyRegistered;
        _targetHint = "在目标输入框按 " + Shortcut + " 唤出，可回填译文。";
        RefreshActions();
        OpenPopup();
    }

    private void OpenPopup()
    {
        if (!IsVisible)
        {
            Measure(new Size(Width, double.PositiveInfinity));
            var position = _platform.GetPopupPosition(Width, DesiredSize.Height > 0 ? DesiredSize.Height : 280);
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
        if (PrimaryButton is null || _translation is null) return;
        var state = _translation.State;
        _rendering = true;
        try
        {
            var visibleResult = state.CanEdit ? state.Result : "";
            if (!_resultComposing && TranslationText.Text != visibleResult) TranslationText.Text = visibleResult;
        }
        finally { _rendering = false; }
        TranslationText.IsReadOnly = _busy || !state.CanEdit;
        DraftPlaceholder.Visibility = string.IsNullOrEmpty(DraftText.Text) ? Visibility.Visible : Visibility.Collapsed;
        ResultPlaceholder.Text = state.Phase switch
        {
            TranslationPhase.Waiting => "等待翻译…",
            TranslationPhase.Translating => "翻译中…",
            TranslationPhase.Composing => "选词中…",
            TranslationPhase.Error => "翻译未完成",
            _ => "译文"
        };
        ResultPlaceholder.Visibility = string.IsNullOrEmpty(TranslationText.Text) ? Visibility.Visible : Visibility.Collapsed;
        var needsConfiguration = !_configuration.IsConfigured;
        var feedback = state.Phase == TranslationPhase.Error ? state.Message : _notice ?? (needsConfiguration ? "请先设置翻译接口。" : null);
        FeedbackText.Text = feedback ?? "";
        FeedbackText.Foreground = new SolidColorBrush(_noticeIsError || state.Phase == TranslationPhase.Error ? Color.FromRgb(164, 57, 47) : Color.FromRgb(94, 117, 102));
        FeedbackPanel.Visibility = string.IsNullOrEmpty(feedback) ? Visibility.Collapsed : Visibility.Visible;
        RetryButton.Visibility = state.Phase == TranslationPhase.Error ? Visibility.Visible : Visibility.Collapsed;
        RetryButton.IsEnabled = !_busy && !IsComposing;
        ConfigureButton.Visibility = needsConfiguration ? Visibility.Visible : Visibility.Collapsed;
        ConfigureButton.IsEnabled = !_busy;
        PrimaryButton.IsEnabled = !_busy && !IsComposing && state.CanOutput;
        CopyButton.IsEnabled = !_busy && !IsComposing && state.CanOutput;
        CopyButton.Visibility = _target is null ? Visibility.Collapsed : Visibility.Visible;
        DraftText.IsReadOnly = _busy;
        TargetText.Text = _target?.DisplayName ?? "";
        TargetText.ToolTip = _targetHint;
        var action = _target?.Selection is null ? "填入" : "替换";
        PrimaryButton.Content = _busy ? "…" : _target is null ? "复制" : action;
        PrimaryButton.ToolTip = _target is null ? "复制译文 · Ctrl+Enter" : action + " " + _target.DisplayName + " · Ctrl+Enter\n" + _targetHint;
    }

    private async Task InsertAsync()
    {
        if (_busy || IsComposing || _target is null || _translation?.State.CanOutput != true) return;
        _busy = true;
        var target = _target;
        var text = _translation.State.Result;
        RefreshActions();
        SetNotice("正在回填…");
        var result = await _insertion.InsertAsync(target, text, _lifetime.Token);
        _target = null;
        _busy = false;
        _notice = result.Message;
        _noticeIsError = result.Outcome != InsertionOutcome.Submitted;
        DiagnosticLog.Write("insertion", result.Outcome.ToString(), target.ProcessName);
        if (result.Outcome == InsertionOutcome.Submitted)
        {
            _translation.Suspend();
            // Clearing the source also cancels old responses and clears the result.
            DraftText.Clear();
            _selectionDraft = false;
            SetNotice(null);
            Hide();
        }
        else if (result.WasSubmitted) { RefreshActions(); _translation.Suspend(); Hide(); }
        else if (!_disposed) { RefreshActions(); Show(); Activate(); DraftText.Focus(); }
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

    private void SetNotice(string? message, bool error = false)
    { _notice = message; _noticeIsError = error; RefreshActions(); }

    private Task ExecutePrimaryAsync() => _target is null ? CopyAsync() : InsertAsync();
    private async void Primary_Click(object sender, RoutedEventArgs e) => await ExecutePrimaryAsync();
    private async void Copy_Click(object sender, RoutedEventArgs e) => await CopyAsync();
    private async Task CopyAsync()
    {
        if (_busy || IsComposing || _translation?.State.CanOutput != true) return;
        try { await _platform.WriteClipboardAsync(_translation.State.Result, _lifetime.Token); SetNotice("已复制"); }
        catch (Exception) { SetNotice("剪贴板暂不可用，请稍后重试。", true); }
    }
    private void Close_Click(object sender, RoutedEventArgs e) => HideDraft();
    private void Draft_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    { if (!_rendering) { _notice = null; _noticeIsError = false; _translation?.UpdateSource(DraftText.Text); } RefreshActions(); }
    private void Translation_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        if (_rendering) return;
        _notice = null; _noticeIsError = false;
        if (!_resultComposing) _translation?.EditResult(TranslationText.Text);
        RefreshActions();
    }
    private void Retry_Click(object sender, RoutedEventArgs e) { _notice = null; _noticeIsError = false; _translation?.Retry(); }
    private void Settings_Click(object sender, RoutedEventArgs e) => OpenSettings();

    public void OpenSettings()
    {
        if (_busy || _capturing || _openingSettings) return;
        var wasVisible = IsVisible;
        _target = null;
        _translation?.Suspend();
        RefreshActions();
        _openingSettings = true;
        try
        {
            var dialog = new SettingsWindow(_settings, _http, _configuration, _hotKey, QuickModeEnabled, SaveSettings, SetShortcutRecording)
            { Owner = this, WindowStartupLocation = wasVisible ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen };
            if (dialog.ShowDialog() == true && dialog.Saved is { } saved)
            {
                ApplyConfiguration(saved);
                DiagnosticLog.Write("settings", "saved");
                SetNotice(null);
            }
        }
        finally
        {
            SetShortcutRecording(false);
            _openingSettings = false;
            if (wasVisible) { _translation?.Resume(); Activate(); DraftText.Focus(); }
        }
    }

    private void SetShortcutRecording(bool recording)
    {
        if (recording)
        {
            if (_shortcutRecording) return;
            _shortcutRecording = true;
            if (_hotKeyRegistered) NativeMethods.UnregisterHotKey(_handle, _registeredHotKeyId);
            _hotKeyRegistered = false;
        }
        else if (_shortcutRecording)
        {
            _shortcutRecording = false;
            _hotKeyRegistered = NativeMethods.RegisterHotKey(_handle, _registeredHotKeyId, 0x4000 | _hotKey.Modifiers, _hotKey.VirtualKey);
            if (!_hotKeyRegistered) SetNotice("快捷键已被占用，请从托盘设置更换。", true);
        }
    }

    private TranslationConfiguration SaveSettings(string baseUrl, string model, string replacementKey, HotKeyBinding binding, bool quickMode)
    {
        SetShortcutRecording(false);
        if (!binding.IsValid) throw new ArgumentException("请使用 Ctrl 或 Alt，搭配字母、数字或 F1–F12。");
        var changed = binding != _hotKey || !_hotKeyRegistered;
        var candidateId = _registeredHotKeyId == HotKeyId ? HotKeyId + 1 : HotKeyId;
        if (changed && !NativeMethods.RegisterHotKey(_handle, candidateId, 0x4000 | binding.Modifiers, binding.VirtualKey))
            throw new ArgumentException("这个快捷键已被占用，请换一个组合。" + (_hotKeyRegistered ? "原快捷键仍然有效。" : ""));
        try
        {
            var saved = _settings.Save(baseUrl, model, replacementKey);
            _hotKeySettings.Save(binding);
            _quickSettings.Save(quickMode);
            if (changed)
            {
                if (_hotKeyRegistered) NativeMethods.UnregisterHotKey(_handle, _registeredHotKeyId);
                _registeredHotKeyId = candidateId;
                _hotKeyRegistered = true;
                _hotKey = binding;
                _platform.TriggerKey = (int)_hotKey.VirtualKey;
            }
            SetQuickModeEnabled(quickMode);
            HotKeyChanged?.Invoke();
            return saved;
        }
        catch
        {
            if (changed) NativeMethods.UnregisterHotKey(_handle, candidateId);
            try { _hotKeySettings.Save(_hotKey); _quickSettings.Save(QuickModeEnabled); }
            catch (Exception) { }
            throw;
        }
    }

    private void Title_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    { if (e.OriginalSource is System.Windows.Controls.TextBlock || e.OriginalSource is System.Windows.Controls.Grid) DragMove(); }

    private async void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (IsComposing || e.Key == Key.ImeProcessed) return;
        if (e.Key == Key.Escape) { e.Handled = true; HideDraft(); }
        else if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.Control)
        { e.Handled = true; await ExecutePrimaryAsync(); }
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
        if (_hotKeyRegistered) NativeMethods.UnregisterHotKey(_handle, _registeredHotKeyId);
        _source?.RemoveHook(WindowProc);
        _lifetime.Dispose();
    }
}
