using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Automation;
using System.Windows.Automation.Text;
using TranslationFloat.Core;

namespace TranslationFloat.App;

internal static class FocusInspector
{
    // Runs only inside an isolated, short-lived child. A stuck provider can be killed.
    internal static void Run(string rootArgument, bool readSelection)
    {
        var observation = FocusObservation.Unavailable;
        if (long.TryParse(rootArgument, NumberStyles.Integer, CultureInfo.InvariantCulture, out var root))
            observation = Task.Run(() => Inspect((nint)root, readSelection)).GetAwaiter().GetResult();
        using var writer = new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false));
        writer.Write(JsonSerializer.Serialize(observation));
    }

    private static FocusObservation Inspect(nint expectedRoot, bool readSelection)
    {
        try
        {
            var focused = AutomationElement.FocusedElement;
            if (focused is null) return FocusObservation.Unavailable;
            var current = focused.Current;
            var belongs = false;
            var ancestor = focused;
            for (var depth = 0; ancestor is not null && depth < 32; depth++)
            {
                var handle = (nint)ancestor.Current.NativeWindowHandle;
                if (handle != 0 && NativeMethods.GetAncestor(handle, 2) == expectedRoot) { belongs = true; break; }
                ancestor = TreeWalker.RawViewWalker.GetParent(ancestor);
            }
            bool? readOnly = null;
            bool? editable = null;
            if (focused.TryGetCurrentPattern(ValuePattern.Pattern, out var value))
            {
                readOnly = ((ValuePattern)value).Current.IsReadOnly;
                editable = !readOnly.Value;
            }
            else if (focused.TryGetCurrentPattern(TextPattern.Pattern, out var text))
            {
                var attribute = ((TextPattern)text).DocumentRange.GetAttributeValue(TextPattern.IsReadOnlyAttribute);
                if (attribute is bool isReadOnly) { readOnly = isReadOnly; editable = !isReadOnly; }
            }
            var kind = current.ControlType;
            if (editable is null && kind != ControlType.Edit && kind != ControlType.Document && kind != ControlType.Custom &&
                kind != ControlType.Pane && kind != ControlType.Window && kind != ControlType.ComboBox)
                editable = false;
            var runtimeId = focused.GetRuntimeId();
            var identity = runtimeId is { Length: > 0 and <= 64 }
                ? string.Join(",", runtimeId.Select(number => number.ToString(CultureInfo.InvariantCulture))) : null;
            var result = new FocusObservation(true, belongs, current.IsEnabled, current.IsPassword, readOnly, editable, identity, kind.ProgrammaticName);
            if (readSelection && result.HasIdentity)
                result = result with { Selection = ReadSelection(focused) };
            return result;
        }
        catch (Exception) { return FocusObservation.Unavailable; }
    }

    private static SelectionSnapshot ReadSelection(AutomationElement focused)
    {
        try
        {
            if (!focused.TryGetCurrentPattern(TextPattern.Pattern, out var pattern)) return SelectionSnapshot.Unavailable;
            var text = (TextPattern)pattern;
            if (text.SupportedTextSelection == SupportedTextSelection.None) return SelectionSnapshot.Unavailable;
            var ranges = text.GetSelection();
            if (ranges.Length == 0) return SelectionSnapshot.None;
            if (ranges.Length != 1) return new(SelectionState.Multiple);
            var range = ranges[0];
            if (range.CompareEndpoints(TextPatternRangeEndpoint.Start, range, TextPatternRangeEndpoint.End) == 0)
                return SelectionSnapshot.None;
            var selected = range.GetText(SelectionSnapshot.MaxLength + 1);
            if (selected.Length > SelectionSnapshot.MaxLength) return new(SelectionState.TooLong);
            // A digest of text and screen ranges detects a changed selection without
            // reading the surrounding document or retaining live provider objects.
            var rectangles = range.GetBoundingRectangles();
            if (rectangles.Length == 0) return SelectionSnapshot.Unavailable;
            var location = string.Join(",", rectangles.SelectMany(rect => new[] { rect.X, rect.Y, rect.Width, rect.Height })
                .Select(value => Math.Round(value, 1).ToString(CultureInfo.InvariantCulture)));
            return SelectionSnapshot.Create(selected, SelectionMethod.Automation, location);
        }
        catch (Exception) { return SelectionSnapshot.Unavailable; }
    }
}
