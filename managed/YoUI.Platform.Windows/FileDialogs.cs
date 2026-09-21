using System.ComponentModel;
using System.Runtime.InteropServices;

namespace YoUI.Platform.Windows;

/// <summary>Platform-only document dialogs. Cancellation is distinct from a dialog failure.</summary>
public static class FileDialogs
{
    public static string? Open(nint owner, string title, string description = "YoUI configuration", string pattern = "*.youi.json;*.json") => Choose(owner, title, description, pattern, "", null, false);
    public static string? Save(nint owner, string title, string fileName, string extension = "json", string description = "YoUI configuration", string pattern = "*.youi.json;*.json") => Choose(owner, title, description, pattern, extension, fileName, true);
    private static unsafe string? Choose(nint owner, string title, string description, string pattern, string extension, string? name, bool save)
    {
        if (name?.Length >= 32768) throw new ArgumentException("File name is too long.");
        nint buffer = Marshal.AllocHGlobal(32768 * sizeof(char));
        try
        {
            var chars = new Span<char>((void*)buffer, 32768); chars.Clear(); name.AsSpan().CopyTo(chars);
            var data = new OpenFileName { Size = Marshal.SizeOf<OpenFileName>(), Owner = owner, Filter = description + "\0" + pattern + "\0All files\0*.*\0\0", File = buffer, MaxFile = 32768, Title = title, DefaultExtension = extension, FilterIndex = 1, Flags = 0x80000 | 0x8 | 0x800 | (save ? 0x2 : 0x1000) };
            bool ok = save ? GetSaveFileName(ref data) : GetOpenFileName(ref data);
            if (ok) return Marshal.PtrToStringUni(buffer); uint error = CommDlgExtendedError(); if (error != 0) throw new Win32Exception((int)error, "File dialog failed."); return null;
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }
    /// <returns>6 = save, 7 = discard, 2 = cancel.</returns>
    public static int ConfirmUnsaved(nint owner) => MessageBox(owner, "Save your changes before continuing?", "YoUI Editor · Unsaved changes", 0x3 | 0x20);
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct OpenFileName
    {
        public int Size; public nint Owner, Instance; public string? Filter; public nint CustomFilter; public int MaxCustomFilter, FilterIndex;
        public nint File; public int MaxFile; public nint FileTitle; public int MaxFileTitle; public string? InitialDirectory, Title;
        public int Flags; public short FileOffset, FileExtension; public string? DefaultExtension; public nint CustomData, Hook, Template, Reserved;
        public int ReservedValue, FlagsEx;
    }
    [DllImport("comdlg32.dll", EntryPoint = "GetOpenFileNameW", CharSet = CharSet.Unicode, SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetOpenFileName(ref OpenFileName file);
    [DllImport("comdlg32.dll", EntryPoint = "GetSaveFileNameW", CharSet = CharSet.Unicode, SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetSaveFileName(ref OpenFileName file);
    [DllImport("comdlg32.dll")] private static extern uint CommDlgExtendedError();
    [DllImport("user32.dll", EntryPoint = "MessageBoxW", CharSet = CharSet.Unicode)] private static extern int MessageBox(nint owner, string text, string caption, uint type);
}
