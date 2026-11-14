using Microsoft.Win32;
using TranslationFloat.App;

internal static class StartupChecks
{
    public static void Run()
    {
        var testKey = @"Software\TranslationFloat.Tests.Startup\" + Guid.NewGuid().ToString("N");
        var store = new RegistryStartupStore(testKey + @"\Run", testKey + @"\Preferences");
        var command = StartupRegistration.BuildCommand(@"C:\Program Files\随译\TranslationFloat.exe");
        try
        {
            var startup = new StartupRegistration(store, command);
            startup.Initialize();
            Check(startup.IsEnabled && store.ReadPreference() == true, "首次启动默认开启并保存用户偏好");
            using (var run = Registry.CurrentUser.OpenSubKey(testKey + @"\Run", true))
                run!.SetValue("OtherApplication", "untouched");
            startup.SetEnabled(false);
            using (var run = Registry.CurrentUser.OpenSubKey(testKey + @"\Run"))
                Check(!startup.IsEnabled && store.ReadPreference() == false && (string?)run!.GetValue("OtherApplication") == "untouched", "关闭只删除本程序启动项，不影响其他应用");
            startup = new StartupRegistration(store, command);
            startup.Initialize();
            Check(!startup.IsEnabled && store.ReadPreference() == false, "重新启动后保留关闭选择");
            startup.SetEnabled(true);
            Check(startup.IsEnabled && store.ReadPreference() == true, "重新勾选后恢复注册");
            var movedCommand = StartupRegistration.BuildCommand(@"D:\随译 新位置\TranslationFloat.exe");
            startup = new StartupRegistration(store, movedCommand);
            startup.Initialize();
            Check(startup.IsEnabled && store.ReadCommand() == movedCommand, "开启状态下移动便携版会更新注册路径");
            startup.SetEnabled(false);
            startup = new StartupRegistration(store, command);
            startup.Initialize();
            Check(!startup.IsEnabled && store.ReadCommand() is null, "关闭状态下移动程序不会重新开启");
            using (var preferences = Registry.CurrentUser.OpenSubKey(testKey + @"\Preferences", true))
                preferences!.SetValue(RegistryStartupStore.PreferenceValueName, "invalid");
            Expect<InvalidDataException>(startup.Initialize);
            Check(store.ReadCommand() is null, "损坏的偏好不会被当成首次运行而开启");
        }
        finally
        {
            // This GUID branch was created by this test and is outside the real Run key.
            Registry.CurrentUser.DeleteSubKeyTree(testKey, false);
        }

        Check(command == "\"C:\\Program Files\\随译\\TranslationFloat.exe\"" &&
            StartupRegistration.BuildCommand(@"C:\Program Files\dotnet\dotnet.exe", @"D:\随译\TranslationFloat.dll") == "\"C:\\Program Files\\dotnet\\dotnet.exe\" \"D:\\随译\\TranslationFloat.dll\"", "含空格和中文的 apphost 与 dotnet 路径正确加引号");
        Expect<ArgumentException>(() => StartupRegistration.BuildCommand("relative.exe"));
        Expect<InvalidOperationException>(() => StartupRegistration.BuildCommand(@"C:\" + new string('a', 260) + ".exe"));
        Check(true, "不完整或过长启动命令在写入前拒绝");

        var unchanged = new FakeStore { Command = command, Preference = true };
        new StartupRegistration(unchanged, command).Initialize();
        Check(unchanged.CommandWrites == 0 && unchanged.PreferenceWrites == 0, "每次登录不重复改写已经正确的启动项");
        var denied = new FakeStore { Preference = false, FailCommand = true };
        Expect<UnauthorizedAccessException>(() => new StartupRegistration(denied, command).SetEnabled(true));
        Check(denied.Command is null && denied.Preference == false && denied.PreferenceWrites == 0, "注册写入失败时保留之前的关闭偏好");
        var preferenceFailure = new FakeStore { Command = "old-command", Preference = true, FailPreference = true };
        Expect<IOException>(() => new StartupRegistration(preferenceFailure, command).SetEnabled(false));
        Check(preferenceFailure.Command == "old-command" && preferenceFailure.Preference == true, "偏好写入失败时恢复原有启动命令");
        Console.WriteLine("12 startup checks passed; only isolated test registry branches were used.");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        Console.WriteLine("PASS " + message);
    }

    private static void Expect<T>(Action action) where T : Exception
    {
        try { action(); }
        catch (T) { return; }
        throw new InvalidOperationException("Expected " + typeof(T).Name);
    }

    private sealed class FakeStore : IStartupStore
    {
        public string? Command;
        public bool? Preference;
        public bool FailCommand, FailPreference;
        public int CommandWrites, PreferenceWrites;
        public bool? ReadPreference() => Preference;
        public string? ReadCommand() => Command;
        public void WriteCommand(string? command)
        {
            CommandWrites++;
            if (FailCommand) throw new UnauthorizedAccessException();
            Command = command;
        }
        public void WritePreference(bool? enabled)
        {
            PreferenceWrites++;
            if (FailPreference) throw new IOException();
            Preference = enabled;
        }
    }
}
