using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using TranslationFloat.Core;

namespace TranslationFloat.App;

internal sealed class WindowsInsertionPlatform(Func<TargetSession, bool> isCurrentSession) : IInsertionPlatform
{
    private readonly FocusProbeClient _focusProbe = new();

    public async Task<(TargetSession? Target, string Message)> CaptureTargetAsync(CancellationToken cancellationToken)
    {
        try
        {
            var root = NativeMethods.GetAncestor(NativeMethods.GetForegroundWindow(), 2);
            var thread = NativeMethods.GetWindowThreadProcessId(root, out var processId);
            if (root == 0 || processId == Environment.ProcessId || thread == 0)
                return (null, "未找到原输入位置，请在目标输入框重新唤出。");
            using var process = Process.GetProcessById((int)processId);
            var name = process.ProcessName;
            var info = NativeMethods.GuiThreadInfo.Create();
            NativeMethods.GetGUIThreadInfo(thread, ref info);
            var focus = info.Focus;
            if (focus != 0 && (!NativeMethods.IsWindow(focus) || NativeMethods.GetAncestor(focus, 2) != root))
                return (null, "输入焦点不属于原窗口，请重新唤出。");
            var className = new StringBuilder(256);
            if (focus != 0) NativeMethods.GetClassName(focus, className, className.Capacity);
            var nativeEdit = IsNativeEdit(className.ToString());
            if (focus != 0 && !NativeMethods.IsWindowEnabled(focus))
                return (null, "原输入控件不可用，可以复制后手动粘贴。");
            if (nativeEdit && NativeMethods.BlocksTextInsertion(NativeMethods.GetWindowLongPtr(focus, -16).ToInt64()))
                return (null, "此输入框为密码或只读字段，请选择普通文本输入框。");

            var observation = nativeEdit
                ? new FocusObservation(true, true, true, false, false, true, null, "Native.Edit")
                : await _focusProbe.InspectAsync(root, cancellationToken);
            if (NativeMethods.GetAncestor(NativeMethods.GetForegroundWindow(), 2) != root)
                return (null, "输入焦点已变化，请在目标输入框重新唤出。");
            DiagnosticLog.Write("focus-probe", observation.ControlKind, name);
            if (observation.Reject)
                return (null, observation.Password || observation.ReadOnly == true
                    ? "此控件为密码或只读区域，可以使用复制功能。"
                    : "当前位置不是可用的文本输入框，请重新选择。");
            if (focus == 0 && !observation.HasIdentity)
                return (null, "无法确认原输入位置，可以复制后手动粘贴。");

            var target = new TargetSession(Guid.NewGuid(), root, focus, (int)processId,
                process.StartTime.ToUniversalTime().Ticks, name, TargetPresentation.AppName(name),
                observation.HasIdentity ? observation.FocusId : null);
            DiagnosticLog.Write("capture", observation.HasIdentity || nativeEdit ? "bound-editable" : "bound-opaque", name, root, focus);
            return (target, observation.HasIdentity || nativeEdit
                ? "确认译文后按 Ctrl+Enter，回填到原输入框。"
                : "请确认光标已在目标输入框中，确认译文后回填。");
        }
        catch (Exception)
        { return (null, "无法识别原输入位置，仍可复制文字后手动粘贴。"); }
    }

    public bool IsSessionValid(TargetSession target)
    {
        if (!isCurrentSession(target) || !NativeMethods.IsWindow(target.Window)) return false;
        if (target.FocusWindow != 0 && (!NativeMethods.IsWindow(target.FocusWindow) || NativeMethods.GetAncestor(target.FocusWindow, 2) != target.Window)) return false;
        if (target.FocusWindow == 0 && target.AccessibleFocusId is null) return false;
        try
        {
            NativeMethods.GetWindowThreadProcessId(target.Window, out var pid);
            using var process = Process.GetProcessById(target.ProcessId);
            return pid == target.ProcessId && process.StartTime.ToUniversalTime().Ticks == target.ProcessStartTime;
        }
        catch (Exception) { return false; }
    }

