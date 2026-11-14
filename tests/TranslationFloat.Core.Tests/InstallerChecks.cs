using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TranslationFloat.Core;

internal static class InstallerChecks
{
    private static readonly byte[] Payload = Encoding.UTF8.GetBytes("synthetic installer fixture; never executed");
    private static readonly ReleaseInstaller Installer = new("0.2.2", "v0.2.2", Payload.Length, Convert.ToHexString(SHA256.HashData(Payload)));

    public static IEnumerable<(string Name, Func<Task> Run)> Cases()
    {
        yield return ("从正式发布附件读取安装包摘要并构造固定仓库下载地址", async () =>
        {
            using var client = Client((_, _) => Task.FromResult(Metadata(Asset())));
            ReleaseVersion.TryParse("0.2.0", out var current);
            var result = await new GitHubUpdateChecker(client).CheckAsync(current);
            Check(result.Installer?.IsValid == true && result.Installer.DownloadUri == new Uri("https://github.com/whoarei/suiyi/releases/download/v0.2.2/suiyi-0.2.2-windows-x64-setup.exe"));
        });
        yield return ("没有有效摘要或附件尺寸类型错误时仅保留手动入口", async () =>
        {
            foreach (var asset in new[] { Asset(digest: "sha256:bad"), Asset(size: "invalid"), Asset(size: 0),
                Asset(size: ReleaseInstaller.MaximumSize + 1), Asset(state: "new"), Asset(name: "other.exe") })
            {
                using var client = Client((_, _) => Task.FromResult(Metadata(asset)));
                ReleaseVersion.TryParse("0.2.0", out var current);
                var result = await new GitHubUpdateChecker(client).CheckAsync(current);
                Check(result.State == UpdateCheckState.Available && result.Installer is null);
            }
        });
        yield return ("经过官方 CDN 重定向下载并验证大小和 SHA256", async () =>
        {
            using var fixture = new Fixture(); var calls = 0; var progress = new List<DownloadProgress>();
            using var client = Client((request, _) =>
            {
                calls++;
                Check(request.Headers.Authorization is null && !request.Headers.Contains("Cookie"));
                return Task.FromResult(calls == 1 ? Redirect("https://release-assets.githubusercontent.com/test") : Body(Payload));
            });
            var file = await new InstallerDownload(client).DownloadAsync(Installer, fixture.Root, new ProgressSink(progress));
            Check(calls == 2 && File.ReadAllBytes(file.Path).SequenceEqual(Payload) && file.Sha256 == Installer.Sha256);
            Check(progress.First().Received == 0 && progress.Last().Percent == 100 && !File.Exists(Path.Combine(file.Directory, "download.part")));
            InstallerDownload.Discard(file); Check(!Directory.Exists(file.Directory));
        });
        yield return ("拒绝非 HTTPS、非官方主机和带凭据的下载重定向", async () =>
        {
            foreach (var url in new[] { "http://release-assets.githubusercontent.com/test", "https://evil.test/setup.exe", "https://user@release-assets.githubusercontent.com/test", "https://release-assets.githubusercontent.com:444/test" })
            {
                using var fixture = new Fixture(); var calls = 0;
                using var client = Client((_, _) => { calls++; return Task.FromResult(Redirect(url)); });
                await Fails(() => new InstallerDownload(client).DownloadAsync(Installer, fixture.Root));
                Check(calls == 1 && Empty(fixture.Root));
            }
        });
        yield return ("CDN 重定向次数有上限", async () =>
        {
            using var fixture = new Fixture(); var calls = 0;
            using var client = Client((_, _) => { calls++; return Task.FromResult(Redirect("https://release-assets.githubusercontent.com/loop")); });
            await Fails(() => new InstallerDownload(client).DownloadAsync(Installer, fixture.Root));
            Check(calls == 6 && Empty(fixture.Root));
        });
        yield return ("摘要不匹配时不保留可执行文件", async () =>
        { await InvalidDownload(Installer with { Sha256 = new string('0', 64) }, () => Body(Payload)); });
        yield return ("下载中断或少收数据不产生安装包", async () =>
        { await InvalidDownload(Installer, () => Body(Payload[..^1], unknownLength: true)); });
        yield return ("未声明长度时也不能超过附件大小", async () =>
        { await InvalidDownload(Installer, () => Body(Payload.Concat(new byte[] { 0 }).ToArray(), unknownLength: true)); });
        yield return ("声明长度与附件不符时在读取前拒绝", async () =>
        { await InvalidDownload(Installer, () => Body([0])); });
        yield return ("下载请求可取消并清理暂存目录", async () =>
        {
            using var fixture = new Fixture(); using var cancel = new CancellationTokenSource();
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            using var client = Client(async (_, token) => { entered.SetResult(); await Task.Delay(Timeout.Infinite, token); return Body(Payload); });
            var task = new InstallerDownload(client).DownloadAsync(Installer, fixture.Root, cancellationToken: cancel.Token);
            await entered.Task; cancel.Cancel();
            try { await task; throw new InvalidOperationException("Cancellation was swallowed"); } catch (OperationCanceledException) { }
            Check(Empty(fixture.Root));
        });
        yield return ("失败状态和局部响应不作为安装包", async () =>
        {
            foreach (var status in new[] { HttpStatusCode.Forbidden, HttpStatusCode.NotFound, HttpStatusCode.ServiceUnavailable, HttpStatusCode.PartialContent })
                await InvalidDownload(Installer, () => new(status) { Content = new StringContent("private-server-detail") });
        });
        yield return ("下载网络错误不显示服务端细节", async () =>
        {
            using var fixture = new Fixture();
            using var client = Client((_, _) => throw new HttpRequestException("private-network-detail"));
            var error = await Fails(() => new InstallerDownload(client).DownloadAsync(Installer, fixture.Root));
            Check(!error.Message.Contains("private-network-detail") && Empty(fixture.Root));
        });
        yield return ("下载超时允许重试且清理暂存", async () =>
        {
            using var fixture = new Fixture(); using var client = Client((_, _) => throw new TaskCanceledException());
            var error = await Fails(() => new InstallerDownload(client).DownloadAsync(Installer, fixture.Root));
            Check(error.Message.Contains("超时") && Empty(fixture.Root));
        });
        yield return ("不合法的安装包元数据不发出请求", async () =>
        {
            foreach (var installer in new[] { Installer with { Version = "../evil" }, Installer with { Tag = "v0.2.3" },
                Installer with { Size = 0 }, Installer with { Size = ReleaseInstaller.MaximumSize + 1 } })
            {
                using var fixture = new Fixture(); var calls = 0;
                using var client = Client((_, _) => { calls++; return Task.FromResult(Body(Payload)); });
                await Fails(() => new InstallerDownload(client).DownloadAsync(installer, fixture.Root)); Check(calls == 0);
            }
        });
        yield return ("暂存清理不会删除指定目录外的文件", () =>
        {
            using var fixture = new Fixture(); var protectedFile = Path.Combine(fixture.Root, "keep.txt"); File.WriteAllText(protectedFile, "keep");
            InstallerDownload.Discard(new(protectedFile, Path.Combine(fixture.Root, Guid.NewGuid().ToString("N")), Installer.Sha256));
            Check(File.Exists(protectedFile)); return Task.CompletedTask;
        });
    }

