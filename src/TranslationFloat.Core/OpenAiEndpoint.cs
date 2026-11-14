namespace TranslationFloat.Core;

public static class OpenAiEndpoint
{
    public static string Normalize(string address)
    {
        if (address.Length > 2048 || !Uri.TryCreate(address.Trim(), UriKind.Absolute, out var uri) ||
            uri.Scheme is not "https" and not "http" || !string.IsNullOrEmpty(uri.UserInfo) ||
            !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
            throw new ArgumentException("请填写有效的 API 根地址，如 https://example.com/v1。");
        var path = uri.AbsolutePath.TrimEnd('/');
        if (path.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase)) path = path[..^17];
        return new UriBuilder(uri) { Path = path + "/" }.Uri.AbsoluteUri;
    }

    public static void ValidateKey(string key)
    {
        if (key.Length > 4096 || key.Any(c => c < 33 || c > 126))
            throw new ArgumentException("API Key 格式无效。");
    }

    public static void ValidateModel(string model)
    {
        if (string.IsNullOrWhiteSpace(model) || model.Length > 256 || model.Any(char.IsControl))
            throw new ArgumentException("请填写模型名称，或点击获取。");
    }
}
