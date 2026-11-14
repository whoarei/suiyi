using System.Net;
using System.Text;
using TranslationFloat.Core;

internal static class OpenAiCompatibilityChecks
{
    public static IEnumerable<(string Name, Func<Task> Run)> Cases()
    {
        yield return ("API 根路径保留版本和代理前缀，兼容完整聊天地址", () =>
        {
            Check(OpenAiEndpoint.Normalize("https://proxy.test/api/v9") == "https://proxy.test/api/v9/");
            Check(OpenAiEndpoint.Normalize("https://proxy.test/api/v9/chat/completions/") == "https://proxy.test/api/v9/");
            Check(OpenAiEndpoint.Normalize("http://127.0.0.1:1234/v1") == "http://127.0.0.1:1234/v1/");
            return Task.CompletedTask;
        });
        yield return ("非法 API 地址不生成请求，模型 ID 支持命名空间和冒号", () =>
        {
            foreach (var url in new[] { "", "not-a-url", "ftp://api.test/v1", "https://user:pass@api.test", "https://api.test/v1?key=secret", "https://api.test/#fragment" })
            {
                try { OpenAiEndpoint.Normalize(url); throw new InvalidOperationException("Expected invalid address"); }
                catch (ArgumentException) { }
            }
            OpenAiEndpoint.ValidateModel("org/model:latest");
            return Task.CompletedTask;
        });
        yield return ("任意接口和模型均可请求，不要求厂商预设且允许本地匿名接口", async () =>
        {
            using var client = Client((request, _) =>
            {
                Check(request.RequestUri == new Uri("http://localhost:1234/custom/v7/chat/completions"));
                Check(request.Headers.Authorization is null);
                return Task.FromResult(Json("{\"choices\":[{\"finish_reason\":\"stop\",\"message\":{\"content\":\"Hello\"}}]}"));
            });
            Check(await new OpenAiTranslationProvider(client, "http://localhost:1234/custom/v7", "", "org/model:latest").TranslateAsync(new("你好"), default) == "Hello");
        });
        yield return ("模型列表使用 GET models、当前 Key 并排序去重", async () =>
        {
            using var client = Client((request, _) =>
            {
                Check(request.Method == HttpMethod.Get && request.RequestUri == new Uri("https://api.test/custom/models"));
                Check(request.Headers.Authorization?.Parameter == "test-key");
                return Task.FromResult(Json("{\"data\":[{\"id\":\"z-model\"},{\"id\":\"org/a:latest\"},{\"id\":\"z-model\"}]}"));
            });
            var models = await new OpenAiModelCatalog(client).ListAsync("https://api.test/custom", "test-key", default);
            Check(models.SequenceEqual(new[] { "org/a:latest", "z-model" }));
        });
        yield return ("模型分页只请求原 API 地址并转义游标", async () =>
        {
            var calls = 0;
            using var client = Client((request, _) =>
            {
                Check(request.RequestUri!.Host == "api.test");
                if (++calls == 1) return Task.FromResult(Json("{\"data\":[{\"id\":\"a\"}],\"has_more\":true,\"last_id\":\"https://other.test/key\",\"next\":\"https://other.test/\"}"));
                Check(request.RequestUri.Query.Contains("after=https%3A%2F%2Fother.test%2Fkey"));
                return Task.FromResult(Json("{\"data\":[{\"id\":\"b\"}],\"has_more\":false}"));
            });
            Check((await new OpenAiModelCatalog(client).ListAsync("https://api.test/v1", "test-key", default)).Count == 2 && calls == 2);
        });
        yield return ("不支持获取模型的接口返回可手填提示，不包含服务端正文", async () =>
        {
            foreach (var code in new[] { 404, 405, 501 })
            {
                using var client = Client((_, _) => Task.FromResult(new HttpResponseMessage((HttpStatusCode)code) { Content = new StringContent("private key and source") }));
                try { await new OpenAiModelCatalog(client).ListAsync("https://api.test/v1", "test-key", default); throw new InvalidOperationException("Expected failure"); }
                catch (ModelCatalogException error) { Check(error.Message.Contains("手动填写") && !error.Message.Contains("private")); }
            }
        });
        yield return ("获取模型的认证失败不会被误报为不支持接口", async () =>
        {
            using var client = Client((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized)));
            try { await new OpenAiModelCatalog(client).ListAsync("https://api.test", "test-key", default); throw new InvalidOperationException("Expected failure"); }
            catch (TranslationException error) { Check(error.Failure == TranslationFailure.Authentication); }
        });
        yield return ("模型空列表有效，坏响应只提供静态提示", async () =>
        {
            using var empty = Client((_, _) => Task.FromResult(Json("{\"data\":[]}")));
            Check((await new OpenAiModelCatalog(empty).ListAsync("https://api.test", "", default)).Count == 0);
            foreach (var body in new[] { "private response", "{\"data\":{}}", "{\"error\":\"private response\"}" })
            {
                using var client = Client((_, _) => Task.FromResult(Json(body)));
                try { await new OpenAiModelCatalog(client).ListAsync("https://api.test", "", default); throw new InvalidOperationException("Expected failure"); }
                catch (ModelCatalogException error) { Check(!error.Message.Contains("private")); }
            }
        });
        yield return ("模型列表大小和循环分页有限制", async () =>
        {
            using var large = Client((_, _) => Task.FromResult(Json(new string('x', 2 * 1024 * 1024 + 1))));
            try { await new OpenAiModelCatalog(large).ListAsync("https://api.test", "", default); throw new InvalidOperationException("Expected limit"); }
            catch (ModelCatalogException error) { Check(error.Message.Contains("过大")); }
            var calls = 0;
            using var loop = Client((_, _) => { calls++; return Task.FromResult(Json("{\"data\":[{\"id\":\"a\"}],\"has_more\":true,\"last_id\":\"a\"}")); });
            try { await new OpenAiModelCatalog(loop).ListAsync("https://api.test", "", default); throw new InvalidOperationException("Expected limit"); }
            catch (ModelCatalogException) { Check(calls == 2); }
        });
        yield return ("获取模型可取消，超时分类清晰", async () =>
        {
            using var client = Client(async (_, token) => { await Task.Delay(Timeout.Infinite, token); return Json("{}"); });
            try { await new OpenAiModelCatalog(client, TimeSpan.FromMilliseconds(25)).ListAsync("https://api.test", "", default); throw new InvalidOperationException("Expected timeout"); }
            catch (TranslationException error) { Check(error.Failure == TranslationFailure.Timeout); }
            using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
            try { await new OpenAiModelCatalog(client).ListAsync("https://api.test", "", cancellation.Token); throw new InvalidOperationException("Expected cancellation"); }
            catch (OperationCanceledException) { }
        });
    }

    private static HttpClient Client(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> callback) => new(new StubHandler(callback));
    private static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    private static void Check(bool value) { if (!value) throw new InvalidOperationException("OpenAI compatibility assertion failed"); }
}
