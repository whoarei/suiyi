using Microsoft.Win32;

namespace TranslationFloat.App;

internal sealed class QuickModeSettingsStore(string keyPath = @"Software\Suiyi")
{
    private const string ValueName = "QuickModeEnabled";

    public bool Load()
    {
        using var key = Registry.CurrentUser.OpenSubKey(keyPath);
        return key?.GetValue(ValueName) is int value && value == 1;
    }

    public void Save(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(keyPath);
        key.SetValue(ValueName, enabled ? 1 : 0, RegistryValueKind.DWord);
    }
}
