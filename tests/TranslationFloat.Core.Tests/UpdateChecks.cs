using System.Net;
using System.Text;
using System.Text.Json;
using TranslationFloat.Core;

internal static class UpdateChecks
{
    public static IEnumerable<(string Name, Func<Task> Run)> Cases()
    {
        yield return ("更新版本按数字比较，0.1.10 高于 0.1.9", () =>
        { Check(Version("v0.1.10").CompareTo(Version("0.1.9")) > 0); return Task.CompletedTask; });
        yield return ("预发布版本遵循语义版本顺序", () =>
        {
            var ordered = new[] { "alpha", "alpha.1", "alpha.beta", "beta", "beta.2", "beta.11", "rc.1" }
                .Select(suffix => Version("1.0.0-" + suffix)).Append(Version("1.0.0")).ToArray();
            for (var i = 1; i < ordered.Length; i++) Check(ordered[i].CompareTo(ordered[i - 1]) > 0);
            return Task.CompletedTask;
        });
        yield return ("构建提交摘要不影响版本比较或显示", () =>
        { Check(Version("0.1.3+abc123").CompareTo(Version("v0.1.3")) == 0 && Version("0.1.3+abc123").ToString() == "0.1.3"); return Task.CompletedTask; });
        yield return ("拒绝无效或超长版本，包含尾部换行和数字前导零", () =>
        {
            foreach (var value in new[] { "", "v0.1", "0.1.3.0", "0.01.3", "1.0.0-01", "1.0.0\n", "1.0.0/evil", "999999999999.0.0", new string('1', 257) })
                Check(!ReleaseVersion.TryParse(value, out _));
            return Task.CompletedTask;
        });
        yield return ("匿名检查正式版并使用固定的官方发布链接", async () =>
        {
            using var client = Client((request, _) =>
            {
                Check(request.RequestUri == GitHubUpdateChecker.LatestReleaseApi && request.Method == HttpMethod.Get);
                Check(request.Headers.Authorization is null && !request.Headers.Contains("Cookie"));
                Check(request.Headers.UserAgent.ToString() == "Suiyi/0.1.3");
                Check(request.Headers.Accept.Single().MediaType == "application/vnd.github+json");
                Check(request.Headers.GetValues("X-GitHub-Api-Version").Single() == "2026-03-10");
                return Task.FromResult(Release("v0.1.4", htmlUrl: "https://untrusted.test/"));
            });
            var result = await new GitHubUpdateChecker(client).CheckAsync(Version("0.1.3"));
            Check(result.State == UpdateCheckState.Available && result.Latest!.ToString() == "0.1.4"
                && result.ReleasePage == new Uri("https://github.com/whoarei/suiyi/releases/tag/v0.1.4"));
        });
        yield return ("相同版本和不同构建摘要不误报更新", async () =>
        { Check((await CheckRelease("v0.1.3", "0.1.3+commit")).State == UpdateCheckState.Current); });
        yield return ("本地较新版本不提示降级", async () =>
        { Check((await CheckRelease("v0.1.3", "0.1.4-dev")).State == UpdateCheckState.Current); });
        yield return ("同版本正式版高于预发布版", async () =>
        { Check((await CheckRelease("v0.1.4", "0.1.4-rc.1")).State == UpdateCheckState.Available); });
        yield return ("不接受草稿、预发布和缺失状态的更新信息", async () =>
        {
            foreach (var json in new[] { "{\"tag_name\":\"v1.0.0\",\"draft\":true,\"prerelease\":false}",
                "{\"tag_name\":\"v1.0.0\",\"draft\":false,\"prerelease\":true}",
                "{\"tag_name\":\"v1.0.0-rc.1\",\"draft\":false,\"prerelease\":false}", "{\"tag_name\":\"v1.0.0\"}" })
            { Check((await CheckResponse(() => Json(json))).State == UpdateCheckState.Failed); }
        });
        yield return ("发布标签缺失、类型错误或不是版本号时不打开链接", async () =>
        {
            foreach (var json in new[] { "[]", "{\"tag_name\":5,\"draft\":false,\"prerelease\":false}",
                "{\"tag_name\":\"../../elsewhere\",\"draft\":false,\"prerelease\":false}" })
            {
                var result = await CheckResponse(() => Json(json));
                Check(result.State == UpdateCheckState.Failed && result.ReleasePage is null);
            }
        });
        yield return ("未发布正式版本时明确提示", async () =>
        { Check((await CheckResponse(() => new(HttpStatusCode.NotFound))).Error!.Contains("正式版本")); });
        yield return ("GitHub 限流不自动重试，也不显示响应正文", async () =>
        {
            foreach (var status in new[] { HttpStatusCode.Forbidden, HttpStatusCode.TooManyRequests })
            {
                var calls = 0;
                using var client = Client((_, _) => { calls++; return Task.FromResult(Json("private-server-detail", status)); });
                var result = await new GitHubUpdateChecker(client).CheckAsync(Version("0.1.3"));
                Check(calls == 1 && result.Error!.Contains("限制") && !result.Error.Contains("private-server-detail"));
            }
        });
        yield return ("服务端失败或重定向不当作可用更新", async () =>
        {
            foreach (var status in new[] { HttpStatusCode.ServiceUnavailable, HttpStatusCode.Redirect })
            { Check((await CheckResponse(() => Json("private-server-detail", status))).State == UpdateCheckState.Failed); }
        });
        yield return ("网络错误只显示简洁提示", async () =>
        {
            using var client = Client((_, _) => throw new HttpRequestException("private-network-detail"));
            var result = await new GitHubUpdateChecker(client).CheckAsync(Version("0.1.3"));
            Check(result.State == UpdateCheckState.Failed && result.Error!.Contains("网络") && !result.Error.Contains("private-network-detail"));
        });
        yield return ("损坏 JSON 不产生下载入口", async () =>
        { Check((await CheckResponse(() => Json("not-json"))).ReleasePage is null); });
        yield return ("拒绝声明体积过大的响应", async () =>
        {
            Check((await CheckResponse(() =>
            {
                var response = Release("v1.0.0");
                response.Content.Headers.ContentLength = 1024 * 1024 + 1;
                return response;
            })).State == UpdateCheckState.Failed);
        });
        yield return ("未声明长度的响应也受体积上限限制", async () =>
        {
            Check((await CheckResponse(() => new HttpResponseMessage(HttpStatusCode.OK)
                { Content = new UnknownLengthContent(new byte[1024 * 1024 + 1]) })).State == UpdateCheckState.Failed);
        });
        yield return ("关闭时取消网络请求并传播取消，不返回失败提示", async () =>
        {
            using var cancel = new CancellationTokenSource();
            var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            using var client = Client(async (_, token) =>
            { started.SetResult(); await Task.Delay(Timeout.Infinite, token); return Release("v1.0.0"); });
            var pending = new GitHubUpdateChecker(client).CheckAsync(Version("0.1.3"), cancel.Token);
            await started.Task; cancel.Cancel();
            try { await pending; throw new InvalidOperationException("Cancellation was swallowed"); }
            catch (OperationCanceledException) { }
        });
        yield return ("请求超时有可重试提示", async () =>
        {
            using var client = Client((_, _) => throw new TaskCanceledException());
            Check((await new GitHubUpdateChecker(client).CheckAsync(Version("0.1.3"))).Error!.Contains("超时"));
        });
        yield return ("响应流断开时保留可重试错误", async () =>
        {
            using var client = Client((_, _) => throw new IOException("private-stream-detail"));
            var result = await new GitHubUpdateChecker(client).CheckAsync(Version("0.1.3"));
            Check(result.State == UpdateCheckState.Failed && !result.Error!.Contains("private-stream-detail"));
        });
    }

