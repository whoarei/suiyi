using System.Security.Cryptography;
using System.Text;
using TranslationFloat.App;

internal static class SettingsChecks
{
    public static void Run()
    {
        var folder = Path.Combine(Path.GetTempPath(), "TranslationFloat.Settings.Tests." + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var checks = 0;
        void Check(bool condition, string message)
        { if (!condition) throw new InvalidOperationException(message); checks++; Console.WriteLine("PASS " + message); }
        try
        {
            const string address = "https://api.test/v1/", otherAddress = "https://other.test/v1/";
            const string dummyKey = "local-test-value-never-used-as-a-real-key";
            var store = new TranslationSettingsStore(folder, useEnvironment: false);
            Check(store.Load() is { BaseUrl: "", Model: "", ApiKey: "" }, "首次使用不预设厂商、模型或密钥");
            store.Save(address, "org/model:latest", dummyKey);
            Check(store.Load() is { BaseUrl: address, Model: "org/model:latest", ApiKey: dummyKey }, "自定义地址、模型和 DPAPI 密钥可恢复");
            Check(!store.Load().ToString().Contains(dummyKey), "配置对象的字符串表示不泄露密钥");
            Check(!Encoding.UTF8.GetString(File.ReadAllBytes(store.CredentialPath(address))).Contains(dummyKey), "密钥文件不保存明文");
            Check(!File.ReadAllText(Path.Combine(folder, "settings.json")).Contains(dummyKey), "JSON 设置不包含密钥");
            store.Save(address.TrimEnd('/'), "new-model-id", "");
            Check(store.Load() is { Model: "new-model-id", ApiKey: dummyKey }, "同地址模型变化无需更新预设，留空保留密钥");
            store.Save(otherAddress, "remote-model", "");
            Check(store.Load().ApiKey == "" && store.ResolveKey(address, "") == dummyKey, "切换地址不会沿用原地址密钥，原密钥仍保留");
            store.Save(otherAddress, "remote-model", "other-test-key");
            Check(new TranslationSettingsStore(folder, false).Load().ApiKey == "other-test-key" && store.ResolveKey(address, "") == dummyKey, "多个地址的加密密钥彼此独立");
            File.WriteAllBytes(store.CredentialPath(otherAddress), [1, 2, 3]);
            Check(store.Load().ApiKey == "", "损坏密钥不会回退到其他地址的凭据");
            store.Save(address + "chat/completions", "namespace/model:v2", "");
            Check(store.Load() is { BaseUrl: address, Model: "namespace/model:v2", ApiKey: dummyKey }, "完整聊天地址归一化后仍使用同一地址密钥");
            Check(store.ResolveKey("https://api.test/v2", "") == "", "同主机不同 API 路径不会隐式共用密钥");
            try { store.Save(address, "invalid\nmodel", ""); throw new InvalidOperationException("Expected rejection"); }
            catch (ArgumentException) { }
            Check(store.Load().Model == "namespace/model:v2", "非法配置不会覆盖已有设置");
            File.WriteAllText(Path.Combine(folder, "settings.json"), "not-json");
            Check(!store.Load().IsConfigured && store.Load().ApiKey == "", "损坏配置不猜测 API 地址或发送密钥");
            store.Save("", "", "");
            Check(!store.Load().IsConfigured && store.ResolveKey(address, "") == dummyKey, "未配置翻译服务也可保存其他偏好，不删除已有密钥");
            store.Save("http://127.0.0.1:1234/v1", "local-model", "");
            Check(store.Load().IsConfigured && store.Load().ApiKey == "", "本地无鉴权接口允许空 Key");
            var legacyFolder = Path.Combine(folder, "legacy"); Directory.CreateDirectory(legacyFolder);
            var plain = Encoding.UTF8.GetBytes(dummyKey);
            try { File.WriteAllBytes(Path.Combine(legacyFolder, "deepseek.key"), ProtectedData.Protect(plain, null, DataProtectionScope.CurrentUser)); }
            finally { CryptographicOperations.ZeroMemory(plain); }
            File.WriteAllText(Path.Combine(legacyFolder, "settings.json"), "{\"Version\":1,\"Model\":\"existing-model-name\"}");
            var legacy = new TranslationSettingsStore(legacyFolder, false);
            var loaded = legacy.Load();
            Check(loaded is { BaseUrl: "https://api.deepseek.com/", Model: "existing-model-name", ApiKey: dummyKey }, "旧版配置原样加载，不依赖旧模型白名单");
            legacy.Save(loaded.BaseUrl, loaded.Model, "");
            File.Delete(Path.Combine(legacyFolder, "deepseek.key"));
            Check(legacy.Load() == loaded && File.ReadAllText(Path.Combine(legacyFolder, "settings.json")).Contains("\"Version\":2"), "保存后迁移到通用格式及按地址加密的密钥文件");
        }
        finally
        {
            foreach (var directory in Directory.GetDirectories(folder).Append(folder))
            { foreach (var file in Directory.GetFiles(directory)) File.Delete(file); if (directory != folder) Directory.Delete(directory); }
            Directory.Delete(folder);
        }
        Console.WriteLine($"{checks} settings checks passed; no production credentials read.");
    }
}
