using System;
using System.IO;
using System.Security;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;

namespace TranslationFloat.App;

internal sealed class ThemeManager : IDisposable
{
    private readonly ResourceDictionary _resources;
    private readonly ThemeSettingsStore _store;
    private readonly Func<bool> _systemDark;
    private readonly Dispatcher _dispatcher = Dispatcher.CurrentDispatcher;
    private readonly bool _watch;
    private bool _disposed;
    public ThemePalette Selected { get; private set; }
    public bool IsDark { get; private set; }
    public ThemeAppearance Appearance { get; private set; }
    public event Action? Changed;

    public ThemeManager(ResourceDictionary resources, ThemeSettingsStore? store = null, Func<bool>? systemDark = null, bool watchSystem = true)
    {
        _resources = resources; _store = store ?? new(); _systemDark = systemDark ?? (() => ReadSystemDark());
        Selected = ThemePalette.Get(_store.Load()); IsDark = _systemDark();
        Appearance = new(Selected, IsDark);
        Apply();
        if (watchSystem)
        {
            try { SystemEvents.UserPreferenceChanged += SystemPreferenceChanged; _watch = true; }
            catch (Exception error) when (error is InvalidOperationException or System.Runtime.InteropServices.ExternalException)
            { DiagnosticLog.Write("theme-monitor", error.GetType().Name); }
        }
    }

    public void Select(ThemeColor color)
    {
        var next = ThemePalette.Get(color);
        _store.Save(color);
        Selected = next;
        Apply();
    }

    public void RefreshSystemTheme()
    {
        if (_disposed) return;
        var dark = _systemDark();
        if (IsDark == dark) return;
        IsDark = dark;
        Apply();
    }

    private void Apply()
    {
        Appearance = new(Selected, IsDark);
        foreach (var pair in Appearance.Colors)
        {
            var brush = new SolidColorBrush(pair.Value); brush.Freeze();
            _resources["Theme." + pair.Key] = brush;
        }
        Changed?.Invoke();
    }

    private void SystemPreferenceChanged(object? sender, UserPreferenceChangedEventArgs e)
    {
        if (_disposed || _dispatcher.HasShutdownStarted) return;
        _dispatcher.BeginInvoke(() => { if (!_disposed) RefreshSystemTheme(); });
    }

    internal static bool ReadSystemDark(string path = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize")
    {
        try { using var key = Registry.CurrentUser.OpenSubKey(path); return key?.GetValue("AppsUseLightTheme") is int value && value == 0; }
        catch (Exception error) when (error is SecurityException or UnauthorizedAccessException or IOException) { return false; }
    }

    public void Dispose()
    { _disposed = true; if (_watch) SystemEvents.UserPreferenceChanged -= SystemPreferenceChanged; }
}