    private static ReleaseVersion Version(string value)
    { if (!ReleaseVersion.TryParse(value, out var version)) throw new InvalidOperationException("Bad test version"); return version; }
    private static HttpResponseMessage Release(string tag, string htmlUrl = "https://github.com/whoarei/suiyi/releases") =>
        Json(JsonSerializer.Serialize(new { tag_name = tag, draft = false, prerelease = false, html_url = htmlUrl }));
    private static HttpResponseMessage Json(string json, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
    private static async Task<UpdateCheckResult> CheckRelease(string tag, string current) =>
        await CheckResponse(() => Release(tag), current);
    private static async Task<UpdateCheckResult> CheckResponse(Func<HttpResponseMessage> response, string current = "0.1.3")
    {
        using var client = Client((_, _) => Task.FromResult(response()));
        return await new GitHubUpdateChecker(client).CheckAsync(Version(current));
    }
    private static HttpClient Client(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) => new(new Handler(send));
    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => send(request, token);
    }
    private sealed class UnknownLengthContent(byte[] bytes) : HttpContent
    {
        protected override bool TryComputeLength(out long length) { length = 0; return false; }
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) => stream.WriteAsync(bytes).AsTask();
    }
    private static void Check(bool condition) { if (!condition) throw new InvalidOperationException("Update check assertion failed"); }
}
