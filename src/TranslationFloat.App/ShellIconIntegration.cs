using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;

namespace TranslationFloat.App;

internal static class ShellIconIntegration
{
    internal const string AppId = "Suiyi.Desktop";
    internal static readonly Guid PropertyGroup = new("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3");
    internal static readonly Guid ShellLinkClass = new("00021401-0000-0000-C000-000000000046");

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    internal struct PropertyKey(uint id) { public Guid Format = PropertyGroup; public uint Id = id; }

    [StructLayout(LayoutKind.Explicit, Size = 24)]
    internal struct PropertyValue : IDisposable
    {
        [FieldOffset(0)] private ushort _type;
        [FieldOffset(8)] private nint _pointer;
        public static PropertyValue String(string value) => new() { _type = 31, _pointer = Marshal.StringToCoTaskMemUni(value) };
        public string? Text => _type == 31 ? Marshal.PtrToStringUni(_pointer) : null;
        public void Dispose() => PropVariantClear(ref this);
    }

    [ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IPropertyStore
    {
        [PreserveSig] int GetCount(out uint count);
        [PreserveSig] int GetAt(uint index, out PropertyKey key);
        [PreserveSig] int GetValue(ref PropertyKey key, out PropertyValue value);
        [PreserveSig] int SetValue(ref PropertyKey key, ref PropertyValue value);
        [PreserveSig] int Commit();
    }

    [ComImport, Guid("000214F9-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IShellLink
    {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder file, int count, nint findData, uint flags);
        void GetIDList(out nint idList);
        void SetIDList(nint idList);
        void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder description, int count);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string description);
        void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder directory, int count);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string directory);
        void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder arguments, int count);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string arguments);
        void GetHotkey(out ushort hotkey);
        void SetHotkey(ushort hotkey);
        void GetShowCmd(out int command);
        void SetShowCmd(int command);
        void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder path, int count, out int index);
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string path, int index);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string path, uint reserved);
        void Resolve(nint window, uint flags);
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string path);
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern int SetCurrentProcessExplicitAppUserModelID(string id);
    [DllImport("shell32.dll")] private static extern int SHGetPropertyStoreForWindow(nint window, ref Guid id, [MarshalAs(UnmanagedType.Interface)] out IPropertyStore store);
    [DllImport("ole32.dll")] private static extern int PropVariantClear(ref PropertyValue value);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern void SHChangeNotify(int change, uint flags, string item, nint other);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern uint GetLongPathName(string shortPath, StringBuilder longPath, uint size);

    public static void SetProcessIdentity() => Marshal.ThrowExceptionForHR(SetCurrentProcessExplicitAppUserModelID(AppId));

    public static void ApplyWindow(nint window, string command, string iconPath)
    {
        var store = WindowStore(window);
        try
        {
            Set(store, 2, command); Set(store, 3, iconPath + ",0"); Set(store, 4, "随译"); Set(store, 5, AppId);
        }
        finally { Marshal.FinalReleaseComObject(store); }
    }

    public static void ClearWindow(nint window)
    {
        var store = WindowStore(window);
        try
        {
            foreach (var id in new uint[] { 2, 3, 4, 5 })
            { var key = new PropertyKey(id); var value = new PropertyValue(); Marshal.ThrowExceptionForHR(store.SetValue(ref key, ref value)); }
        }
        finally { Marshal.FinalReleaseComObject(store); }
    }

    internal static IPropertyStore WindowStore(nint window)
    {
        var id = typeof(IPropertyStore).GUID;
        Marshal.ThrowExceptionForHR(SHGetPropertyStoreForWindow(window, ref id, out var store));
        return store;
    }

    internal static void Set(IPropertyStore store, uint id, string text)
    {
        var key = new PropertyKey(id); var value = PropertyValue.String(text);
        try { Marshal.ThrowExceptionForHR(store.SetValue(ref key, ref value)); }
        finally { value.Dispose(); }
    }

    internal static object CreateLink() => Activator.CreateInstance(Type.GetTypeFromCLSID(ShellLinkClass)!)!;

    internal static bool SameFilePath(string first, string second)
    {
        first = Environment.ExpandEnvironmentVariables(first); second = Environment.ExpandEnvironmentVariables(second);
        if (string.Equals(first, second, StringComparison.OrdinalIgnoreCase)) return true;
        if (!Path.IsPathFullyQualified(first) || !Path.IsPathFullyQualified(second)
            || !string.Equals(Path.GetPathRoot(first), Path.GetPathRoot(second), StringComparison.OrdinalIgnoreCase)
            || first.StartsWith(@"\\", StringComparison.Ordinal)) return false;
        var firstName = Path.GetFileName(first); var secondName = Path.GetFileName(second);
        if (!firstName.Equals(secondName, StringComparison.OrdinalIgnoreCase) && !firstName.Contains('~') && !secondName.Contains('~')) return false;
        static string Expand(string path)
        {
            var buffer = new StringBuilder(32768);
            return GetLongPathName(path, buffer, (uint)buffer.Capacity) > 0 ? buffer.ToString() : Path.GetFullPath(path);
        }
        return string.Equals(Expand(first), Expand(second), StringComparison.OrdinalIgnoreCase);
    }

    public static IEnumerable<string> OwnShortcutCandidates()
    {
        var start = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "随译.lnk");
        if (File.Exists(start)) yield return start;
        var pinned = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), @"Microsoft\Internet Explorer\Quick Launch\User Pinned\TaskBar");
        if (Directory.Exists(pinned)) foreach (var file in Directory.EnumerateFiles(pinned, "*.lnk")) yield return file;
    }

    public static void RefreshShortcuts(IEnumerable<string> candidates, string executable, string iconPath)
    {
        foreach (var path in candidates)
        {
            try
            {
                var link = CreateLink();
                try
                {
                    var persist = (IPersistFile)link; persist.Load(path, 2);
                    var shell = (IShellLink)link;
                    var target = new StringBuilder(32768); shell.GetPath(target, target.Capacity, 0, 4);
                    if (!SameFilePath(target.ToString(), executable)) continue;
                    shell.SetIconLocation(iconPath, 0);
                    var store = (IPropertyStore)link; Set(store, 5, AppId); Marshal.ThrowExceptionForHR(store.Commit());
                    persist.Save(path, true);
                }
                finally { Marshal.FinalReleaseComObject(link); }
                SHChangeNotify(0x00002000, 0x1005, path, 0); // UPDATEITEM, PATHW | FLUSH. No global cache purge.
            }
            catch (Exception error) when (error is COMException or IOException or UnauthorizedAccessException)
            { DiagnosticLog.Write("theme-shortcut", error.GetType().Name); }
        }
    }
}
