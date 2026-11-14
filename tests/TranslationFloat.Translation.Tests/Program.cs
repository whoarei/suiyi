using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using TranslationFloat.Core;

const string fakeKey = "test-only-not-a-real-api-key";
var checks = new List<(string Name, Func<Task> Run)>();
void Add(string name, Func<Task> run) => checks.Add((name, run));

Add("请求遵循 DeepSeek 协议并关闭思考，不发送聊天历史或工具", async () =>
{
    using var client = Client(async (request, _) =>
    {
        Check(request.RequestUri == DeepSeekTranslationProvider.Endpoint && request.Method == HttpMethod.Post);
        Check(request.Headers.Authorization?.Scheme == "Bearer" && request.Headers.Authorization.Parameter == fakeKey);
        using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
        var root = body.RootElement;
        Check(root.GetProperty("model").GetString() == "deepseek-flash");
        Check(root.GetProperty("thinking").GetProperty("type").GetString() == "disabled");
        Check(root.GetProperty("response_format").GetProperty("type").GetString() == "json_object");
        Check(!root.GetProperty("stream").GetBoolean() && !root.EnumerateObject().Any(property => property.Name == "tools"));
        var messages = root.GetProperty("messages"); Check(messages.GetArrayLength() == 2);
        Check(messages[0].GetProperty("role").GetString() == "system");
        Check(messages[1].GetProperty("content").GetString() == "你好\n明天见 ✅");
        return Response(JsonSerializer.Serialize(new { lines = new[] { "Hello", "See you tomorrow ✅" } }));
    });
    var output = await new DeepSeekTranslationProvider(client, fakeKey).TranslateAsync(new("你好\n明天见 ✅"), default);
    Check(output == "Hello\nSee you tomorrow ✅");
});

Add("只展示最终 content，忽略 reasoning_content", async () =>
{
    using var client = Client((_, _) => Task.FromResult(Json("{\"choices\":[{\"finish_reason\":\"stop\",\"message\":{\"content\":\"Hello\",\"reasoning_content\":\"hidden reasoning\"}}]}")));
    Check(await new DeepSeekTranslationProvider(client, fakeKey).TranslateAsync(new("你好"), default) == "Hello");
});

Add("多行译文保留空行、末尾空行与 CRLF", async () =>
{
    using var client = Client((_, _) => Task.FromResult(Response("{\"lines\":[\"Hello\",\"\",\"See you\",\"\"]}")));
    var result = await new DeepSeekTranslationProvider(client, fakeKey).TranslateAsync(new("你好\r\n\r\n再见\r\n"), default);
    Check(result == "Hello\r\n\r\nSee you\r\n");
});

Add("模型合并行或额外添加段落时拒绝不完整译文", async () =>
{
    using var client = Client((_, _) => Task.FromResult(Response("{\"lines\":[\"Merged lines\"]}")));
    var error = await Failure(() => new DeepSeekTranslationProvider(client, fakeKey).TranslateAsync(new("第一行\n第二行"), default));
    Check(error.Failure == TranslationFailure.InvalidResponse);
});

Add("空白原文行不能被模型填入额外内容", async () =>
{
    using var client = Client((_, _) => Task.FromResult(Response("{\"lines\":[\"Hello\",\"unexpected extra\"]}")));
    var error = await Failure(() => new DeepSeekTranslationProvider(client, fakeKey).TranslateAsync(new("你好\n"), default));
    Check(error.Failure == TranslationFailure.InvalidResponse);
});

