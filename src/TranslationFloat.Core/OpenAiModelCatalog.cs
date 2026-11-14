using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace TranslationFloat.Core;

public sealed class ModelCatalogException(string message) : Exception(message);

public sealed class OpenAiModelCatalog(HttpClient client, TimeSpan? timeout = null)
{
    public async Task<IReadOnlyList<string>> ListAsync(string baseUrl, string apiKey, CancellationToken cancellationToken)
    {
        var endpoint = new Uri(new Uri(OpenAiEndpoint.Normalize(baseUrl)), "models");
        OpenAiEndpoint.ValidateKey(apiKey);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(timeout ?? TimeSpan.FromSeconds(15));
        var models = new HashSet<string>(StringComparer.Ordinal);
        var cursors = new HashSet<string>(StringComparer.Ordinal);
        string? after = null;
        try
        {
            for (var page = 0; page < 20; page++)
            {
                var address = after is null ? endpoint : new Uri(endpoint.AbsoluteUri + "?after=" + Uri.EscapeDataString(after));
                using var request = new HttpRequestMessage(HttpMethod.Get, address);
                if (!string.IsNullOrEmpty(apiKey)) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
                using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token).ConfigureAwait(false);
                if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.MethodNotAllowed or HttpStatusCode.NotImplemented)
                    throw new ModelCatalogException("接口不支持获取模型，可手动填写。");
                if (!response.IsSuccessStatusCode)
                    throw new TranslationException(response.StatusCode switch
                    {
                        HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => TranslationFailure.Authentication,
                        HttpStatusCode.TooManyRequests => TranslationFailure.RateLimit,
                        _ when (int)response.StatusCode >= 500 => TranslationFailure.Server,
                        _ => TranslationFailure.InvalidRequest
                    });
                const int limit = 2 * 1024 * 1024;
                if (response.Content.Headers.ContentLength > limit) throw new ModelCatalogException("模型列表过大，请手动填写。");
                await using var stream = await response.Content.ReadAsStreamAsync(deadline.Token).ConfigureAwait(false);
                using var body = new MemoryStream();
                var buffer = new byte[8192];
                int count;
                while ((count = await stream.ReadAsync(buffer, deadline.Token).ConfigureAwait(false)) > 0)
                {
                    if (body.Length + count > limit) throw new ModelCatalogException("模型列表过大，请手动填写。");
                    body.Write(buffer, 0, count);
                }
                using var json = JsonDocument.Parse(body.ToArray());
                var root = json.RootElement;
                foreach (var entry in root.GetProperty("data").EnumerateArray())
                {
                    var id = entry.GetProperty("id").GetString();
                    if (id is { Length: > 0 and <= 256 } && !string.IsNullOrWhiteSpace(id) && !id.Any(char.IsControl)) models.Add(id);
                    if (models.Count > 5000) throw new ModelCatalogException("模型列表过大，请手动填写。");
                }
                if (!root.TryGetProperty("has_more", out var more) || !more.GetBoolean())
                    return models.Order(StringComparer.OrdinalIgnoreCase).ToArray();
                after = root.GetProperty("last_id").GetString();
                if (string.IsNullOrEmpty(after) || after.Length > 256 || !cursors.Add(after)) break;
            }
            throw new ModelCatalogException("模型列表分页不完整，可手动填写。");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        { throw new TranslationException(TranslationFailure.Timeout); }
        catch (HttpRequestException) { throw new TranslationException(TranslationFailure.Network); }
        catch (IOException) { throw new TranslationException(TranslationFailure.Network); }
        catch (Exception error) when (error is JsonException or KeyNotFoundException or InvalidOperationException)
        { throw new ModelCatalogException("无法识别模型列表，可手动填写。"); }
    }
}
