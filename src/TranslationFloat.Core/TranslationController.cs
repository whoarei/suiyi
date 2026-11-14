namespace TranslationFloat.Core;

public enum TranslationPhase { Empty, NotConfigured, Composing, Waiting, Translating, Ready, EditedReady, Error }

public sealed record TranslationSnapshot(string Source, string Result, TranslationPhase Phase, string Message)
{
    public bool CanOutput => !string.IsNullOrWhiteSpace(Source) && !string.IsNullOrWhiteSpace(Result) &&
        Phase is TranslationPhase.Ready or TranslationPhase.EditedReady;
    public bool CanEdit => Phase is TranslationPhase.Ready or TranslationPhase.EditedReady;
}

public sealed class TranslationController : IDisposable
{
    private readonly object _gate = new();
    private readonly TimeSpan _debounce, _minInterval;
    private readonly Dictionary<string, (string Result, DateTimeOffset Time)> _cache = new(StringComparer.Ordinal);
    private readonly Queue<string> _cacheOrder = new();
    private TranslationSnapshot _state = new("", "", TranslationPhase.Empty, "输入中文，停顿后自动翻译。");
    private ITranslationProvider? _provider;
    private CancellationTokenSource? _pending;
    private long _generation;
    private bool _composing, _suspended = true, _disposed;
    private DateTimeOffset _lastRequest, _cooldown;

    public TranslationController(TimeSpan? debounce = null, TimeSpan? minInterval = null)
    { _debounce = debounce ?? TimeSpan.FromMilliseconds(450); _minInterval = minInterval ?? TimeSpan.FromMilliseconds(500); }

    public event Action? Changed;
    public TranslationSnapshot State { get { lock (_gate) return _state; } }

    public void Configure(ITranslationProvider? provider)
    {
        lock (_gate)
        {
            _provider = provider; _cache.Clear(); _cacheOrder.Clear(); _cooldown = default;
            _state = _state with { Result = "" };
        }
        Schedule();
    }

    public void UpdateSource(string source)
    {
        lock (_gate)
        {
            if (source == _state.Source) return;
            _state = _state with { Source = source };
        }
        Schedule();
    }

    public void SetComposing(bool composing)
    {
        lock (_gate) { if (_composing == composing) return; _composing = composing; }
        Schedule();
    }

    public void EditResult(string result)
    {
        lock (_gate)
        {
            if (!_state.CanEdit || _disposed) return;
            CancelLocked();
            _state = _state with { Result = result, Phase = TranslationPhase.EditedReady, Message = "译文已手动修改。" };
        }
        Changed?.Invoke();
    }

    public void Resume()
    {
        lock (_gate) { _suspended = false; if (_state.CanOutput) return; }
        Schedule();
    }

    public void Suspend()
    {
        lock (_gate)
        {
            _suspended = true; CancelLocked();
            if (_state.Phase is TranslationPhase.Translating or TranslationPhase.Waiting or TranslationPhase.Composing)
                _state = _state with { Phase = TranslationPhase.Waiting, Message = "草稿已保留，重新唤出后继续翻译。" };
            _composing = false;
        }
        Changed?.Invoke();
    }

    public void Retry() => Schedule(immediate: true);

    private void CancelLocked()
    {
        _generation++;
        _pending?.Cancel();
        _pending = null;
    }

    private void Schedule(bool immediate = false)
    {
        long generation;
        CancellationTokenSource? pending = null;
        ITranslationProvider? provider = null;
        string source = "";
        lock (_gate)
        {
            if (_disposed) return;
            CancelLocked(); generation = _generation;
            if (string.IsNullOrWhiteSpace(_state.Source))
                _state = new(_state.Source, "", TranslationPhase.Empty, "输入中文，停顿后自动翻译。");
            else if (_provider is null)
                _state = _state with { Phase = TranslationPhase.NotConfigured, Message = "请先设置翻译接口和模型。" };
            else if (_composing)
                _state = _state with { Phase = TranslationPhase.Composing, Message = "正在选词，输入完成后开始翻译。" };
            else if (_suspended)
                _state = _state with { Phase = TranslationPhase.Waiting, Message = "草稿已保留，重新唤出后继续翻译。" };
            else if (_cache.TryGetValue(_state.Source, out var cached) && DateTimeOffset.UtcNow - cached.Time < TimeSpan.FromMinutes(10))
                _state = _state with { Result = cached.Result, Phase = TranslationPhase.Ready, Message = "译文已更新。" };
            else
            {
                _state = _state with { Phase = TranslationPhase.Waiting, Message = "等待输入停顿…" };
                _pending = pending = new CancellationTokenSource();
                provider = _provider; source = _state.Source;
            }
        }
        Changed?.Invoke();
        if (pending is not null && provider is not null)
            _ = RunAsync(generation, source, provider, pending, immediate);
    }

    private async Task RunAsync(long generation, string source, ITranslationProvider provider, CancellationTokenSource pending, bool immediate)
    {
        try
        {
            await Task.Delay(immediate ? TimeSpan.Zero : _debounce, pending.Token);
            TimeSpan throttle;
            lock (_gate)
            {
                var now = DateTimeOffset.UtcNow;
                var next = _lastRequest + _minInterval;
                if (_cooldown > next) next = _cooldown;
                throttle = next - now;
            }
            if (throttle > TimeSpan.Zero) await Task.Delay(throttle, pending.Token);
            lock (_gate)
            {
                if (generation != _generation || pending.IsCancellationRequested || _disposed) return;
                _lastRequest = DateTimeOffset.UtcNow;
                _state = _state with { Phase = TranslationPhase.Translating, Message = "正在翻译…" };
            }
            Changed?.Invoke();
            var result = await provider.TranslateAsync(new(source), pending.Token);
            lock (_gate)
            {
                if (generation != _generation || pending.IsCancellationRequested || _disposed) return;
                if (string.IsNullOrWhiteSpace(result)) throw new TranslationException(TranslationFailure.InvalidResponse);
                if (!_cache.ContainsKey(source)) _cacheOrder.Enqueue(source);
                _cache[source] = (result, DateTimeOffset.UtcNow);
                while (_cache.Count > 128 && _cacheOrder.TryDequeue(out var oldest)) _cache.Remove(oldest);
                _state = _state with { Result = result, Phase = TranslationPhase.Ready, Message = "译文已更新，可直接编辑。" };
            }
            Changed?.Invoke();
        }
        catch (OperationCanceledException) when (pending.IsCancellationRequested) { }
        catch (Exception error)
        {
            lock (_gate)
            {
                if (generation != _generation || pending.IsCancellationRequested || _disposed) return;
                if (error is TranslationException { Failure: TranslationFailure.RateLimit } limited)
                    _cooldown = DateTimeOffset.UtcNow + (limited.RetryAfter ?? TimeSpan.FromSeconds(5));
                _state = _state with { Phase = TranslationPhase.Error, Message = error is TranslationException known ? known.Message : "翻译失败，请重试。" };
            }
            Changed?.Invoke();
        }
        finally
        {
            lock (_gate) { if (ReferenceEquals(_pending, pending)) _pending = null; }
            pending.Dispose();
        }
    }

    public void Dispose()
    {
        lock (_gate) { _disposed = true; CancelLocked(); _cache.Clear(); _cacheOrder.Clear(); }
    }
}