foreach (var entry in new[] { (401, TranslationFailure.Authentication), (403, TranslationFailure.Authentication), (402, TranslationFailure.Balance), (500, TranslationFailure.Server), (400, TranslationFailure.InvalidRequest) })
{
    var (code, expected) = entry;
    Add($"HTTP {code} 错误归类，不泄露服务端正文", async () =>
    {
        using var client = Client((_, _) => Task.FromResult(new HttpResponseMessage((HttpStatusCode)code) { Content = new StringContent(fakeKey + " private-source") }));
        var failure = await Failure(() => new DeepSeekTranslationProvider(client, fakeKey).TranslateAsync(new("你好"), default));
        Check(failure.Failure == expected && !failure.Message.Contains(fakeKey) && !failure.Message.Contains("private-source"));
    });
}

Add("限流保留 Retry-After", async () =>
{
    using var client = Client((_, _) =>
    {
        var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
        response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(7));
        return Task.FromResult(response);
    });
    var error = await Failure(() => new DeepSeekTranslationProvider(client, fakeKey).TranslateAsync(new("你好"), default));
    Check(error.Failure == TranslationFailure.RateLimit && error.RetryAfter == TimeSpan.FromSeconds(7));
});

Add("输出截断时不接受半句译文", async () =>
{
    using var client = Client((_, _) => Task.FromResult(Response("Partial", "length")));
    Check((await Failure(() => new DeepSeekTranslationProvider(client, fakeKey).TranslateAsync(new("你好"), default))).Failure == TranslationFailure.Incomplete);
});

foreach (var body in new[] { "{}", "not-json", "{\"choices\":[]}", "{\"choices\":[{\"finish_reason\":\"stop\",\"message\":{\"content\":null}}]}" })
{
    Add("拒绝空译文或无效响应结构", async () =>
    {
        using var client = Client((_, _) => Task.FromResult(Json(body)));
        Check((await Failure(() => new DeepSeekTranslationProvider(client, fakeKey).TranslateAsync(new("你好"), default))).Failure == TranslationFailure.InvalidResponse);
    });
}

Add("响应大小有限制", async () =>
{
    using var client = Client((_, _) => Task.FromResult(Json(new string('x', 65537))));
    Check((await Failure(() => new DeepSeekTranslationProvider(client, fakeKey).TranslateAsync(new("你好"), default))).Failure == TranslationFailure.InvalidResponse);
});

Add("请求超时映射为可读错误", async () =>
{
    using var client = Client(async (_, token) => { await Task.Delay(Timeout.Infinite, token); return Response("unreachable"); });
    Check((await Failure(() => new DeepSeekTranslationProvider(client, fakeKey, timeout: TimeSpan.FromMilliseconds(30)).TranslateAsync(new("你好"), default))).Failure == TranslationFailure.Timeout);
});

Add("主动取消不误报网络超时", async () =>
{
    using var client = Client(async (_, token) => { await Task.Delay(Timeout.Infinite, token); return Response("unreachable"); });
    using var cancel = new CancellationTokenSource(30);
    try { await new DeepSeekTranslationProvider(client, fakeKey).TranslateAsync(new("你好"), cancel.Token); throw new InvalidOperationException("Expected cancellation"); }
    catch (OperationCanceledException) { Check(cancel.IsCancellationRequested); }
});

Add("网络异常不带入原始异常消息", async () =>
{
    using var client = Client((_, _) => throw new HttpRequestException(fakeKey));
    var error = await Failure(() => new DeepSeekTranslationProvider(client, fakeKey).TranslateAsync(new("你好"), default));
    Check(error.Failure == TranslationFailure.Network && !error.Message.Contains(fakeKey));
});

Add("过长原文不发出 HTTP 请求", async () =>
{
    var called = false;
    using var client = Client((_, _) => { called = true; return Task.FromResult(Response("bad")); });
    await Failure(() => new DeepSeekTranslationProvider(client, fakeKey).TranslateAsync(new(new string('中', 2001)), default));
    Check(!called);
});

Add("未配置密钥不启用输出", () =>
{
    using var controller = Controller(); controller.Resume(); controller.UpdateSource("你好");
    Check(controller.State.Phase == TranslationPhase.NotConfigured && !controller.State.CanOutput);
    return Task.CompletedTask;
});

