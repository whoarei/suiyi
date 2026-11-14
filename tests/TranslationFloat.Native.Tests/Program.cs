using System.Runtime.InteropServices;
using TranslationFloat.App;

Check(Marshal.SizeOf<NativeMethods.Input>() == 40, "INPUT 的 x64 原生布局为 40 字节");
Check(Marshal.OffsetOf<NativeMethods.Input>(nameof(NativeMethods.Input.Data)).ToInt32() == 8, "INPUT union 按原生指针对齐");
Check(Marshal.SizeOf<NativeMethods.GuiThreadInfo>() == 72, "GUITHREADINFO 的 x64 布局正确");
var paste = NativeMethods.PasteInputs();
Check(paste.Length == 4, "单次粘贴只有四个键盘事件");
Check(paste.All(input => input.Type == 1), "回填不包含鼠标或其他输入事件");
Check(paste.All(input => input.Data.Keyboard.VirtualKey != 0x0D), "任何回填事件均不包含 Enter");
Check(paste.Select(input => (input.Data.Keyboard.VirtualKey, input.Data.Keyboard.Flags)).SequenceEqual(new (ushort, uint)[] { (0x11, 0), (0x56, 0), (0x56, 2), (0x11, 2) }), "粘贴事件为完整的 Ctrl+V 按下与释放");
Console.WriteLine("7 native input checks passed; no input was injected.");

Exception? nativeControlFailure = null;
var nativeControlThread = new Thread(() =>
{
    try
    {
        using var ordinary = new System.Windows.Forms.TextBox();
        using var password = new System.Windows.Forms.TextBox { PasswordChar = '*' };
        using var readOnly = new System.Windows.Forms.TextBox { ReadOnly = true };
        var ordinaryStyle = NativeMethods.GetWindowLongPtr(ordinary.Handle, -16).ToInt64();
        Check((ordinaryStyle & 0x0080) != 0 && !NativeMethods.BlocksTextInsertion(ordinaryStyle), "真实 Win32 单行输入框的自动横滚样式不会被当成密码字段");
        Check(NativeMethods.BlocksTextInsertion(NativeMethods.GetWindowLongPtr(password.Handle, -16).ToInt64()), "真实 Win32 密码框被拦截");
        Check(NativeMethods.BlocksTextInsertion(NativeMethods.GetWindowLongPtr(readOnly.Handle, -16).ToInt64()), "真实 Win32 只读框被拦截");
    }
    catch (Exception exception) { nativeControlFailure = exception; }
});
nativeControlThread.SetApartmentState(ApartmentState.STA);
nativeControlThread.Start();
nativeControlThread.Join();
if (nativeControlFailure is not null) throw nativeControlFailure;
Console.WriteLine("3 native edit control checks passed; no user windows were accessed.");

var testFolder = Path.Combine(Path.GetTempPath(), "TranslationFloat.Settings.Tests." + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(testFolder);
try
{
    const string dummyKey = "local-test-value-never-used-as-a-real-key";
    var store = new DeepSeekSettingsStore(testFolder, useEnvironment: false);
    store.Save("deepseek-flash", dummyKey);
    Check(store.Load().ApiKey == dummyKey, "DPAPI 密钥可由当前用户解密");
    Check(!store.Load().ToString().Contains(dummyKey), "配置对象的字符串表示不泄露密钥");
    Check(!System.Text.Encoding.UTF8.GetString(File.ReadAllBytes(Path.Combine(testFolder, "deepseek.key"))).Contains(dummyKey), "密钥文件不保存明文");
    Check(!File.ReadAllText(Path.Combine(testFolder, "settings.json")).Contains(dummyKey), "普通 JSON 设置不包含密钥");
    store.Save("deepseek-v4-pro", "");
    Check(store.Load() is { Model: "deepseek-v4-pro", ApiKey: dummyKey }, "空密钥输入保留已有密钥并更新模型");
    File.WriteAllBytes(Path.Combine(testFolder, "deepseek.key"), [1, 2, 3]);
    Check(!store.Load().IsConfigured, "损坏的密钥不会导致程序崩溃或返回伪凭据");
    store.Save("deepseek-flash", dummyKey);
    File.WriteAllText(Path.Combine(testFolder, "settings.json"), "not-json");
    Check(store.Load() is { Model: "deepseek-flash", ApiKey: dummyKey }, "设置损坏时仍可独立恢复加密密钥");
    Console.WriteLine("7 settings checks passed; no production credentials read.");
}
finally
{
    // Only this test's newly created, non-recursive directory is removed.
    foreach (var file in Directory.GetFiles(testFolder)) File.Delete(file);
    Directory.Delete(testFolder);
}

static void Check(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
    Console.WriteLine("PASS " + message);
}
