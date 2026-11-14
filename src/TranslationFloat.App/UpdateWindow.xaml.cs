using System;
using System.Diagnostics;
using System.Net.Http;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using TranslationFloat.Core;

namespace TranslationFloat.App;

public partial class UpdateWindow : Window
{
    private readonly HttpClient _client = new(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = Timeout.InfiniteTimeSpan };
    private readonly CancellationTokenSource _lifetime = new();
    private readonly ReleaseVersion _current;
    private Uri? _releasePage;
    private bool _checking, _closed, _started;

    public UpdateWindow()
    {
        InitializeComponent();
        var version = typeof(App).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!ReleaseVersion.TryParse(version, out var current)) throw new InvalidOperationException("无法识别当前程序版本。");
        _current = current;
        CurrentVersionText.Text = "当前 v" + current;
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        if (_started) return;
        _started = true;
        await CheckAsync();
    }

    private async Task CheckAsync()
    {
        if (_checking || _closed) return;
        _checking = true;
        _releasePage = null;
        ActionArea.Visibility = Visibility.Visible;
        ActionButton.Visibility = Visibility.Collapsed;
        CheckProgress.Visibility = Visibility.Visible;
        StatusText.Text = "正在检查…";
        DetailText.Text = "正在获取最新正式版本。";
        try
        {
            var result = await new GitHubUpdateChecker(_client).CheckAsync(_current, _lifetime.Token);
            if (_closed) return;
            switch (result.State)
            {
                case UpdateCheckState.Available:
                    StatusText.Text = "发现新版本 v" + result.Latest;
                    DetailText.Text = "下载便携包后，退出随译并完整解压替换。";
                    _releasePage = result.ReleasePage;
                    ActionButton.Content = "前往下载";
                    ActionButton.Visibility = Visibility.Visible;
                    break;
                case UpdateCheckState.Current:
                    ActionArea.Visibility = Visibility.Collapsed;
                    var ahead = _current.CompareTo(result.Latest) > 0;
                    StatusText.Text = ahead ? "当前版本较新" : "已是最新版本";
                    DetailText.Text = ahead ? "最新正式版为 v" + result.Latest + "。" : "可以继续使用随译。";
                    break;
                default:
                    ShowFailure(result.Error!);
                    break;
            }
        }
        catch (OperationCanceledException) when (_closed) { }
        catch (Exception error)
        {
            DiagnosticLog.Write("update-check", error.GetType().Name);
            if (!_closed) ShowFailure("暂时无法检查更新，请稍后重试。");
        }
        finally
        {
            _checking = false;
            if (!_closed) CheckProgress.Visibility = Visibility.Collapsed;
        }
    }

    private void ShowFailure(string message)
    {
        StatusText.Text = "未能检查更新";
        DetailText.Text = message;
        ActionButton.Content = "重试";
        ActionButton.Visibility = Visibility.Visible;
    }

    private async void Action_Click(object sender, RoutedEventArgs e)
    {
        if (_releasePage is null) { await CheckAsync(); return; }
        try { Process.Start(new ProcessStartInfo(_releasePage.AbsoluteUri) { UseShellExecute = true }); }
        catch (Exception error)
        {
            DiagnosticLog.Write("update-open", error.GetType().Name);
            DetailText.Text = "未能打开浏览器，请重试或手动访问 GitHub 上的随译仓库。";
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
    private void Title_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    { if (e.ChangedButton == MouseButton.Left) DragMove(); }
    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    { if (e.Key == Key.Escape) { e.Handled = true; Close(); } }
    private void Window_Closed(object? sender, EventArgs e)
    {
        _closed = true;
        _lifetime.Cancel();
        _client.Dispose();
        _lifetime.Dispose();
    }
}
