using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TranslationFloat.Core;

namespace TranslationFloat.App;

internal sealed record TranslationConfiguration(string BaseUrl, string Model, string ApiKey)
{
    public bool IsConfigured => !string.IsNullOrWhiteSpace(BaseUrl) && !string.IsNullOrWhiteSpace(Model);
    public override string ToString() => $"TranslationConfiguration {{ Model = {Model}, IsConfigured = {IsConfigured} }}";
}

internal sealed class TranslationSettingsStore(string? folder = null, bool useEnvironment = true)
{
    // Used only to read existing v1 installations, never as a new-user preset.
    private const string LegacyBaseUrl = "https://api.deepseek.com/";
    private readonly string _folder = folder ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TranslationFloat");
    private sealed record Preferences(int Version, string Model, string? BaseUrl = null);

    public TranslationConfiguration Load()
    {
        try
        {
            var path = Path.Combine(_folder, "settings.json");
            if (!File.Exists(path)) return new("", "", "");
            var saved = JsonSerializer.Deserialize<Preferences>(File.ReadAllText(path));
            if (saved is null || saved.Version is not 1 and not 2) return new("", "", "");
            if (saved.Version == 2 && string.IsNullOrWhiteSpace(saved.BaseUrl)) return new("", "", "");
            var address = OpenAiEndpoint.Normalize(saved.Version == 1 ? LegacyBaseUrl : saved.BaseUrl ?? "");
            var model = saved.Model ?? "";
            if (model.Length > 0) OpenAiEndpoint.ValidateModel(model);
            return new(address, model, ResolveKey(address, ""));
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or ArgumentException)
        { return new("", "", ""); }
    }

    public string ResolveKey(string baseUrl, string replacementKey)
    {
        var address = OpenAiEndpoint.Normalize(baseUrl);
        if (!string.IsNullOrWhiteSpace(replacementKey))
        {
            var key = replacementKey.Trim();
            OpenAiEndpoint.ValidateKey(key);
            return key;
        }
        var path = CredentialPath(address);
        var legacy = false;
        if (!File.Exists(path) && address == LegacyBaseUrl)
        { path = Path.Combine(_folder, "deepseek.key"); legacy = true; }
        try
        {
            if (File.Exists(path))
            {
                var plain = ProtectedData.Unprotect(File.ReadAllBytes(path), null, DataProtectionScope.CurrentUser);
                try
                {
                    var key = Encoding.UTF8.GetString(plain);
                    OpenAiEndpoint.ValidateKey(key);
                    return key;
                }
                finally { CryptographicOperations.ZeroMemory(plain); }
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or CryptographicException or ArgumentException) { }
        // Never send a legacy environment key to a different API address.
        if (legacy && useEnvironment)
        {
            var key = Environment.GetEnvironmentVariable("DEEPSEEK_API_KEY")?.Trim() ?? "";
            try { OpenAiEndpoint.ValidateKey(key); return key; }
            catch (ArgumentException) { }
        }
        return "";
    }

    public TranslationConfiguration Save(string baseUrl, string model, string replacementKey)
    {
        if (string.IsNullOrWhiteSpace(baseUrl) && string.IsNullOrWhiteSpace(model) && string.IsNullOrWhiteSpace(replacementKey))
        {
            Directory.CreateDirectory(_folder);
            AtomicWrite(Path.Combine(_folder, "settings.json"), JsonSerializer.SerializeToUtf8Bytes(new Preferences(2, "", "")));
            return new("", "", "");
        }
        var address = OpenAiEndpoint.Normalize(baseUrl);
        model = model.Trim();
        if (model.Length > 0) OpenAiEndpoint.ValidateModel(model);
        var key = ResolveKey(address, replacementKey);
        Directory.CreateDirectory(_folder);
        if (key.Length > 0)
        {
            var plain = Encoding.UTF8.GetBytes(key);
            byte[] encrypted;
            try { encrypted = ProtectedData.Protect(plain, null, DataProtectionScope.CurrentUser); }
            finally { CryptographicOperations.ZeroMemory(plain); }
            AtomicWrite(CredentialPath(address), encrypted);
        }
        AtomicWrite(Path.Combine(_folder, "settings.json"), JsonSerializer.SerializeToUtf8Bytes(new Preferences(2, model, address)));
        return new(address, model, key);
    }

    internal string CredentialPath(string normalizedAddress) => Path.Combine(_folder,
        "api-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalizedAddress))).ToLowerInvariant() + ".key");

    private static void AtomicWrite(string path, byte[] data)
    {
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.WriteAllBytes(temporary, data); File.Move(temporary, path, true); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
