using System.Security.Cryptography;
using System.Text;

namespace TranslationFloat.Core;

public enum SelectionState { Unavailable, None, Selected, TooLong, Multiple }
public enum SelectionMethod { Automation, NativeEdit }

// The session retains a digest and location, never the selected source text.
public sealed record SelectionBinding(SelectionMethod Method, string Fingerprint, uint Start = 0, uint End = 0);

public sealed record SelectionSnapshot(SelectionState State, string? Text = null, SelectionBinding? Binding = null)
{
    public const int MaxLength = 2000;
    public static SelectionSnapshot Unavailable { get; } = new(SelectionState.Unavailable);
    public static SelectionSnapshot None { get; } = new(SelectionState.None);

    public static SelectionSnapshot Create(string text, SelectionMethod method, string location, uint start = 0, uint end = 0)
    {
        if (text.Length == 0) return None;
        if (text.Length > MaxLength) return new(SelectionState.TooLong);
        var digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text + "\0" + location)));
        return new(SelectionState.Selected, text, new(method, digest, start, end));
    }

    public override string ToString() => $"SelectionSnapshot {{ State = {State} }}";
}
