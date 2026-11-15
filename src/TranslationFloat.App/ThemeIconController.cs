using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Interop;

namespace TranslationFloat.App;

internal sealed class ThemeIconController : IDisposable
{
    private readonly ThemeManager _theme;
    private readonly ThemeIconFactory _factory = new();
    private readonly Action<System.Drawing.Icon> _tray;
    private readonly string _cacheRoot, _executable, _command;
    private readonly Func<IEnumerable<string>> _shortcuts;
    private readonly Dictionary<Window, (EventHandler Init, CancelEventHandler Closing, EventHandler Closed)> _windows = new();
    private ThemedIcons _current;
    public System.Drawing.Icon TrayIcon => _current.TrayIcon;
    internal string? IconPath { get; private set; }

    public ThemeIconController(ThemeManager theme, Action<System.Drawing.Icon> tray, string? cacheRoot = null,
        string? executable = null, Func<IEnumerable<string>>? shortcuts = null)
    {
        _theme = theme; _tray = tray;
        _cacheRoot = cacheRoot ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TranslationFloat", "Icons");
        _executable = executable ?? Environment.ProcessPath ?? throw new InvalidOperationException("Missing executable path");
        _command = "\"" + _executable + "\"";
        if (Path.GetFileNameWithoutExtension(_executable).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
            _command += " \"" + typeof(App).Assembly.Location + "\"";
        _shortcuts = shortcuts ?? ShellIconIntegration.OwnShortcutCandidates;
        _current = _factory.Create(theme.Appearance);
        RefreshShellCache();
        theme.Changed += Changed;
    }

    public void Track(Window window)
    {
        if (_windows.ContainsKey(window)) return;
        window.Icon = _current.WindowImage;
        EventHandler initialized = (_, _) => UpdateWindow(window);
        CancelEventHandler closing = (_, args) =>
        {
            ClearWindow(window);
            if (args.Cancel) window.Dispatcher.BeginInvoke(() => { if (_windows.ContainsKey(window)) UpdateWindow(window); });
        };
        EventHandler closed = (_, _) => Untrack(window);
        _windows.Add(window, (initialized, closing, closed));
        window.SourceInitialized += initialized; window.Closing += closing; window.Closed += closed;
        UpdateWindow(window);
    }

    private void Changed()
    {
        var next = _factory.Create(_theme.Appearance);
        var previous = _current;
        try
        {
            _tray(next.TrayIcon);
            _current = next;
            foreach (var window in _windows.Keys) window.Icon = next.WindowImage;
            RefreshShellCache();
        }
        catch { if (ReferenceEquals(_current, previous)) next.Dispose(); throw; }
        finally { if (!ReferenceEquals(_current, previous)) previous.Dispose(); }
    }

    private void RefreshShellCache()
    {
        IconPath = null;
        try
        {
            Directory.CreateDirectory(_cacheRoot);
            var hash = Convert.ToHexString(SHA256.HashData(_current.WindowBytes))[..12];
            var path = Path.Combine(_cacheRoot, $"{_theme.Selected.Id}-{(_theme.IsDark ? "dark" : "light")}-{hash}.ico");
            if (!File.Exists(path) || new FileInfo(path).Length != _current.WindowBytes.Length
                || !File.ReadAllBytes(path).AsSpan().SequenceEqual(_current.WindowBytes))
            {
                var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
                try { File.WriteAllBytes(temporary, _current.WindowBytes); File.Move(temporary, path, true); }
                finally { if (File.Exists(temporary)) File.Delete(temporary); }
            }
            IconPath = path;
            foreach (var window in _windows.Keys) UpdateWindow(window);
            ShellIconIntegration.RefreshShortcuts(_shortcuts(), _executable, path);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException)
        {
            foreach (var window in _windows.Keys) ClearWindow(window);
            DiagnosticLog.Write("theme-icon-cache", error.GetType().Name);
        }
    }

    private void UpdateWindow(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == 0 || IconPath is null) return;
        try { ShellIconIntegration.ApplyWindow(handle, _command, IconPath); }
        catch (System.Runtime.InteropServices.COMException error) { DiagnosticLog.Write("theme-taskbar", error.GetType().Name); }
    }

    private static void ClearWindow(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == 0) return;
        try { ShellIconIntegration.ClearWindow(handle); }
        catch (System.Runtime.InteropServices.COMException error) { DiagnosticLog.Write("theme-taskbar-clear", error.GetType().Name); }
    }

    private void Untrack(Window window)
    {
        if (!_windows.Remove(window, out var handlers)) return;
        window.SourceInitialized -= handlers.Init; window.Closing -= handlers.Closing; window.Closed -= handlers.Closed;
    }

    public void Dispose()
    {
        _theme.Changed -= Changed;
        foreach (var window in _windows.Keys.ToArray()) { ClearWindow(window); Untrack(window); }
        _current.Dispose();
    }
}
