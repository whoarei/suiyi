using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using TranslationFloat.Core;

namespace TranslationFloat.App;

public partial class SettingsWindow : Window
{
    private readonly Func<string, string, string, HotKeyBinding, bool, TranslationConfiguration> _save;
    private readonly Action<bool> _recording;
    private readonly TranslationSettingsStore _store;
    private readonly HttpClient _client;
    private readonly CancellationTokenSource _lifetime = new();
    private HotKeyBinding _hotKey;
    internal TranslationConfiguration? Saved { get; private set; }

    internal SettingsWindow(TranslationSettingsStore store, HttpClient client, TranslationConfiguration current,
        HotKeyBinding hotKey, bool quickMode,
        Func<string, string, string, HotKeyBinding, bool, TranslationConfiguration> save, Action<bool> recording)
    {
        _store = store; _client = client; _save = save; _recording = recording; _hotKey = hotKey;
        InitializeComponent();
        AddressBox.Text = current.BaseUrl;
        ModelBox.Text = current.Model;
        ShortcutBox.Text = hotKey.DisplayName;
        QuickModeBox.IsChecked = quickMode;
        UpdateKeyHint();
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            _recording(false);
            Saved = _save(AddressBox.Text.Trim(), ModelBox.Text.Trim(), ApiKeyBox.Password, _hotKey, QuickModeBox.IsChecked == true);
            ApiKeyBox.Clear();
            DialogResult = true;
        }
        catch (ArgumentException error) { ShowStatus(error.Message, true); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Security.Cryptography.CryptographicException or System.Security.SecurityException)
        { ShowStatus("无法保存设置，请检查用户目录权限。", true); }
    }

    private async void FetchModels_Click(object sender, RoutedEventArgs e) => await RunRequestAsync(async token =>
    {
        var address = AddressBox.Text.Trim();
        var key = _store.ResolveKey(address, ApiKeyBox.Password);
        var models = await new OpenAiModelCatalog(_client).ListAsync(address, key, token);
        token.ThrowIfCancellationRequested();
        var selected = ModelBox.Text;
        ModelBox.ItemsSource = models;
        ModelBox.Text = selected;
        ShowStatus(models.Count == 0 ? "未返回模型，可手动填写。" : $"已获取 {models.Count} 个模型，可选择或手动填写。", false);
        return models.Count > 0;
    });

    private async void Test_Click(object sender, RoutedEventArgs e) => await RunRequestAsync(async token =>
    {
        var address = AddressBox.Text.Trim();
        var key = _store.ResolveKey(address, ApiKeyBox.Password);
        var provider = new OpenAiTranslationProvider(_client, address, key, ModelBox.Text.Trim());
        await provider.TranslateAsync(new("你好，世界。"), token);
        await provider.TranslateAsync(new("第一行。\n\n第二行。"), token);
        token.ThrowIfCancellationRequested();
        ShowStatus("测试通过", false);
        return false;
    });

    private async Task RunRequestAsync(Func<CancellationToken, Task<bool>> request)
    {
        Fields.IsEnabled = TestButton.IsEnabled = SaveButton.IsEnabled = false;
        ShowStatus("正在连接…", false);
        var openModels = false;
        try { openModels = await request(_lifetime.Token); }
        catch (OperationCanceledException) { }
        catch (TranslationException error) { ShowStatus(error.Message, true); }
        catch (ModelCatalogException error) { ShowStatus(error.Message, true); }
        catch (ArgumentException error) { ShowStatus(error.Message, true); }
        catch (Exception) { ShowStatus("请求失败，请检查接口设置。", true); }
        finally
        {
            if (!_lifetime.IsCancellationRequested)
            {
                Fields.IsEnabled = TestButton.IsEnabled = SaveButton.IsEnabled = true;
                if (openModels && IsActive) { ModelBox.Focus(); ModelBox.IsDropDownOpen = true; }
            }
        }
    }

    private void Address_Changed(object sender, TextChangedEventArgs e)
    {
        ClearStatus();
        if (ModelBox is not null)
        {
            var selected = ModelBox.Text;
            ModelBox.ItemsSource = null;
            ModelBox.Text = selected;
        }
        UpdateKeyHint();
    }
    private void ApiKey_Changed(object sender, RoutedEventArgs e) { ClearStatus(); UpdateKeyHint(); }
    private void Model_Changed(object sender, TextChangedEventArgs e) => ClearStatus();
    private void ClearStatus() { if (TestStatus is not null) TestStatus.Visibility = Visibility.Collapsed; }
    private void UpdateKeyHint()
    {
        if (KeyHint is null || ApiKeyBox is null || AddressBox is null) return;
        var saved = false;
        if (ApiKeyBox.Password.Length == 0)
        {
            try { saved = _store.ResolveKey(AddressBox.Text, "").Length > 0; }
            catch (ArgumentException) { }
        }
        KeyHint.Visibility = saved ? Visibility.Visible : Visibility.Collapsed;
    }
    private void Shortcut_GotFocus(object sender, KeyboardFocusChangedEventArgs e) => _recording(true);
    private void Shortcut_LostFocus(object sender, KeyboardFocusChangedEventArgs e) => _recording(false);
    private void Shortcut_KeyDown(object sender, KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key is Key.Tab or Key.Escape) return;
        e.Handled = true;
        var candidate = new HotKeyBinding((uint)Keyboard.Modifiers, (uint)KeyInterop.VirtualKeyFromKey(key));
        if (!candidate.IsValid) return;
        _hotKey = candidate;
        ShortcutBox.Text = candidate.DisplayName;
    }
    private void ResetHotKey_Click(object sender, RoutedEventArgs e)
    { _hotKey = HotKeyBinding.Default; ShortcutBox.Text = _hotKey.DisplayName; }
    private void Close_Click(object sender, RoutedEventArgs e) => Close();
    private void Title_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    { if (e.OriginalSource is TextBlock || e.OriginalSource is Grid) DragMove(); }
    private void ShowStatus(string message, bool error)
    {
        if (_lifetime.IsCancellationRequested) return;
        TestStatus.Text = message; TestStatus.Visibility = Visibility.Visible;
        TestStatus.Foreground = new SolidColorBrush(error ? Color.FromRgb(164, 57, 47) : Color.FromRgb(52, 104, 73));
    }
    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    { if (e.Key == Key.Escape && !ModelBox.IsDropDownOpen) { e.Handled = true; Close(); } }
    private void Window_Closed(object? sender, EventArgs e)
    { _lifetime.Cancel(); _recording(false); ApiKeyBox.Clear(); }
}
