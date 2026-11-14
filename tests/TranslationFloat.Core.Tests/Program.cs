using TranslationFloat.Core;

var target = new TargetSession(Guid.NewGuid(), 1, 2, 3, 4, "Weixin", "微信");
var tests = new (string Name, Func<Task> Run)[]
{
    ("成功回填只提交一次，原目标会话不可重放", async () =>
    {
        var p = new FakePlatform(); var service = new InsertionCoordinator(p);
        Check((await service.InsertAsync(target, "hello")).Outcome == InsertionOutcome.Submitted);
        Check((await service.InsertAsync(target, "hello")).Outcome == InsertionOutcome.Rejected);
        Check(p.PasteCalls == 1);
    }),
    ("目标已失效时不恢复窗口、不改剪贴板", async () =>
    {
        var p = new FakePlatform { Valid = false };
        Check((await new InsertionCoordinator(p).InsertAsync(target, "hello")).Outcome == InsertionOutcome.Rejected);
        Check(p.RestoreCalls == 0 && p.Writes == 0 && p.PasteCalls == 0);
    }),
    ("焦点恢复失败时不粘贴", async () =>
    {
        var p = new FakePlatform { Restored = false };
        await new InsertionCoordinator(p).InsertAsync(target, "hello");
        Check(p.Writes == 0 && p.PasteCalls == 0);
    }),
    ("剪贴板竞争时停止，不覆盖新内容", async () =>
    {
        var p = new FakePlatform { Sequence = 12 };
        await new InsertionCoordinator(p).InsertAsync(target, "hello");
        Check(p.Writes == 1 && p.PasteCalls == 0);
    }),
    ("恢复后目标改变时不提交输入", async () =>
    {
        var p = new FakePlatform { Focused = false };
        await new InsertionCoordinator(p).InsertAsync(target, "hello");
        Check(p.PasteCalls == 0);
    }),
    ("部分输入提交不自动重试，也不重放会话", async () =>
    {
        var p = new FakePlatform { Receipt = new(4, 2) }; var service = new InsertionCoordinator(p);
        Check((await service.InsertAsync(target, "hello")).Outcome == InsertionOutcome.Uncertain);
        await service.InsertAsync(target, "hello"); Check(p.PasteCalls == 1);
    }),
    ("修饰键未松开时不抢焦点", async () =>
    {
        var p = new FakePlatform { KeysReleased = false };
        await new InsertionCoordinator(p).InsertAsync(target, "hello");
        Check(p.RestoreCalls == 0 && p.PasteCalls == 0);
    }),
    ("取消等待不会更改目标或剪贴板", async () =>
    {
        using var cancel = new CancellationTokenSource(); cancel.Cancel();
        var p = new FakePlatform();
        await new InsertionCoordinator(p).InsertAsync(target, "hello", cancel.Token);
        Check(p.Writes == 0 && p.PasteCalls == 0);
    }),
    ("重入请求不会二次提交", async () =>
    {
        var gate = new TaskCompletionSource<bool>(); var p = new FakePlatform { Gate = gate.Task };
        var service = new InsertionCoordinator(p); var first = service.InsertAsync(target, "hello");
        Check((await service.InsertAsync(target, "hello")).Outcome == InsertionOutcome.Busy);
        gate.SetResult(true); await first; Check(p.PasteCalls == 1);
    }),
    ("输入提交期间异常按未知结果处理", async () =>
    {
        var p = new FakePlatform { ThrowOnPaste = true }; var service = new InsertionCoordinator(p);
        Check((await service.InsertAsync(target, "hello")).Outcome == InsertionOutcome.Uncertain);
        await service.InsertAsync(target, "hello"); Check(p.PasteCalls == 1);
    }),
    ("异步控件验证期间会话失效时不提交输入", async () =>
    {
        var p = new FakePlatform(); p.OnVerify = () => p.Valid = false;
        await new InsertionCoordinator(p).InsertAsync(target, "hello"); Check(p.PasteCalls == 0);
    }),
    ("最终控件验证期间取消不会提交粘贴", async () =>
    {
        using var cancellation = new CancellationTokenSource();
        var p = new FakePlatform { OnVerify = cancellation.Cancel };
        var result = await new InsertionCoordinator(p).InsertAsync(target, "hello", cancellation.Token);
        Check(result.Outcome == InsertionOutcome.Rejected && p.PasteCalls == 0);
    }),
    ("未知应用名称可以展示，名称映射不充当白名单", () =>
    {
        Check(TargetPresentation.AppName("MyCustomEditor") == "MyCustomEditor");
        Check(TargetPresentation.AppName("chrome") == "Google Chrome"); return Task.CompletedTask;
    }),
    ("可编辑控件具有可验证的焦点身份", () =>
    {
        var probe = new FocusObservation(true, true, true, false, false, true, "field-1", "ControlType.Edit");
        Check(probe.HasIdentity && probe.Matches("field-1") && !probe.Matches("field-2")); return Task.CompletedTask;
    }),
    ("密码控件拒绝回填", () =>
    {
        var probe = new FocusObservation(true, true, true, true, false, true, "password", "ControlType.Edit");
        Check(probe.Reject && !probe.HasIdentity); return Task.CompletedTask;
    }),
    ("只读控件拒绝回填", () =>
    {
        var probe = new FocusObservation(true, true, true, false, true, false, "readonly", "ControlType.Edit");
        Check(probe.Reject && !probe.Matches("readonly")); return Task.CompletedTask;
    }),
    ("按钮和不可用控件拒绝回填", () =>
    {
        Check(new FocusObservation(true, true, true, false, null, false, "button", "ControlType.Button").Reject);
        Check(new FocusObservation(true, true, false, false, false, true, "disabled", "ControlType.Edit").Reject); return Task.CompletedTask;
    }),
    ("属于其他窗口的控件拒绝回填", () =>
    {
        Check(new FocusObservation(true, false, true, false, false, true, "other", "ControlType.Edit").Reject); return Task.CompletedTask;
    }),
    ("选区变化时拒绝替换并保留译文剪贴板", async () =>
    {
        var selected = target with { Selection = SelectionSnapshot.Create("原文", SelectionMethod.NativeEdit, "0:2", 0, 2).Binding };
        var p = new FakePlatform { SelectionValid = false };
        var result = await new InsertionCoordinator(p).InsertAsync(selected, "translation");
        Check(result.Outcome == InsertionOutcome.Rejected && result.Message.Contains("原选区"));
        Check(p.Writes == 1 && p.PasteCalls == 0);
    }),
    ("相同文字不同选区不会被视为原选区", () =>
    {
        var original = SelectionSnapshot.Create("secret demo", SelectionMethod.Automation, "10,20,30,40");
        Check(original.Binding != SelectionSnapshot.Create("secret demo", SelectionMethod.Automation, "50,20,30,40").Binding);
        Check(original.Binding != SelectionSnapshot.Create("changed demo", SelectionMethod.Automation, "10,20,30,40").Binding);
        Check(!original.ToString().Contains("secret demo") && !original.Binding!.ToString().Contains("secret demo"));
        return Task.CompletedTask;
    }),
    ("过长选区不截断，不保留可回填绑定", () =>
    {
        var snapshot = SelectionSnapshot.Create(new string('x', 2001), SelectionMethod.NativeEdit, "0:2001", 0, 2001);
        Check(snapshot.State == SelectionState.TooLong && snapshot.Text is null && snapshot.Binding is null);
        Check(SelectionSnapshot.Create(new string('x', 2000), SelectionMethod.Automation, "range").State == SelectionState.Selected);
        Check(SelectionSnapshot.Create("", SelectionMethod.Automation, "caret").State == SelectionState.None);
        return Task.CompletedTask;
    }),
    ("无法识别的自绘控件不伪造精确控件身份", () =>
    {
        Check(!FocusObservation.Unavailable.HasIdentity);
        Check(!new FocusObservation(true, true, true, false, null, null, "root", "ControlType.Window").HasIdentity); return Task.CompletedTask;
    })
};
var allTests = tests.Concat(QuickTranslationChecks.Cases(target)).ToArray();
foreach (var test in allTests) { await test.Run(); Console.WriteLine("PASS " + test.Name); }
Console.WriteLine($"{allTests.Length} checks passed.");

