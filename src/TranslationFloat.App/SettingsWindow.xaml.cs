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
        KeyHint.Text = current.IsConfigured ? "已有密钥。留空会保留当前密钥；填写新密钥可替换。" : "请粘贴 DeepSeek 控制台创建的 API Key，不要发到聊天里。";
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
        ShowStatus("正在用固定测试句连接 DeepSeek…", false);
        try
        {
            var key = string.IsNullOrWhiteSpace(ApiKeyBox.Password) ? _current.ApiKey : ApiKeyBox.Password.Trim();
            var provider = new DeepSeekTranslationProvider(_client, key, SelectedModel);
            var translation = await provider.TranslateAsync(new("你好，世界。"), _lifetime.Token);
            if (!_lifetime.IsCancellationRequested) ShowStatus("连接成功：" + translation, false);
        }
        catch (OperationCanceledException) { }
        catch (TranslationException error) { ShowStatus(error.Message, true); }
        catch (ArgumentException) { ShowStatus("请先填写有效的 DeepSeek API Key。", true); }
        finally { TestButton.IsEnabled = SaveButton.IsEnabled = true; }
    }

    private void ShowStatus(string message, bool error)
    { TestStatus.Text = message; TestStatus.Foreground = new SolidColorBrush(error ? Color.FromRgb(164, 57, 47) : Color.FromRgb(52, 104, 73)); }
    private void Cancel_Click(object sender, RoutedEventArgs e) => Close();
    private void Window_Closed(object? sender, EventArgs e) { _lifetime.Cancel(); ApiKeyBox.Clear(); }
}
