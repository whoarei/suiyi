using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using TranslationFloat.Core;

namespace TranslationFloat.App;

public partial class SettingsWindow : Window
{
    private readonly DeepSeekSettingsStore _store;
    private readonly HttpClient _client;
    private readonly DeepSeekConfiguration _current;
    private readonly CancellationTokenSource _lifetime = new();
    internal DeepSeekConfiguration? Saved { get; private set; }

    internal SettingsWindow(DeepSeekSettingsStore store, HttpClient client, DeepSeekConfiguration current)
    {
        InitializeComponent();
        _store = store; _client = client; _current = current;
        ModelBox.SelectedIndex = current.Model == "deepseek-v4-pro" ? 1 : 0;
        KeyHint.Text = current.IsConfigured ? "留空保留已有密钥。" : "密钥在本机加密保存。";
    }

    private string SelectedModel => (ModelBox.SelectedItem as ComboBoxItem)?.Content as string ?? DeepSeekTranslationProvider.DefaultModel;
    private void Save_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Saved = _store.Save(SelectedModel, ApiKeyBox.Password);
            ApiKeyBox.Clear();
            DialogResult = true;
        }
        catch (ArgumentException error) { ShowStatus(error.Message, true); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Security.Cryptography.CryptographicException)
        { ShowStatus("无法保存本机设置，请检查用户目录权限。", true); }
    }

    private async void Test_Click(object sender, RoutedEventArgs e)
    {
        TestButton.IsEnabled = SaveButton.IsEnabled = false;
        ShowStatus("正在连接…", false);
        try
        {
            var key = string.IsNullOrWhiteSpace(ApiKeyBox.Password) ? _current.ApiKey : ApiKeyBox.Password.Trim();
            var provider = new DeepSeekTranslationProvider(_client, key, SelectedModel);
            await provider.TranslateAsync(new("你好，世界。"), _lifetime.Token);
            if (!_lifetime.IsCancellationRequested) ShowStatus("连接成功", false);
        }
        catch (OperationCanceledException) { }
        catch (TranslationException error) { ShowStatus(error.Message, true); }
        catch (ArgumentException) { ShowStatus("请先填写有效的 DeepSeek API Key。", true); }
        finally { TestButton.IsEnabled = SaveButton.IsEnabled = true; }
    }

    private void ShowStatus(string message, bool error)
    { TestStatus.Text = message; TestStatus.Visibility = Visibility.Visible; TestStatus.Foreground = new SolidColorBrush(error ? Color.FromRgb(164, 57, 47) : Color.FromRgb(52, 104, 73)); }
    private void Window_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    { if (e.Key == System.Windows.Input.Key.Escape && !ModelBox.IsDropDownOpen) { e.Handled = true; Close(); } }
    private void Window_Closed(object? sender, EventArgs e) { _lifetime.Cancel(); ApiKeyBox.Clear(); }
}
