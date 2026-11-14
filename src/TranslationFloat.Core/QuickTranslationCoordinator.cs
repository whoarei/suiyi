namespace TranslationFloat.Core;

// A single explicit hotkey request. No popup, debounce, retry, or focus restoration.
public sealed class QuickTranslationCoordinator(ITranslationProvider? provider, InsertionCoordinator insertion)
{
    public async Task<InsertionResult> TranslateAndReplaceAsync(
        TargetSession? target, string? source, CancellationToken cancellationToken = default)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (target?.Selection is null || string.IsNullOrWhiteSpace(source))
                return new(InsertionOutcome.Rejected, "请先在支持的输入框中选中文字，再按 Ctrl+Alt+T。");
            if (source.Length > SelectionSnapshot.MaxLength)
                return new(InsertionOutcome.Rejected, "选中文字超过 2000 字，请缩小选区后重试。");
            if (provider is null)
                return new(InsertionOutcome.Rejected, "请先从随译托盘菜单设置翻译接口和模型。");

            var translation = await provider.TranslateAsync(new(source), cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(translation))
                return new(InsertionOutcome.Rejected, "翻译结果为空，未替换。请重试。");
            return await insertion.InsertAsync(target, translation, cancellationToken, restoreTarget: false);
        }
        catch (OperationCanceledException)
        { return new(InsertionOutcome.Rejected, "快速翻译已取消。"); }
        catch (TranslationException error)
        { return new(InsertionOutcome.Rejected, error.Message); }
        catch (Exception)
        { return new(InsertionOutcome.Rejected, "快速翻译失败，未替换。请重试。"); }
    }
}
