namespace TranslationFloat.Core;

public sealed record TargetSession(
    Guid Id,
    nint Window,
    nint FocusWindow,
    int ProcessId,
    long ProcessStartTime,
    string ProcessName,
    string DisplayName,
    string? AccessibleFocusId = null,
    SelectionBinding? Selection = null);

public enum InsertionOutcome { Rejected, Submitted, Uncertain, Busy }

public sealed record InsertionResult(InsertionOutcome Outcome, string Message)
{
    public bool WasSubmitted => Outcome is InsertionOutcome.Submitted or InsertionOutcome.Uncertain;
}

public readonly record struct InjectionReceipt(uint Requested, uint Submitted);

public interface IInsertionPlatform
{
    bool IsSessionValid(TargetSession target);
    Task<bool> WaitForKeysReleasedAsync(CancellationToken cancellationToken);
    Task<bool> RestoreTargetAsync(TargetSession target, CancellationToken cancellationToken);
    Task<bool> PrepareSelectionAsync(TargetSession target, CancellationToken cancellationToken);
    Task<uint> WriteClipboardAsync(string text, CancellationToken cancellationToken);
    Task<bool> VerifyTargetAsync(TargetSession target, CancellationToken cancellationToken);
    uint ClipboardSequence { get; }
    InjectionReceipt Paste();
}

public sealed class InsertionCoordinator(IInsertionPlatform platform)
{
    private readonly HashSet<Guid> _consumed = [];
    private int _busy;

    public async Task<InsertionResult> InsertAsync(
        TargetSession? target, string text, CancellationToken cancellationToken = default)
    {
        if (Interlocked.CompareExchange(ref _busy, 1, 0) != 0)
            return new(InsertionOutcome.Busy, "正在回填，请稍候。");

        var injectionStarted = false;
        try
        {
            if (target is null || string.IsNullOrWhiteSpace(text))
                return Reject("请在目标输入框按 Ctrl+Alt+T 唤出，并确认译文。");
            if (_consumed.Contains(target.Id) || !platform.IsSessionValid(target))
                return Reject("原输入位置已失效，请回到输入框重新唤出。");
            if (!await platform.WaitForKeysReleasedAsync(cancellationToken))
                return Reject("请松开快捷键，再试一次。");
            if (!platform.IsSessionValid(target) ||
                !await platform.RestoreTargetAsync(target, cancellationToken))
                return Reject("无法恢复原输入位置，请重新唤出或复制文字。");

            // Compatibility copy checks happen before placing the translation on the
            // clipboard. A later Ctrl+C must never overwrite what Ctrl+V will paste.
            var selectionValid = await platform.PrepareSelectionAsync(target, cancellationToken);
            var sequence = await platform.WriteClipboardAsync(text, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (!selectionValid || !platform.IsSessionValid(target) || !await platform.VerifyTargetAsync(target, cancellationToken) || !platform.IsSessionValid(target))
                return Reject(target.Selection is null
                    ? "输入焦点已改变，已停止回填。文字已留在剪贴板。"
                    : "原选区或输入焦点已改变，已停止替换。译文已留在剪贴板。");
            if (sequence == 0 || platform.ClipboardSequence != sequence)
                return Reject("剪贴板已改变，已停止回填。");

            // Once a call might have submitted input, this session is never retried.
            _consumed.Add(target.Id);
            injectionStarted = true;
            var receipt = platform.Paste();
            if (receipt.Requested > 0 && receipt.Submitted == receipt.Requested)
                return new(InsertionOutcome.Submitted, "已执行粘贴，请确认原输入框内容。");
            if (receipt.Submitted > 0)
                return new(InsertionOutcome.Uncertain, "粘贴结果不确定，请检查输入框；不会自动重试。");
            return Reject("系统未接受粘贴。请检查权限或手动粘贴。");
        }
        catch (OperationCanceledException)
        {
            return injectionStarted
                ? new(InsertionOutcome.Uncertain, "操作已中断，请检查输入框；不会自动重试。")
                : Reject("回填已取消，草稿已保留。");
        }
        catch (Exception)
        {
            return injectionStarted
                ? new(InsertionOutcome.Uncertain, "粘贴结果不确定，请检查输入框；不会自动重试。")
                : Reject("回填失败，草稿已保留，可以复制后手动粘贴。");
        }
        finally { Volatile.Write(ref _busy, 0); }
    }

    private static InsertionResult Reject(string message) => new(InsertionOutcome.Rejected, message);
}