    private static object Asset(string? name = null, object? size = null, string? digest = null, string state = "uploaded") =>
        new { name = name ?? Installer.FileName, size = size ?? Installer.Size, digest = digest ?? "sha256:" + Installer.Sha256, state, browser_download_url = "https://untrusted.test/ignored" };
    private static HttpResponseMessage Metadata(object asset) => new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(
        new { tag_name = "v0.2.2", draft = false, prerelease = false, assets = new[] { asset } })) };
    private static HttpResponseMessage Redirect(string url) => new(HttpStatusCode.Redirect) { Headers = { Location = new Uri(url) } };
    private static HttpResponseMessage Body(byte[] bytes, bool unknownLength = false) => new(HttpStatusCode.OK)
        { Content = unknownLength ? new UnknownLengthContent(bytes) : new ByteArrayContent(bytes) };
    private static HttpClient Client(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) => new(new Handler(send));
    private static async Task InvalidDownload(ReleaseInstaller installer, Func<HttpResponseMessage> response)
    {
        using var fixture = new Fixture(); using var client = Client((_, _) => Task.FromResult(response()));
        await Fails(() => new InstallerDownload(client).DownloadAsync(installer, fixture.Root)); Check(Empty(fixture.Root));
    }
    private static async Task<InstallerDownloadException> Fails(Func<Task<InstallerFile>> run)
    { try { await run(); } catch (InstallerDownloadException error) { return error; } throw new InvalidOperationException("Expected download rejection"); }
    private static bool Empty(string root) => !Directory.EnumerateFileSystemEntries(root).Any();
    private static void Check(bool condition) { if (!condition) throw new InvalidOperationException("Installer check failed"); }
    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => send(request, token); }
    private sealed class ProgressSink(List<DownloadProgress> values) : IProgress<DownloadProgress>
    { public void Report(DownloadProgress value) => values.Add(value); }
    private sealed class UnknownLengthContent(byte[] bytes) : HttpContent
    {
        protected override bool TryComputeLength(out long length) { length = 0; return false; }
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) => stream.WriteAsync(bytes).AsTask();
    }
    private sealed class Fixture : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "Suiyi.Installer.Tests." + Guid.NewGuid().ToString("N"));
        public Fixture() => Directory.CreateDirectory(Root);
        public void Dispose() => Directory.Delete(Root, true);
    }
}
