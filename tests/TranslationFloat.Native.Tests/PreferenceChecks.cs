using Microsoft.Win32;
using TranslationFloat.App;
using TranslationFloat.Core;

internal static class PreferenceChecks
{
    public static void Run()
    {
        var branch = @"Software\TranslationFloat.Tests.Preferences\" + Guid.NewGuid().ToString("N");
        var quick = new QuickModeSettingsStore(branch);
        var hotKey = new HotKeySettingsStore(branch);
        try
        {
            Check(!quick.Load(), "快速模式首次默认关闭");
            quick.Save(true);
            Check(new QuickModeSettingsStore(branch).Load(), "快速模式开启状态跨实例保存");
            quick.Save(false);
            Check(!new QuickModeSettingsStore(branch).Load(), "快速模式关闭状态跨实例保存");
            using (var key = Registry.CurrentUser.OpenSubKey(branch, true)) key!.SetValue("QuickModeEnabled", "invalid");
            Check(!quick.Load(), "损坏的偏好不自动开启快速模式");
            Check(hotKey.Load() == HotKeyBinding.Default, "首次使用默认快捷键");
            var custom = new HotKeyBinding(6, 0x7B);
            hotKey.Save(custom);
            Check(new HotKeySettingsStore(branch).Load() == custom, "自定义快捷键完整持久化");
            try { hotKey.Save(new(0, 0x41)); throw new InvalidOperationException("Expected rejection"); }
            catch (ArgumentException) { }
            Check(hotKey.Load() == custom, "非法快捷键不覆盖已有设置");
            using (var key = Registry.CurrentUser.OpenSubKey(branch, true)) key!.SetValue("HotKey", "invalid");
            Check(hotKey.Load() == HotKeyBinding.Default, "损坏的快捷键回退默认值");
        }
        finally { Registry.CurrentUser.DeleteSubKeyTree(branch, false); }
        Console.WriteLine("8 UI preference checks passed; only isolated test registry branches were used.");
    }
    private static void Check(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); Console.WriteLine("PASS " + message); }
}
