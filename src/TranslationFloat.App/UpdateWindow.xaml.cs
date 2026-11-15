using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using TranslationFloat.Core;

namespace TranslationFloat.App;

public partial class UpdateWindow : Window
{
    private readonly HttpClient _client = new(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false }) { Timeout = Timeout.InfiniteTimeSpan };
    private readonly CancellationTokenSource _lifetime = new();
    private readonly ReleaseVersion _current;
    private Uri? _releasePage;
    private ReleaseInstaller? _installer;
    private InstallerFile? _downloaded;
    private readonly Func<bool> _canInstall;
    private bool _checking, _closed, _started, _downloading, _installing;

    public UpdateWindow(Func<bool>? canInstall = null)
    {
        InitializeComponent();
        (Application.Current as App)?.TrackThemeIcon(this);
        _canInstall = canInstall ?? (() => true);
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
        _installer = null;
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
                    _installer = result.Installer;
                    DetailText.Text = _installer is null ? "此版本需要从发布页面下载安装。" : "安装完成后自动重启，本机配置与密钥保留。";
                    _releasePage = result.ReleasePage;
                    ActionButton.Content = _installer is null ? "前往下载" : "下载并安装";
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
        if (_downloading || _installing) return;
        if (_installer is not null) { await DownloadAndInstallAsync(); return; }
        if (_releasePage is null) { await CheckAsync(); return; }
        try { Process.Start(new ProcessStartInfo(_releasePage.AbsoluteUri) { UseShellExecute = true }); }
        catch (Exception error)
        {
            DiagnosticLog.Write("update-open", error.GetType().Name);
            DetailText.Text = "未能打开浏览器，请重试或手动访问 GitHub 上的随译仓库。";
        }
    }

    private async Task DownloadAndInstallAsync()
    {
        _downloading = true;
        ActionButton.Visibility = Visibility.Collapsed;
        CheckProgress.Visibility = Visibility.Visible;
        CheckProgress.IsIndeterminate = false;
        try
        {
            if (_downloaded is null)
            {
                StatusText.Text = "正在下载安装包…";
                var progress = new Progress<DownloadProgress>(value =>
                {
                    if (_closed || !_downloading) return;
                    CheckProgress.Value = value.Percent;
                    DetailText.Text = $"{value.Received / 1048576d:F1} / {value.Total / 1048576d:F1} MB · 关闭可取消";
                });
                _downloaded = await new InstallerDownload(_client).DownloadAsync(_installer!,
                    Path.Combine(Path.GetTempPath(), "Suiyi.Updates"), progress, _lifetime.Token);
            }
            if (_closed) { InstallerDownload.Discard(_downloaded); _downloaded = null; return; }
            StatusText.Text = "正在校验安装包…";
            CheckProgress.IsIndeterminate = true;
            string verifiedHash;
            await using (var stream = File.OpenRead(_downloaded.Path))
                verifiedHash = Convert.ToHexString(await SHA256.HashDataAsync(stream, _lifetime.Token));
            if (!verifiedHash.Equals(_downloaded.Sha256, StringComparison.OrdinalIgnoreCase))
            { InstallerDownload.Discard(_downloaded); _downloaded = null; throw new InstallerDownloadException("安装包校验失败，请重试下载。"); }
            if (_closed) return;
            if (!_canInstall())
            {
                StatusText.Text = "下载完成";
                DetailText.Text = "请完成当前操作并关闭设置，再点击安装更新。";
                ActionButton.Content = "安装更新";
                ActionButton.Visibility = Visibility.Visible;
                return;
            }
            var start = new ProcessStartInfo(_downloaded.Path) { UseShellExecute = true };
            foreach (var argument in new[] { "/SP-", "/SILENT", "/NORESTART", "/SUIYIUPDATE=1" }) start.ArgumentList.Add(argument);
            using var process = Process.Start(start) ?? throw new InvalidOperationException("Installer did not start");
            _installing = true;
            Application.Current.Shutdown();
        }
        catch (OperationCanceledException) when (_closed) { }
        catch (Exception error)
        {
            DiagnosticLog.Write("update-install", error.GetType().Name);
            if (!_closed)
            {
                StatusText.Text = "未能完成更新";
                DetailText.Text = error is InstallerDownloadException ? error.Message : "未能启动安装，请稍后重试。";
                ActionButton.Content = _downloaded is null ? "重试下载" : "安装更新";
                ActionButton.Visibility = Visibility.Visible;
            }
        }
        finally
        {
            _downloading = false;
            if (_closed && !_installing && _downloaded is not null) { InstallerDownload.Discard(_downloaded); _downloaded = null; }
            if (!_closed) { CheckProgress.Visibility = Visibility.Collapsed; CheckProgress.IsIndeterminate = true; }
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
        if (!_installing && !_downloading && _downloaded is not null) InstallerDownload.Discard(_downloaded);
        _client.Dispose();
        _lifetime.Dispose();
    }
}
