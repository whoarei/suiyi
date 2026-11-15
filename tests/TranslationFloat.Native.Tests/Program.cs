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
        var nativeClass = new System.Text.StringBuilder(256);
        NativeMethods.GetClassName(ordinary.Handle, nativeClass, nativeClass.Capacity);
        Check(NativeMethods.IsNativeEdit(nativeClass.ToString()), "真实 WinForms 编辑框进入原生选区检查路径");
        Check((ordinaryStyle & 0x0080) != 0 && !NativeMethods.BlocksTextInsertion(ordinaryStyle), "真实 Win32 单行输入框的自动横滚样式不会被当成密码字段");
        Check(NativeMethods.BlocksTextInsertion(NativeMethods.GetWindowLongPtr(password.Handle, -16).ToInt64()), "真实 Win32 密码框被拦截");
        Check(NativeMethods.BlocksTextInsertion(NativeMethods.GetWindowLongPtr(readOnly.Handle, -16).ToInt64()), "真实 Win32 只读框被拦截");
        ordinary.Text = "before 中间 after";
        ordinary.Select(7, 2);
        Check(NativeMethods.TryGetSelection(ordinary.Handle, out var start, out var end) && start == 7 && end == 9, "有界原生读取取得精确选区");
        ordinary.Select(9, 0);
        Check(NativeMethods.TryGetSelection(ordinary.Handle, out start, out end) && start == end, "光标位置不会被当作选中文字");
    }
    catch (Exception exception) { nativeControlFailure = exception; }
});
nativeControlThread.SetApartmentState(ApartmentState.STA);
nativeControlThread.Start();
nativeControlThread.Join();
if (nativeControlFailure is not null) throw nativeControlFailure;
Console.WriteLine("6 native edit control checks passed; no user windows were accessed.");

SettingsChecks.Run();

static void Check(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
    Console.WriteLine("PASS " + message);
}

// Private test registry branches only; never read or change the real login startup key.
StartupChecks.Run();
PreferenceChecks.Run();
KeyVisibilityChecks.Run();
ThemeChecks.Run(args is ["--theme-preview", var previewDirectory] ? previewDirectory : null);
