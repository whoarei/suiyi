using System;
using System.IO;
using System.Security;
using Microsoft.Win32;

namespace TranslationFloat.App;

internal sealed class ThemeSettingsStore(string keyPath = @"Software\Suiyi")
{
    public ThemeColor Load()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(keyPath);
            if (key?.GetValue("ThemeColor") is string saved)
                foreach (var palette in ThemePalette.All)
                    if (palette.Id.ToString().Equals(saved, StringComparison.OrdinalIgnoreCase)) return palette.Id;
        }
        catch (Exception error) when (error is SecurityException or UnauthorizedAccessException or IOException) { }
        return ThemeColor.Orange;
    }

    public void Save(ThemeColor color)
    {
        _ = ThemePalette.Get(color);
        using var key = Registry.CurrentUser.CreateSubKey(keyPath);
        key.SetValue("ThemeColor", color.ToString(), RegistryValueKind.String);
    }
}
