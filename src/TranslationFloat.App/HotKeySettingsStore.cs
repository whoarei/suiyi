using System;
using System.Globalization;
using Microsoft.Win32;
using TranslationFloat.Core;

namespace TranslationFloat.App;

internal sealed class HotKeySettingsStore(string keyPath = @"Software\Suiyi")
{
    private const string ValueName = "HotKey";
    public HotKeyBinding Load()
    {
        using var key = Registry.CurrentUser.OpenSubKey(keyPath);
        if (key?.GetValue(ValueName) is string saved)
        {
            var parts = saved.Split(':');
            if (parts.Length == 2 && uint.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var modifiers) &&
                uint.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var virtualKey))
            {
                var binding = new HotKeyBinding(modifiers, virtualKey);
                if (binding.IsValid) return binding;
            }
        }
        return HotKeyBinding.Default;
    }

    public void Save(HotKeyBinding binding)
    {
        if (!binding.IsValid) throw new ArgumentException("请使用 Ctrl 或 Alt，搭配字母、数字或 F1–F12。");
        using var key = Registry.CurrentUser.CreateSubKey(keyPath);
        key.SetValue(ValueName, binding.Modifiers.ToString(CultureInfo.InvariantCulture) + ":" +
            binding.VirtualKey.ToString(CultureInfo.InvariantCulture), RegistryValueKind.String);
    }
}