    public async Task<bool> WaitForKeysReleasedAsync(CancellationToken cancellationToken)
    {
        // Include Enter: its key-up must not leak into a chat composer after restoring focus.
        int[] keys = [0x10, 0x11, 0x12, 0x0D, 0x5B, 0x5C];
        var timer = Stopwatch.StartNew();
        while (timer.ElapsedMilliseconds < 600)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var down = false;
            foreach (var key in keys) down |= (NativeMethods.GetAsyncKeyState(key) & 0x8000) != 0;
            if (!down) return true;
            await Task.Delay(15, cancellationToken);
        }
        return false;
    }

    public async Task<bool> RestoreTargetAsync(TargetSession target, CancellationToken cancellationToken)
    {
        if (!IsSessionValid(target)) return false;
        if (NativeMethods.IsIconic(target.Window)) NativeMethods.ShowWindowAsync(target.Window, 9);
        NativeMethods.SetForegroundWindow(target.Window);
        for (var attempt = 0; attempt < 20; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!IsSessionValid(target)) return false;
            if (HasNativeTargetFocus(target)) return true;
            await Task.Delay(20, cancellationToken);
        }
        DiagnosticLog.Write("restore", "focus-mismatch", target.ProcessName, target.Window, target.FocusWindow);
        return false;
    }

    private bool HasNativeTargetFocus(TargetSession target)
    {
        if (NativeMethods.GetAncestor(NativeMethods.GetForegroundWindow(), 2) != target.Window) return false;
        var thread = NativeMethods.GetWindowThreadProcessId(target.Window, out var pid);
        var info = NativeMethods.GuiThreadInfo.Create();
        return pid == target.ProcessId && (target.FocusWindow == 0 || (NativeMethods.GetGUIThreadInfo(thread, ref info) && info.Focus == target.FocusWindow));
    }

    public async Task<bool> VerifyTargetAsync(TargetSession target, CancellationToken cancellationToken)
    {
        if (!IsSessionValid(target) || !HasNativeTargetFocus(target)) return false;
        if (target.FocusWindow != 0)
        {
            var className = new StringBuilder(256);
            NativeMethods.GetClassName(target.FocusWindow, className, className.Capacity);
            if (!NativeMethods.IsWindowEnabled(target.FocusWindow) ||
                (IsNativeEdit(className.ToString()) && NativeMethods.BlocksTextInsertion(NativeMethods.GetWindowLongPtr(target.FocusWindow, -16).ToInt64()))) return false;
        }
        if (target.AccessibleFocusId is { } identity)
        {
            var observation = await _focusProbe.InspectAsync(target.Window, cancellationToken);
            if (!observation.Matches(identity)) return false;
        }
        return IsSessionValid(target) && HasNativeTargetFocus(target);
    }

    private static bool IsNativeEdit(string className) => className.StartsWith("Edit", StringComparison.OrdinalIgnoreCase) || className.StartsWith("RichEdit", StringComparison.OrdinalIgnoreCase);

    public async Task<uint> WriteClipboardAsync(string text, CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 4; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                return await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    Clipboard.SetText(text, TextDataFormat.UnicodeText);
                    return NativeMethods.GetClipboardSequenceNumber();
                });
            }
            catch (ExternalException) when (attempt < 3) { await Task.Delay(35, cancellationToken); }
        }
        throw new InvalidOperationException("Clipboard unavailable");
    }

    public uint ClipboardSequence => NativeMethods.GetClipboardSequenceNumber();

    public InjectionReceipt Paste()
    {
        var inputs = NativeMethods.PasteInputs();
        var count = NativeMethods.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<NativeMethods.Input>());
        if (count > 0 && count < inputs.Length)
        {
            // Release only the synthetic chord's keys; never replay a partial paste.
            NativeMethods.Input[] release = [NativeMethods.Key(0x56, true), NativeMethods.Key(0x11, true)];
            NativeMethods.SendInput((uint)release.Length, release, Marshal.SizeOf<NativeMethods.Input>());
        }
        return new((uint)inputs.Length, count);
    }

    public Point GetPopupPosition(double width, double height)
    {
        NativeMethods.GetCursorPos(out var cursor);
        var monitor = NativeMethods.MonitorFromPoint(cursor, 2);
        var info = new NativeMethods.MonitorInfo { Size = (uint)Marshal.SizeOf<NativeMethods.MonitorInfo>() };
        if (!NativeMethods.GetMonitorInfo(monitor, ref info)) return new Point(100, 100);
        // Set Left/Top in DIPs. Clamp the actual window to the current monitor work area.
        var dpi = Math.Max(96, NativeMethods.GetDpiForWindow(NativeMethods.GetForegroundWindow()));
        var scale = dpi / 96d;
        var x = Math.Clamp(cursor.X + 14d, info.Work.Left, Math.Max(info.Work.Left, info.Work.Right - width * scale));
        var y = Math.Clamp(cursor.Y + 14d, info.Work.Top, Math.Max(info.Work.Top, info.Work.Bottom - height * scale));
        return new Point(x / scale, y / scale);
    }
}