static void Check(bool condition) { if (!condition) throw new InvalidOperationException("Assertion failed"); }

sealed class FakePlatform : IInsertionPlatform
{
    public bool Valid = true, Restored = true, Focused = true, KeysReleased = true, ThrowOnPaste;
    public bool SelectionValid = true;
    public uint Sequence = 11;
    public InjectionReceipt Receipt = new(4, 4);
    public int PasteCalls, RestoreCalls, Writes;
    public string? Written, Pasted;
    public Task<bool>? Gate;
    public Action? OnVerify;
    public bool IsSessionValid(TargetSession target) => Valid;
    public async Task<bool> WaitForKeysReleasedAsync(CancellationToken cancellationToken)
    { cancellationToken.ThrowIfCancellationRequested(); return Gate is null ? KeysReleased : await Gate.WaitAsync(cancellationToken); }
    public Task<bool> RestoreTargetAsync(TargetSession target, CancellationToken cancellationToken)
    { RestoreCalls++; return Task.FromResult(Restored); }
    public Task<uint> WriteClipboardAsync(string text, CancellationToken cancellationToken)
    { Writes++; Written = text; return Task.FromResult(11u); }
    public Task<bool> PrepareSelectionAsync(TargetSession target, CancellationToken cancellationToken) => Task.FromResult(SelectionValid);
    public Task<bool> VerifyTargetAsync(TargetSession target, CancellationToken cancellationToken) { OnVerify?.Invoke(); return Task.FromResult(Focused); }
    public uint ClipboardSequence => Sequence;
    public InjectionReceipt Paste()
    { PasteCalls++; Pasted = Written; if (ThrowOnPaste) throw new InvalidOperationException(); return Receipt; }
}
