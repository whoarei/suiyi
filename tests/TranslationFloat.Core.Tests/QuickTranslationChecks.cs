using TranslationFloat.Core;

internal static class QuickTranslationChecks
{
    public static IEnumerable<(string Name, Func<Task> Run)> Cases(TargetSession target)
    {
        var selected = target with { Selection = SelectionSnapshot.Create("你好", SelectionMethod.Automation, "range").Binding };
        yield return ("快速模式直接替换一次，不恢复或激活其他窗口", async () =>
        {
            var platform = new FakePlatform(); var provider = Provider();
            var result = await new QuickTranslationCoordinator(provider, new(platform)).TranslateAndReplaceAsync(selected, "你好");
            Check(result.Outcome == InsertionOutcome.Submitted && platform.PasteCalls == 1 && platform.RestoreCalls == 0);
            Check(platform.Pasted == "Hello" && provider.Calls == 1);
        });
        yield return ("无选区不翻译、不读取剪贴板、不粘贴", async () =>
        {
            var platform = new FakePlatform(); var provider = Provider();
            var result = await new QuickTranslationCoordinator(provider, new(platform)).TranslateAndReplaceAsync(target, "你好");
            Check(result.Outcome == InsertionOutcome.Rejected && provider.Calls == 0 && platform.Writes == 0 && platform.PasteCalls == 0);
        });
        yield return ("快速模式未配置服务时提示且不粘贴", async () =>
        {
            var platform = new FakePlatform();
            var result = await new QuickTranslationCoordinator(null, new(platform)).TranslateAndReplaceAsync(selected, "你好");
            Check(result.Outcome == InsertionOutcome.Rejected && platform.PasteCalls == 0);
        });
        yield return ("快速翻译失败只返回提示，不更改目标或持续重试", async () =>
        {
            var platform = new FakePlatform();
            var provider = new FakeProvider((_, _) => throw new TranslationException(TranslationFailure.Network));
            var result = await new QuickTranslationCoordinator(provider, new(platform)).TranslateAndReplaceAsync(selected, "你好");
            Check(result.Outcome == InsertionOutcome.Rejected && result.Message.Contains("网络") && provider.Calls == 1 && platform.Writes == 0 && platform.PasteCalls == 0);
        });
        yield return ("空的快速翻译结果不替换选中文字", async () =>
        {
            var platform = new FakePlatform();
            var result = await new QuickTranslationCoordinator(new FakeProvider((_, _) => Task.FromResult(" ")), new(platform)).TranslateAndReplaceAsync(selected, "你好");
            Check(result.Outcome == InsertionOutcome.Rejected && platform.PasteCalls == 0);
        });
        yield return ("取消后迟到的快速译文不能写入目标", async () =>
        {
            using var cancellation = new CancellationTokenSource();
            var gate = new TaskCompletionSource<string>(); var platform = new FakePlatform();
            var pending = new QuickTranslationCoordinator(new FakeProvider((_, _) => gate.Task), new(platform))
                .TranslateAndReplaceAsync(selected, "你好", cancellation.Token);
            cancellation.Cancel(); gate.SetResult("Late result");
            var result = await pending;
            Check(result.Outcome == InsertionOutcome.Rejected && result.Message.Contains("取消") && platform.Writes == 0 && platform.PasteCalls == 0);
        });
        yield return ("快速翻译期间切换窗口不抢回焦点或改剪贴板", async () =>
        {
            var platform = new FakePlatform();
            var provider = new FakeProvider((_, _) => { platform.Focused = false; return Task.FromResult("Hello"); });
            var result = await new QuickTranslationCoordinator(provider, new(platform)).TranslateAndReplaceAsync(selected, "你好");
            Check(result.Outcome == InsertionOutcome.Rejected && platform.RestoreCalls == 0 && platform.Writes == 0 && platform.PasteCalls == 0);
        });
        yield return ("快速模式选区变化停止替换并保留译文剪贴板", async () =>
        {
            var platform = new FakePlatform { SelectionValid = false };
            var result = await new QuickTranslationCoordinator(Provider(), new(platform)).TranslateAndReplaceAsync(selected, "你好");
            Check(result.Outcome == InsertionOutcome.Rejected && platform.Pasted is null && platform.Written == "Hello");
        });
        yield return ("快速模式部分提交不重试", async () =>
        {
            var platform = new FakePlatform { Receipt = new(4, 2) };
            var result = await new QuickTranslationCoordinator(Provider(), new(platform)).TranslateAndReplaceAsync(selected, "你好");
            Check(result.Outcome == InsertionOutcome.Uncertain && platform.PasteCalls == 1);
        });
        yield return ("快速模式未知异常不会泄露原文或异常细节", async () =>
        {
            var platform = new FakePlatform();
            var result = await new QuickTranslationCoordinator(new FakeProvider((_, _) => throw new InvalidOperationException("private demo")), new(platform))
                .TranslateAndReplaceAsync(selected, "你好");
            Check(result.Outcome == InsertionOutcome.Rejected && !result.Message.Contains("private demo") && !result.Message.Contains("你好") && platform.PasteCalls == 0);
        });
        yield return ("快速模式剪贴板竞争时不粘贴", async () =>
        {
            var platform = new FakePlatform { Sequence = 12 };
            var result = await new QuickTranslationCoordinator(Provider(), new(platform)).TranslateAndReplaceAsync(selected, "你好");
            Check(result.Outcome == InsertionOutcome.Rejected && platform.PasteCalls == 0);
        });
        yield return ("默认快捷键保持 Ctrl+Alt+T", () =>
        { Check(HotKeyBinding.Default.IsValid && HotKeyBinding.Default.DisplayName == "Ctrl+Alt+T"); return Task.CompletedTask; });
        yield return ("快捷键拒绝裸字母、Shift 单独修饰及未知按键", () =>
        {
            Check(!new HotKeyBinding(0, 0x41).IsValid && !new HotKeyBinding(4, 0x41).IsValid &&
                !new HotKeyBinding(10, 0x41).IsValid && !new HotKeyBinding(2, 0x0D).IsValid);
            return Task.CompletedTask;
        });
        yield return ("快捷键支持数字和 F1 到 F12 及多个修饰键", () =>
        {
            Check(new HotKeyBinding(2, 0x30).IsValid && new HotKeyBinding(1, 0x70).DisplayName == "Alt+F1" &&
                new HotKeyBinding(6, 0x7B).DisplayName == "Ctrl+Shift+F12" && !new HotKeyBinding(3, 0x7C).IsValid);
            return Task.CompletedTask;
        });
    }

    private static FakeProvider Provider() => new((_, _) => Task.FromResult("Hello"));
    private static void Check(bool condition) { if (!condition) throw new InvalidOperationException("Quick mode assertion failed"); }
    private sealed class FakeProvider(Func<TranslationRequest, CancellationToken, Task<string>> translate) : ITranslationProvider
    {
        public int Calls;
        public Task<string> TranslateAsync(TranslationRequest request, CancellationToken cancellationToken)
        { Calls++; return translate(request, cancellationToken); }
    }
}
