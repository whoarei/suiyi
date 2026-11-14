using System.Text.RegularExpressions;

namespace TranslationFloat.Core;

public sealed class ReleaseVersion : IComparable<ReleaseVersion>
{
    private static readonly Regex Pattern = new(
        @"\A[vV]?(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)(?:-([0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*))?(?:\+([0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*))?\z",
        RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);

    private ReleaseVersion(Version number, string? prerelease)
    { Number = number; Prerelease = prerelease; }

    public Version Number { get; }
    public string? Prerelease { get; }
    public override string ToString() => Number + (Prerelease is null ? "" : "-" + Prerelease);

    public static bool TryParse(string? text, out ReleaseVersion version)
    {
        version = null!;
        if (text is null || text.Length > 256) return false;
        var match = Pattern.Match(text);
        if (!match.Success || !int.TryParse(match.Groups[1].Value, out var major)
            || !int.TryParse(match.Groups[2].Value, out var minor) || !int.TryParse(match.Groups[3].Value, out var patch)) return false;
        var prerelease = match.Groups[4].Success ? match.Groups[4].Value : null;
        if (prerelease?.Split('.').Any(part => Numeric(part) && part.Length > 1 && part[0] == '0') == true) return false;
        version = new ReleaseVersion(new Version(major, minor, patch), prerelease);
        return true;
    }

    public int CompareTo(ReleaseVersion? other)
    {
        if (other is null) return 1;
        var number = Number.CompareTo(other.Number);
        if (number != 0) return number;
        if (Prerelease is null) return other.Prerelease is null ? 0 : 1;
        if (other.Prerelease is null) return -1;
        var left = Prerelease.Split('.'); var right = other.Prerelease.Split('.');
        for (var index = 0; index < Math.Min(left.Length, right.Length); index++)
        {
            var leftNumeric = Numeric(left[index]); var rightNumeric = Numeric(right[index]);
            var result = leftNumeric && rightNumeric
                ? left[index].Length.CompareTo(right[index].Length)
                : leftNumeric != rightNumeric ? (leftNumeric ? -1 : 1) : 0;
            if (result == 0) result = string.Compare(left[index], right[index], StringComparison.Ordinal);
            if (result != 0) return result;
        }
        return left.Length.CompareTo(right.Length);
    }

    private static bool Numeric(string part) => part.All(character => character is >= '0' and <= '9');
}
