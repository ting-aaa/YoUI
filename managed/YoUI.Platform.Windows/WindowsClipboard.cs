using System.Runtime.InteropServices;

namespace YoUI.Platform.Windows;

internal sealed class WindowsClipboard(Func<nint> window) : IClipboard
{
    public string GetText()
    {
        if (!OpenClipboard(window())) return "";
        try
        {
            nint data = GetClipboardData(13); if (data == 0) return "";
            nint ptr = GlobalLock(data); if (ptr == 0) return "";
            try { return Marshal.PtrToStringUni(ptr) ?? ""; } finally { GlobalUnlock(data); }
        }
        finally { CloseClipboard(); }
    }
    public void SetText(string text)
    {
        if (!OpenClipboard(window())) return;
        nint memory = 0;
        try
        {
            var chars = (text + '\0').ToCharArray(); memory = GlobalAlloc(2, (nuint)(chars.Length * 2)); if (memory == 0) return;
            nint ptr = GlobalLock(memory); if (ptr == 0) return;
            try { Marshal.Copy(chars, 0, ptr, chars.Length); } finally { GlobalUnlock(memory); }
            if (EmptyClipboard() && SetClipboardData(13, memory) != 0) memory = 0; // Ownership transfers to the OS.
        }
        finally { if (memory != 0) GlobalFree(memory); CloseClipboard(); }
    }
    [DllImport("user32")] private static extern bool OpenClipboard(nint hwnd);
    [DllImport("user32")] private static extern bool CloseClipboard();
    [DllImport("user32")] private static extern bool EmptyClipboard();
    [DllImport("user32")] private static extern nint GetClipboardData(uint format);
    [DllImport("user32")] private static extern nint SetClipboardData(uint format, nint data);
    [DllImport("kernel32")] private static extern nint GlobalAlloc(uint flags, nuint size);
    [DllImport("kernel32")] private static extern nint GlobalLock(nint data);
    [DllImport("kernel32")] private static extern bool GlobalUnlock(nint data);
    [DllImport("kernel32")] private static extern nint GlobalFree(nint data);
}
