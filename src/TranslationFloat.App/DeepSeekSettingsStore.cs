using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TranslationFloat.Core;

namespace TranslationFloat.App;

internal sealed record DeepSeekConfiguration(string Model, string ApiKey)
{
    public bool IsConfigured => !string.IsNullOrWhiteSpace(ApiKey);
    public override string ToString() => $"DeepSeekConfiguration {{ Model = {Model}, IsConfigured = {IsConfigured} }}";
}

internal sealed class DeepSeekSettingsStore(string? folder = null, bool useEnvironment = true)
{
    private readonly string _folder = folder ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TranslationFloat");
    private sealed record Preferences(int Version, string Model);

    public DeepSeekConfiguration Load()
    {
        var model = DeepSeekTranslationProvider.DefaultModel;
        var key = "";
        try
        {
            var path = Path.Combine(_folder, "settings.json");
            if (File.Exists(path))
            {
                var saved = JsonSerializer.Deserialize<Preferences>(File.ReadAllText(path));
                if (saved?.Version == 1 && IsSupportedModel(saved.Model)) model = saved.Model;
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException) { }
        try
        {
            var path = Path.Combine(_folder, "deepseek.key");
            if (File.Exists(path))
            {
                var plain = ProtectedData.Unprotect(File.ReadAllBytes(path), null, DataProtectionScope.CurrentUser);
                try { key = Encoding.UTF8.GetString(plain); }
                finally { CryptographicOperations.ZeroMemory(plain); }
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or CryptographicException) { }
        if (string.IsNullOrWhiteSpace(key) && useEnvironment)
            key = Environment.GetEnvironmentVariable("DEEPSEEK_API_KEY")?.Trim() ?? "";
        return new(model, key);
    }

    public DeepSeekConfiguration Save(string model, string replacementKey)
    {
        if (!IsSupportedModel(model)) throw new ArgumentException("请选择支持的 DeepSeek 模型。");
        var key = string.IsNullOrWhiteSpace(replacementKey) ? Load().ApiKey : replacementKey.Trim();
        if (key.Length is < 10 or > 512 || key.Any(c => c < 33 || c > 126))
            throw new ArgumentException("请填写有效的 DeepSeek API Key。");
        var plain = Encoding.UTF8.GetBytes(key);
        byte[] encrypted;
        try { encrypted = ProtectedData.Protect(plain, null, DataProtectionScope.CurrentUser); }
        finally { CryptographicOperations.ZeroMemory(plain); }
        Directory.CreateDirectory(_folder);
        AtomicWrite("deepseek.key", encrypted);
        AtomicWrite("settings.json", JsonSerializer.SerializeToUtf8Bytes(new Preferences(1, model)));
        return new(model, key);
    }

    public static bool IsSupportedModel(string model) => model is "deepseek-flash" or "deepseek-v4-pro";

    private void AtomicWrite(string name, byte[] data)
    {
        var path = Path.Combine(_folder, name);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        File.WriteAllBytes(temporary, data);
        File.Move(temporary, path, true);
    }
}
