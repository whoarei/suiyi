using System;
using System.IO;
using System.Text.Json;

namespace TranslationFloat.App;

internal static class DiagnosticLog
{
    private static readonly string Folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TranslationFloat");
    internal static void Write(string stage, string outcome, string? process = null, nint window = 0, nint focus = 0)
    {
        try
        {
            Directory.CreateDirectory(Folder);
            var path = Path.Combine(Folder, "m0.log");
            if (File.Exists(path) && new FileInfo(path).Length > 256 * 1024) File.Move(path, path + ".previous", true);
            var entry = new { time = DateTimeOffset.Now, stage, outcome, process, window = window.ToInt64(), focus = focus.ToInt64() };
            File.AppendAllText(path, JsonSerializer.Serialize(entry) + Environment.NewLine);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
