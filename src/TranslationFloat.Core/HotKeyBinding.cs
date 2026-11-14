namespace TranslationFloat.Core;

public sealed record HotKeyBinding(uint Modifiers, uint VirtualKey)
{
    public static HotKeyBinding Default { get; } = new(3, 0x54);
    public bool IsValid => (Modifiers & ~7u) == 0 && (Modifiers & 3) != 0 &&
        (VirtualKey is >= 0x30 and <= 0x39 or >= 0x41 and <= 0x5A or >= 0x70 and <= 0x7B);
    public string KeyName => VirtualKey is >= 0x70 and <= 0x7B ? "F" + (VirtualKey - 0x6F) : ((char)VirtualKey).ToString();
    public string DisplayName => ((Modifiers & 2) != 0 ? "Ctrl+" : "") +
        ((Modifiers & 1) != 0 ? "Alt+" : "") + ((Modifiers & 4) != 0 ? "Shift+" : "") + KeyName;
}
