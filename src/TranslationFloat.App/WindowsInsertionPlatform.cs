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
    public int TriggerKey { get; set; } = 0x54;

    public async Task<(TargetSession? Target, string Message, string? SelectedText)> CaptureTargetAsync(CancellationToken cancellationToken)
    {
        try
        {
            var root = NativeMethods.GetAncestor(NativeMethods.GetForegroundWindow(), 2);
            var thread = NativeMethods.GetWindowThreadProcessId(root, out var processId);
            if (root == 0 || processId == Environment.ProcessId || thread == 0)
                return (null, "未找到原输入位置，请在目标输入框重新唤出。", null);
            using var process = Process.GetProcessById((int)processId);
            var name = process.ProcessName;
            var info = NativeMethods.GuiThreadInfo.Create();
            NativeMethods.GetGUIThreadInfo(thread, ref info);
            var focus = info.Focus;
            if (focus != 0 && (!NativeMethods.IsWindow(focus) || NativeMethods.GetAncestor(focus, 2) != root))
                return (null, "输入焦点不属于原窗口，请重新唤出。", null);
            var className = new StringBuilder(256);
            if (focus != 0) NativeMethods.GetClassName(focus, className, className.Capacity);
            var nativeEdit = NativeMethods.IsNativeEdit(className.ToString());
            if (focus != 0 && !NativeMethods.IsWindowEnabled(focus))
                return (null, "原输入控件不可用，可以复制后手动粘贴。", null);
            if (nativeEdit && NativeMethods.BlocksTextInsertion(NativeMethods.GetWindowLongPtr(focus, -16).ToInt64()))
                return (null, "此输入框为密码或只读字段，请选择普通文本输入框。", null);

            var observation = nativeEdit
                ? new FocusObservation(true, true, true, false, false, true, null, "Native.Edit")
                : await _focusProbe.InspectAsync(root, cancellationToken, readSelection: true);
            if (NativeMethods.GetAncestor(NativeMethods.GetForegroundWindow(), 2) != root)
                return (null, "输入焦点已变化，请在目标输入框重新唤出。", null);
            DiagnosticLog.Write("focus-probe", observation.ControlKind, name);
            if (observation.Reject)
                return (null, observation.Password || observation.ReadOnly == true
                    ? "此控件为密码或只读区域，可以使用复制功能。"
                    : "当前位置不是可用的文本输入框，请重新选择。", null);
            if (focus == 0 && !observation.HasIdentity)
                return (null, "无法确认原输入位置，可以复制后手动粘贴。", null);

            var target = new TargetSession(Guid.NewGuid(), root, focus, (int)processId,
                process.StartTime.ToUniversalTime().Ticks, name, TargetPresentation.AppName(name),
                observation.HasIdentity ? observation.FocusId : null);
            var selection = observation.Selection ?? SelectionSnapshot.Unavailable;
            if (nativeEdit)
                selection = await CaptureNativeSelectionAsync(target, cancellationToken);
            if (!HasNativeTargetFocus(target))
                return (null, "输入焦点已变化，请重新选择文字后唤出。", null);
            if (selection.State is SelectionState.TooLong or SelectionState.Multiple)
                return (null, selection.State == SelectionState.TooLong
                    ? "选中文字超过 2000 字，请缩小选区后重新唤出。"
                    : "暂不支持多个选区，请只选择一段文字。", null);
            if (nativeEdit && selection.State == SelectionState.Unavailable)
                return (null, "未能读取原选区，请重新选择或在浮窗中手动输入。", null);
            target = target with { Selection = selection.Binding };
            DiagnosticLog.Write("capture", observation.HasIdentity || nativeEdit ? "bound-editable" : "bound-opaque", name, root, focus);
            if (target.Selection is not null)
                return (target, "Ctrl+Enter 替换原选中文字，其他内容保留。", selection.Text);
            return (target, observation.HasIdentity || nativeEdit
                ? "确认译文后按 Ctrl+Enter，回填到原输入框。"
                : "此控件未提供可读取的选区，请手动输入并确认光标位置。", null);
        }
        catch (Exception)
        { return (null, "无法识别原输入位置，仍可复制文字后手动粘贴。", null); }
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
        int[] keys = [0x10, 0x11, 0x12, 0x0D, 0x5B, 0x5C, TriggerKey, 0x43];
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
                (NativeMethods.IsNativeEdit(className.ToString()) && NativeMethods.BlocksTextInsertion(NativeMethods.GetWindowLongPtr(target.FocusWindow, -16).ToInt64()))) return false;
        }
        if (target.AccessibleFocusId is { } identity)
        {
            var observation = await _focusProbe.InspectAsync(target.Window, cancellationToken,
                readSelection: target.Selection?.Method == SelectionMethod.Automation);
            if (!observation.Matches(identity)) return false;
            if (target.Selection?.Method == SelectionMethod.Automation && observation.Selection?.Binding != target.Selection) return false;
        }
        if (target.Selection is { Method: SelectionMethod.NativeEdit } selection &&
            (!NativeMethods.TryGetSelection(target.FocusWindow, out var start, out var end) || start != selection.Start || end != selection.End)) return false;
        return IsSessionValid(target) && HasNativeTargetFocus(target);
    }

    public async Task<bool> PrepareSelectionAsync(TargetSession target, CancellationToken cancellationToken)
    {
        if (target.Selection?.Method != SelectionMethod.NativeEdit) return true;
        if (!IsSessionValid(target)) return false;
        var selection = await CaptureNativeSelectionAsync(target, cancellationToken);
        return selection.Binding == target.Selection;
    }

    private async Task<SelectionSnapshot> CaptureNativeSelectionAsync(TargetSession target, CancellationToken cancellationToken)
    {
        if (!await WaitForKeysReleasedAsync(cancellationToken) || !HasNativeTargetFocus(target) ||
            !NativeMethods.IsWindowEnabled(target.FocusWindow) ||
            NativeMethods.BlocksTextInsertion(NativeMethods.GetWindowLongPtr(target.FocusWindow, -16).ToInt64()) ||
            !NativeMethods.TryGetSelection(target.FocusWindow, out var start, out var end)) return SelectionSnapshot.Unavailable;
        if (end == start) return SelectionSnapshot.None;
        if (end < start) return SelectionSnapshot.Unavailable;
        if (end - start > SelectionSnapshot.MaxLength) return new(SelectionState.TooLong);
        var sequence = ClipboardSequence;
        var inputs = NativeMethods.CopyInputs();
        var count = NativeMethods.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<NativeMethods.Input>());
        if (count != inputs.Length)
        {
            if (count > 0)
                NativeMethods.SendInput(2, [NativeMethods.Key(0x43, true), NativeMethods.Key(0x11, true)], Marshal.SizeOf<NativeMethods.Input>());
            return SelectionSnapshot.Unavailable;
        }
        var timer = Stopwatch.StartNew();
        while (timer.ElapsedMilliseconds < 350)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!HasNativeTargetFocus(target)) return SelectionSnapshot.Unavailable;
            var current = ClipboardSequence;
            if (current != sequence && current != 0)
            {
                NativeMethods.GetWindowThreadProcessId(NativeMethods.GetClipboardOwner(), out var owner);
                if (owner != target.ProcessId) return SelectionSnapshot.Unavailable;
                var selected = ReadCopiedSelection(current);
                if (selected is not null && NativeMethods.TryGetSelection(target.FocusWindow, out var nowStart, out var nowEnd) &&
                    nowStart == start && nowEnd == end && HasNativeTargetFocus(target))
                    return SelectionSnapshot.Create(selected, SelectionMethod.NativeEdit, $"{start}:{end}", start, end);
            }
            await Task.Delay(15, cancellationToken);
        }
        return SelectionSnapshot.Unavailable;
    }

    private static string? ReadCopiedSelection(uint sequence)
    {
        // Read only a newly copied, target-owned Unicode selection, capped at 2001
        // code units. Never fetch or restore the previous arbitrary clipboard data.
        if (!NativeMethods.OpenClipboard(0)) return null;
        try
        {
            if (NativeMethods.GetClipboardSequenceNumber() != sequence) return null;
            var handle = NativeMethods.GetClipboardData(13); // CF_UNICODETEXT
            var pointer = NativeMethods.GlobalLock(handle);
            if (pointer == 0) return null;
            try
            {
                var length = (int)Math.Min((ulong)NativeMethods.GlobalSize(handle) / 2, (ulong)SelectionSnapshot.MaxLength + 1);
                var text = new StringBuilder(length);
                for (var index = 0; index < length; index++)
                {
                    var character = (char)Marshal.ReadInt16(pointer, index * 2);
                    if (character == '\0') return NativeMethods.GetClipboardSequenceNumber() == sequence ? text.ToString() : null;
                    text.Append(character);
                }
                return length > SelectionSnapshot.MaxLength ? text.ToString() : null;
            }
            finally { NativeMethods.GlobalUnlock(handle); }
        }
        finally { NativeMethods.CloseClipboard(); }
    }

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