Add("快速输入只翻译最后一版", async () =>
{
    var seen = new ConcurrentQueue<string>();
    using var c = new TranslationController(TimeSpan.FromMilliseconds(40), TimeSpan.Zero);
    c.Configure(new CallbackProvider((r, _) => { seen.Enqueue(r.Text); return Task.FromResult("final"); }));
    c.Resume(); c.UpdateSource("我"); c.UpdateSource("我们"); c.UpdateSource("我们明天见");
    await Until(() => c.State.CanOutput); Check(seen.SequenceEqual(new[] { "我们明天见" }));
});

Add("迟到的旧结果不能覆盖新输入", async () =>
{
    var p = new BlockingProvider(); using var c = Controller(); c.Configure(p); c.Resume();
    c.UpdateSource("旧原文"); await Until(() => p.Count == 1);
    c.UpdateSource("新原文"); await Until(() => p.Count == 2);
    p.Complete(1, "new translation"); await Until(() => c.State.CanOutput);
    p.Complete(0, "stale translation"); await Task.Delay(25);
    Check(c.State.Source == "新原文" && c.State.Result == "new translation");
});

Add("隐藏窗口使在途结果失效，重新唤出可继续", async () =>
{
    var p = new BlockingProvider(); using var c = Controller(); c.Configure(p); c.Resume();
    c.UpdateSource("你好"); await Until(() => p.Count == 1); c.Suspend();
    p.Complete(0, "stale"); await Task.Delay(20); Check(!c.State.CanOutput);
    c.Resume(); await Until(() => p.Count == 2); p.Complete(1, "Hello"); await Until(() => c.State.CanOutput);
    Check(c.State.Result == "Hello");
});

Add("手动修改译文在收起后保留，不重复调用", async () =>
{
    var count = 0; using var c = Controller();
    c.Configure(new CallbackProvider((_, _) => { count++; return Task.FromResult("Hello"); })); c.Resume(); c.UpdateSource("你好");
    await Until(() => c.State.CanOutput); c.EditResult("Hi there"); c.Suspend(); c.Resume();
    Check(c.State.Result == "Hi there" && c.State.Phase == TranslationPhase.EditedReady && count == 1);
    c.EditResult(""); Check(!c.State.CanOutput);
});

Add("相同原文复用内存缓存", async () =>
{
    var count = 0; using var c = Controller();
    c.Configure(new CallbackProvider((r, _) => { count++; return Task.FromResult(r.Text + " translated"); })); c.Resume();
    c.UpdateSource("A"); await Until(() => c.State.CanOutput);
    c.UpdateSource("B"); await Until(() => c.State.CanOutput);
    c.UpdateSource("A"); Check(c.State.CanOutput && c.State.Result == "A translated" && count == 2);
});

Add("中文组合输入期间不调用服务", async () =>
{
    var seen = new ConcurrentQueue<string>(); using var c = Controller();
    c.Configure(new CallbackProvider((r, _) => { seen.Enqueue(r.Text); return Task.FromResult("Hello"); })); c.Resume(); c.SetComposing(true);
    c.UpdateSource("ni"); c.UpdateSource("nihao"); await Task.Delay(30); Check(seen.IsEmpty && !c.State.CanOutput);
    c.UpdateSource("你好"); c.SetComposing(false); await Until(() => c.State.CanOutput);
    Check(seen.SequenceEqual(new[] { "你好" }));
});

Add("新一轮翻译失败时不能输出旧译文", async () =>
{
    using var c = Controller(); c.Configure(new CallbackProvider((r, _) => r.Text == "A" ? Task.FromResult("old") : throw new TranslationException(TranslationFailure.Network))); c.Resume();
    c.UpdateSource("A"); await Until(() => c.State.CanOutput); c.UpdateSource("B");
    await Until(() => c.State.Phase == TranslationPhase.Error); Check(!c.State.CanOutput && c.State.Result == "old");
    c.EditResult("accidental stale edit"); Check(!c.State.CanOutput);
});

