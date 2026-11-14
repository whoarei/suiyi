namespace TranslationFloat.Core;

// Only control metadata crosses the helper boundary; never text or accessible names.
public sealed record FocusObservation(
    bool Available, bool BelongsToTarget, bool Enabled, bool Password,
    bool? ReadOnly, bool? Editable, string? FocusId, string ControlKind)
{
    public static FocusObservation Unavailable { get; } = new(false, false, false, false, null, null, null, "Unknown");
    public bool Reject => Available && (!BelongsToTarget || !Enabled || Password || ReadOnly == true || Editable == false);
    public bool HasIdentity => Available && !Reject && Editable == true && !string.IsNullOrEmpty(FocusId);
    public bool Matches(string capturedFocusId) => HasIdentity && FocusId == capturedFocusId;
}

public static class TargetPresentation
{
    public static string AppName(string processName) => processName.ToLowerInvariant() switch
    {
        "weixin" or "wechat" => "微信",
        "notepad" => "记事本",
        "chrome" => "Google Chrome",
        "msedge" => "Microsoft Edge",
        "firefox" => "Firefox",
        "code" => "Visual Studio Code",
        "winword" => "Word",
        "excel" => "Excel",
        "translationfloat.testtarget" => "测试编辑器",
        _ => processName
    };
}
