using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace TranslationFloat.Core;

public enum UpdateCheckState { Available, Current, Failed }

public sealed record UpdateCheckResult(UpdateCheckState State, ReleaseVersion? Latest = null, Uri? ReleasePage = null, string? Error = null);

// Uses a separate, anonymous client. Translation credentials never enter this request.
public sealed class GitHubUpdateChecker(HttpClient client)
{
    public static readonly Uri LatestReleaseApi = new("https://api.github.com/repos/whoarei/suiyi/releases/latest");
    private const int MaxResponseBytes = 1024 * 1024;

    public async Task<UpdateCheckResult> CheckAsync(ReleaseVersion current, CancellationToken cancellationToken = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, LatestReleaseApi);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
            request.Headers.UserAgent.ParseAdd("Suiyi/" + current.Number);
            request.Headers.Add("X-GitHub-Api-Version", "2026-03-10");
            request.Headers.CacheControl = new CacheControlHeaderValue { NoCache = true };
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.NotFound) return Failed("暂时没有可用的正式版本。");
            if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests)
                return Failed("GitHub 暂时限制了检查请求，请稍后重试。");
            if (!response.IsSuccessStatusCode) return Failed("暂时无法检查更新，请稍后重试。");
            if (response.Content.Headers.ContentLength > MaxResponseBytes) return InvalidResponse();

            await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
            using var buffer = new MemoryStream();
            var chunk = new byte[8192];
            int count;
            while ((count = await stream.ReadAsync(chunk, timeout.Token).ConfigureAwait(false)) > 0)
            {
                if (buffer.Length + count > MaxResponseBytes) return InvalidResponse();
                buffer.Write(chunk, 0, count);
            }
            using var document = JsonDocument.Parse(buffer.ToArray());
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("draft", out var draft) || draft.ValueKind != JsonValueKind.False
                || !root.TryGetProperty("prerelease", out var prerelease) || prerelease.ValueKind != JsonValueKind.False
                || !root.TryGetProperty("tag_name", out var tag) || tag.ValueKind != JsonValueKind.String
                || !ReleaseVersion.TryParse(tag.GetString(), out var latest) || latest.Prerelease is not null) return InvalidResponse();

            // Build the official repository URL instead of trusting a URL supplied in JSON.
            var page = new Uri("https://github.com/whoarei/suiyi/releases/tag/" + Uri.EscapeDataString(tag.GetString()!));
            return new UpdateCheckResult(latest.CompareTo(current) > 0 ? UpdateCheckState.Available : UpdateCheckState.Current, latest, page);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        { return Failed("检查更新超时，请检查网络后重试。"); }
        catch (JsonException) { return InvalidResponse(); }
        catch (HttpRequestException) { return Failed("无法连接 GitHub，请检查网络后重试。"); }
        catch (IOException) { return Failed("无法读取更新信息，请稍后重试。"); }
    }

    private static UpdateCheckResult Failed(string message) => new(UpdateCheckState.Failed, Error: message);
    private static UpdateCheckResult InvalidResponse() => Failed("更新信息暂时不可用，请稍后重试。");
}