Add("更换服务配置后旧结果和缓存失效", async () =>
{
    var old = new BlockingProvider(); using var c = Controller(); c.Configure(old); c.Resume();
    c.UpdateSource("你好"); await Until(() => old.Count == 1);
    c.Configure(new CallbackProvider((_, _) => Task.FromResult("new model"))); await Until(() => c.State.CanOutput);
    old.Complete(0, "old model"); await Task.Delay(20); Check(c.State.Result == "new model");
});

Add("清空原文后旧请求不能恢复译文", async () =>
{
    var p = new BlockingProvider(); using var c = Controller(); c.Configure(p); c.Resume(); c.UpdateSource("你好");
    await Until(() => p.Count == 1); c.UpdateSource(""); p.Complete(0, "stale"); await Task.Delay(20);
    Check(c.State.Phase == TranslationPhase.Empty && c.State.Result == "" && !c.State.CanOutput);
});

Add("重试遵守限流冷却时间", async () =>
{
    var calls = 0; using var c = Controller();
    c.Configure(new CallbackProvider((_, _) => ++calls == 1 ? throw new TranslationException(TranslationFailure.RateLimit, TimeSpan.FromMilliseconds(100)) : Task.FromResult("OK")));
    c.Resume(); c.UpdateSource("你好"); await Until(() => c.State.Phase == TranslationPhase.Error);
    c.Retry(); await Task.Delay(25); Check(calls == 1 && !c.State.CanOutput);
    await Until(() => c.State.CanOutput); Check(calls == 2);
});

Add("未知异常不会泄露原文或密钥", async () =>
{
    using var c = Controller(); c.Configure(new CallbackProvider((_, _) => throw new InvalidOperationException(fakeKey))); c.Resume(); c.UpdateSource("你好");
    await Until(() => c.State.Phase == TranslationPhase.Error); Check(!c.State.Message.Contains(fakeKey));
});

foreach (var check in checks) { await check.Run(); Console.WriteLine("PASS " + check.Name); }
Console.WriteLine($"{checks.Count} translation checks passed; no external requests sent.");

static void Check(bool condition) { if (!condition) throw new InvalidOperationException("Assertion failed"); }
static TranslationController Controller() => new(TimeSpan.FromMilliseconds(5), TimeSpan.Zero);
static HttpClient Client(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> callback) => new(new StubHandler(callback)) { Timeout = Timeout.InfiniteTimeSpan };
static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
static HttpResponseMessage Response(string text, string finish = "stop") => Json(JsonSerializer.Serialize(new { choices = new[] { new { finish_reason = finish, message = new { content = text } } } }));
static async Task<TranslationException> Failure(Func<Task<string>> action)
{ try { await action(); throw new InvalidOperationException("Expected failure"); } catch (TranslationException error) { return error; } }
static async Task Until(Func<bool> condition)
{
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
    while (!condition()) await Task.Delay(5, timeout.Token);
}

sealed class StubHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> callback) : HttpMessageHandler
{ protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => callback(request, token); }
sealed class CallbackProvider(Func<TranslationRequest, CancellationToken, Task<string>> callback) : ITranslationProvider
{ public Task<string> TranslateAsync(TranslationRequest request, CancellationToken token) => callback(request, token); }
sealed class BlockingProvider : ITranslationProvider
{
    private readonly List<TaskCompletionSource<string>> _requests = [];
    public int Count { get { lock (_requests) return _requests.Count; } }
    public Task<string> TranslateAsync(TranslationRequest request, CancellationToken token)
    {
        var completion = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_requests) _requests.Add(completion);
        return completion.Task; // Deliberately ignore cancellation to exercise late responses.
    }
    public void Complete(int index, string value) { lock (_requests) _requests[index].SetResult(value); }
}
