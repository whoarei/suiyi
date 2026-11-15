extern alias AppRuntime;

using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using TranslationFloat.App;
using TranslationFloat.Core;

internal static class KeyVisibilityChecks
{
    public static void Run()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            var folder = Path.Combine(Path.GetTempPath(), "TranslationFloat.KeyVisibility.Tests." + Guid.NewGuid().ToString("N"));
            Window? window = null;
            try
            {
                const string first = "https://first.test/v1/", second = "https://second.test/v1/";
                const string firstKey = "first-test-key-not-a-credential", secondKey = "second-test-key-not-a-credential";
                var store = new TranslationSettingsStore(folder, false);
                store.Save(first, "test-model", firstKey);
                store.Save(second, "test-model", secondKey);
                var assembly = typeof(AppRuntime::TranslationFloat.App.SettingsWindow).Assembly;
                var storeType = assembly.GetType("TranslationFloat.App.TranslationSettingsStore")!;
                var configurationType = assembly.GetType("TranslationFloat.App.TranslationConfiguration")!;
                var dialogType = typeof(AppRuntime::TranslationFloat.App.SettingsWindow);
                var dialogStore = Activator.CreateInstance(storeType, [folder, false]);
                var configuration = Activator.CreateInstance(configurationType, [first, "test-model", firstKey]);
                using var client = new HttpClient();
                window = (Window)Activator.CreateInstance(dialogType, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                    null, [dialogStore, client, configuration, HotKeyBinding.Default, false, null, (Action<bool>)(_ => { })], null)!;
                var masked = (PasswordBox)window.FindName("ApiKeyBox");
                var visible = (TextBox)window.FindName("VisibleApiKeyBox");
                var button = (Button)window.FindName("RevealKeyButton");
                var address = (TextBox)window.FindName("AddressBox");
                void Toggle() => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Check(masked.Password == "" && visible.Visibility == Visibility.Collapsed && visible.Text == "", "密钥默认遮蔽且不把已保存密钥填入界面");
                Toggle();
                Check(visible.Visibility == Visibility.Visible && visible.Text == firstKey && masked.Visibility == Visibility.Collapsed, "主动点击小眼睛可查看当前地址的已保存测试密钥");
                Toggle();
                Check(visible.Visibility == Visibility.Collapsed && visible.Text == "" && masked.Password == firstKey, "隐藏时清空明文控件但保留输入内容");
                Toggle(); visible.Text = "edited-test-key";
                Check(masked.Password == "edited-test-key", "明文编辑同步到保存和测试使用的密码控件");
                typeof(Window).GetMethod("OnDeactivated", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, [EventArgs.Empty]);
                Check(visible.Visibility == Visibility.Collapsed && visible.Text == "", "设置窗口失去焦点自动隐藏并清空明文控件");
                masked.Password = "typed-test-key"; Toggle();
                Check(visible.Text == "typed-test-key", "新输入的测试密钥也可切换显示");
                Toggle(); masked.Clear(); Toggle(); visible.Text = "edited-saved-test-key";
                address.Text = second;
                Check(masked.Password == "" && visible.Text == "" && visible.Visibility == Visibility.Collapsed, "查看并编辑已保存密钥后切换地址仍清空输入，不跨地址带入");
                Toggle();
                Check(visible.Text == secondKey, "切换后只显示新地址自己的测试密钥");
                window.Close();
                Check(masked.Password == "" && visible.Text == "", "关闭设置清空两个密钥输入控件");
                window = null;
            }
            catch (Exception error) { failure = error; }
            finally
            {
                window?.Close();
                if (Directory.Exists(folder))
                { foreach (var file in Directory.GetFiles(folder)) File.Delete(file); Directory.Delete(folder); }
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start(); thread.Join();
        if (failure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
        Console.WriteLine("9 key visibility checks passed; hidden test controls and fake keys only.");
    }

    private static void Check(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); Console.WriteLine("PASS " + message); }
}
