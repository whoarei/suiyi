using System;
using System.IO;
using System.Reflection;
using Microsoft.Win32;

namespace TranslationFloat.App;

internal interface IStartupStore
{
    bool? ReadPreference();
    void WritePreference(bool? enabled);
    string? ReadCommand();
    void WriteCommand(string? command);
}

internal sealed class StartupRegistration(IStartupStore store, string command)
{
    public bool IsEnabled => string.Equals(store.ReadCommand(), command, StringComparison.OrdinalIgnoreCase);

    public static StartupRegistration ForCurrentProcess() => new(new RegistryStartupStore(),
        BuildCommand(Environment.ProcessPath ?? throw new InvalidOperationException("无法确认程序路径。"), Assembly.GetExecutingAssembly().Location));

    public static string BuildCommand(string executable, string? assembly = null)
    {
        static string QuotePath(string path)
        {
            if (!Path.IsPathFullyQualified(path) || path.IndexOfAny(['"', '\r', '\n', '\0']) >= 0)
                throw new ArgumentException("开机启动需要完整的程序路径。");
            return "\"" + path + "\"";
        }
        var result = QuotePath(executable);
        if (Path.GetFileNameWithoutExtension(executable).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
            result += " " + QuotePath(assembly ?? throw new ArgumentException("缺少程序程序集路径。"));
        // Run command lines are limited to 260 characters by Windows.
        if (result.Length > 260) throw new InvalidOperationException("开机启动路径过长，请把程序移到较短的固定目录。");
        return result;
    }

    public void Initialize() => SetEnabled(store.ReadPreference() ?? true);

    public void SetEnabled(bool enabled)
    {
        var previousCommand = store.ReadCommand();
        var previousPreference = store.ReadPreference();
        var desiredCommand = enabled ? command : null;
        var commandChanged = !string.Equals(previousCommand, desiredCommand, StringComparison.OrdinalIgnoreCase);
        var preferenceChanged = previousPreference != enabled;
        try
        {
            // Do not rewrite Run on every login when its command is already correct.
            if (commandChanged) store.WriteCommand(desiredCommand);
            if (preferenceChanged) store.WritePreference(enabled);
        }
        catch
        {
            // Keep preference and registration consistent if the second write fails.
            // Do not replace a different command installed concurrently by another process.
            if (commandChanged)
            {
                try
                {
                    if (string.Equals(store.ReadCommand(), desiredCommand, StringComparison.OrdinalIgnoreCase))
                        store.WriteCommand(previousCommand);
                }
                catch (Exception) { }
            }
            throw;
        }
    }
}

internal sealed class RegistryStartupStore(
    string runKeyPath = RegistryStartupStore.RunKeyPath,
    string preferencesKeyPath = RegistryStartupStore.PreferencesKeyPath) : IStartupStore
{
    internal const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    internal const string PreferencesKeyPath = @"Software\Suiyi";
    internal const string RunValueName = "Suiyi";
    internal const string PreferenceValueName = "AutoStartEnabled";

    public bool? ReadPreference()
    {
        using var key = Registry.CurrentUser.OpenSubKey(preferencesKeyPath);
        return key?.GetValue(PreferenceValueName) switch
        {
            null => null,
            0 => false,
            1 => true,
            _ => throw new InvalidDataException("开机启动偏好设置无效。")
        };
    }

    public void WritePreference(bool? enabled)
    {
        if (enabled is null)
        {
            using var existing = Registry.CurrentUser.OpenSubKey(preferencesKeyPath, true);
            existing?.DeleteValue(PreferenceValueName, false);
            return;
        }
        using var key = Registry.CurrentUser.CreateSubKey(preferencesKeyPath, true);
        key.SetValue(PreferenceValueName, enabled.Value ? 1 : 0, RegistryValueKind.DWord);
    }

    public string? ReadCommand()
    {
        using var key = Registry.CurrentUser.OpenSubKey(runKeyPath);
        return key?.GetValue(RunValueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames) switch
        {
            null => null,
            string value => value,
            _ => throw new InvalidDataException("开机启动注册项无效。")
        };
    }

    public void WriteCommand(string? command)
    {
        if (command is null)
        {
            using var existing = Registry.CurrentUser.OpenSubKey(runKeyPath, true);
            existing?.DeleteValue(RunValueName, false);
            return;
        }
        using var key = Registry.CurrentUser.CreateSubKey(runKeyPath, true);
        key.SetValue(RunValueName, command, RegistryValueKind.String);
    }
}
