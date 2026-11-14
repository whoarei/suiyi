using System;
using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using TranslationFloat.Core;

namespace TranslationFloat.App;

internal sealed class FocusProbeClient
{
    public async Task<FocusObservation> InspectAsync(nint root, CancellationToken cancellationToken, bool readSelection = false)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMilliseconds(900));
        using var child = new Process();
        try
        {
            var executable = Environment.ProcessPath;
            if (executable is null) return FocusObservation.Unavailable;
            child.StartInfo = new ProcessStartInfo(executable)
            {
                UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden,
                RedirectStandardOutput = true, RedirectStandardError = true
            };
            if (System.IO.Path.GetFileNameWithoutExtension(executable).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
                child.StartInfo.ArgumentList.Add(Assembly.GetExecutingAssembly().Location);
            child.StartInfo.ArgumentList.Add(readSelection ? "--inspect-selection" : "--inspect-focus");
            child.StartInfo.ArgumentList.Add(root.ToInt64().ToString(CultureInfo.InvariantCulture));
            child.StartInfo.Environment.Remove("DEEPSEEK_API_KEY");
            if (!child.Start()) return FocusObservation.Unavailable;
            var output = child.StandardOutput.ReadToEndAsync(timeout.Token);
            var errors = child.StandardError.ReadToEndAsync(timeout.Token);
            await child.WaitForExitAsync(timeout.Token);
            var json = await output;
            await errors;
            if (child.ExitCode != 0 || json.Length > 32768) return FocusObservation.Unavailable;
            return JsonSerializer.Deserialize<FocusObservation>(json) ?? FocusObservation.Unavailable;
        }
        catch (Exception) { return FocusObservation.Unavailable; }
        finally
        {
            try { if (child.Id != 0 && !child.HasExited) child.Kill(); }
            catch (InvalidOperationException) { }
            catch (System.ComponentModel.Win32Exception) { }
        }
    }
}
