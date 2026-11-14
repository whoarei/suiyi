using System.Net;
using System.Security.Cryptography;

namespace TranslationFloat.Core;

public sealed record ReleaseInstaller(string Version, string Tag, long Size, string Sha256)
{
    public const long MaximumSize = 256 * 1024 * 1024;
    public string FileName => $"suiyi-{Version}-windows-x64-setup.exe";
    public Uri DownloadUri => new("https://github.com/whoarei/suiyi/releases/download/" + Uri.EscapeDataString(Tag) + "/" + FileName);
    public bool IsValid => ReleaseVersion.TryParse(Tag, out var version) && version.Prerelease is null
        && version.ToString() == Version && Size is > 0 and <= MaximumSize
        && Sha256.Length == 64 && Sha256.All(Uri.IsHexDigit);
}

public sealed record InstallerFile(string Path, string Directory, string Sha256);
public sealed record DownloadProgress(long Received, long Total)
{
    public double Percent => (double)Received / Total * 100;
}
public sealed class InstallerDownloadException(string message) : Exception(message);

public sealed class InstallerDownload(HttpClient client)
{
    public async Task<InstallerFile> DownloadAsync(ReleaseInstaller installer, string cacheRoot,
        IProgress<DownloadProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        if (!installer.IsValid) throw new InstallerDownloadException("安装包信息不完整，请重新检查更新。");
        var folder = System.IO.Path.Combine(System.IO.Path.GetFullPath(cacheRoot), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var partial = System.IO.Path.Combine(folder, "download.part");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(10));
        try
        {
            using var response = await OpenDownloadAsync(installer.DownloadUri, timeout.Token).ConfigureAwait(false);
            if (response.Content.Headers.ContentLength is long length && length != installer.Size)
                throw new InstallerDownloadException("安装包大小不符，请重试下载。");
            await using (var source = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false))
            await using (var target = new FileStream(partial, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, true))
            using (var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
            {
                var buffer = new byte[65536];
                long total = 0;
                var reportTime = System.Diagnostics.Stopwatch.StartNew();
                progress?.Report(new(0, installer.Size));
                int count;
                while ((count = await source.ReadAsync(buffer, timeout.Token).ConfigureAwait(false)) > 0)
                {
                    total += count;
                    if (total > installer.Size) throw new InstallerDownloadException("安装包大小不符，请重试下载。");
                    hash.AppendData(buffer, 0, count);
                    await target.WriteAsync(buffer.AsMemory(0, count), timeout.Token).ConfigureAwait(false);
                    if (reportTime.ElapsedMilliseconds >= 150)
                    { progress?.Report(new(total, installer.Size)); reportTime.Restart(); }
                }
                if (total != installer.Size) throw new InstallerDownloadException("安装包下载不完整，请重试。");
                if (!Convert.ToHexString(hash.GetHashAndReset()).Equals(installer.Sha256, StringComparison.OrdinalIgnoreCase))
                    throw new InstallerDownloadException("安装包校验失败，请重试下载。");
                progress?.Report(new(total, installer.Size));
            }
            cancellationToken.ThrowIfCancellationRequested();
            var path = System.IO.Path.Combine(folder, installer.FileName);
            File.Move(partial, path);
            return new(path, folder, installer.Sha256);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        { throw new InstallerDownloadException("下载超时，请检查网络后重试。"); }
        catch (HttpRequestException) { throw new InstallerDownloadException("无法下载安装包，请检查网络后重试。"); }
        catch (IOException) { throw new InstallerDownloadException("无法保存安装包，请检查磁盘空间后重试。"); }
        catch (UnauthorizedAccessException) { throw new InstallerDownloadException("无法保存安装包，请检查下载目录权限。"); }
        finally
        {
            if (File.Exists(partial)) Discard(new(partial, folder, installer.Sha256));
            else if (!Directory.EnumerateFiles(folder).Any()) Directory.Delete(folder);
        }
    }

    private async Task<HttpResponseMessage> OpenDownloadAsync(Uri initial, CancellationToken cancellationToken)
    {
        var uri = initial;
        for (var attempt = 0; attempt < 6; attempt++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.UserAgent.ParseAdd("Suiyi-Updater/1.0");
            var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.OK) return response;
            var next = response.Headers.Location;
            var redirect = response.StatusCode is HttpStatusCode.MovedPermanently or HttpStatusCode.Redirect
                or HttpStatusCode.RedirectMethod or HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect;
            response.Dispose();
            if (!redirect || next is null) throw new InstallerDownloadException("暂时无法下载安装包，请稍后重试。");
            next = next.IsAbsoluteUri ? next : new Uri(uri, next);
            if (next.Scheme != Uri.UriSchemeHttps || !next.IsDefaultPort || next.UserInfo.Length > 0 || next.Fragment.Length > 0
                || !(next.Host.Equals("release-assets.githubusercontent.com", StringComparison.OrdinalIgnoreCase)
                    || next.Host.Equals("objects.githubusercontent.com", StringComparison.OrdinalIgnoreCase)
                    || next == initial)) throw new InstallerDownloadException("安装包下载地址无效，请重新检查更新。");
            uri = next;
        }
        throw new InstallerDownloadException("下载重定向过多，请稍后重试。");
    }

    public static void Discard(InstallerFile file)
    {
        // Only a direct file in the random download folder can be removed here.
        if (!Guid.TryParseExact(System.IO.Path.GetFileName(file.Directory), "N", out _)
            || System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(file.Path)) != System.IO.Path.GetFullPath(file.Directory)) return;
        try { File.Delete(file.Path); if (!Directory.EnumerateFileSystemEntries(file.Directory).Any()) Directory.Delete(file.Directory); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
