using System.Text.Json;
using TranslationFloat.Core;

// Explicit real-release download verification; no installer execution or credentials.
if (args is ["--download-release", var cacheRoot])
{
    using var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false }) { Timeout = Timeout.InfiniteTimeSpan };
    ReleaseVersion.TryParse("0.0.0", out var current);
    var update = await new GitHubUpdateChecker(client).CheckAsync(current);
    if (update.Installer is null) throw new InvalidOperationException("Release has no verifiable installer");
    var file = await new InstallerDownload(client).DownloadAsync(update.Installer, cacheRoot);
    Console.WriteLine(JsonSerializer.Serialize(new { file.Path, file.Sha256, update.Installer.Version, update.Installer.Size }));
    return 0;
}

// Isolated fixture: owns only a smoke-test mutex/event and locks a fixture DLL.
if (args.Length != 4 || !args[0].StartsWith("Local\\Suiyi.Installer.Smoke.", StringComparison.Ordinal)
    || !args[1].StartsWith(args[0] + ".", StringComparison.Ordinal)) return 2;
using var mutex = new Mutex(true, args[0]);
using var exit = new EventWaitHandle(false, EventResetMode.AutoReset, args[1]);
using var lockedFile = new FileStream(args[2], FileMode.Open, FileAccess.Read, FileShare.Read);
File.WriteAllText(args[3], "ready");
return exit.WaitOne(TimeSpan.FromSeconds(60)) ? 0 : 3;
