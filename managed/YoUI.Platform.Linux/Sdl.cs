using System.Runtime.InteropServices;
using System.Text;

namespace YoUI.Platform.Linux;

// SDL2's stable C ABI, restricted to Linux LP64. No SDL renderer or UI framework is used.
internal static unsafe class Sdl
{
    private const string Library = "libSDL2-2.0.so.0";
    [StructLayout(LayoutKind.Sequential)] internal struct Version { public byte Major, Minor, Patch; }
    [StructLayout(LayoutKind.Explicit, Size = 72)] internal struct WindowInfo
    {
        [FieldOffset(0)] public Version Version;
        [FieldOffset(4)] public int Subsystem;
        [FieldOffset(8)] public nint Display;
        [FieldOffset(16)] public nuint Window;
    }
    [StructLayout(LayoutKind.Explicit, Size = 56)] internal struct Event
    {
        [FieldOffset(0)] public uint Type;
        [FieldOffset(8)] public uint WindowId;
        [FieldOffset(12)] public byte WindowEvent;
        [FieldOffset(16)] public int Data1;
        [FieldOffset(20)] public int Data2;
        [FieldOffset(20)] public int Key;
        [FieldOffset(24)] public ushort Modifiers;
        [FieldOffset(12)] public fixed byte Text[32];
        [FieldOffset(16)] public byte Button;
        [FieldOffset(20)] public int X;
        [FieldOffset(24)] public int Y;
        [FieldOffset(24)] public uint WheelDirection;
        [FieldOffset(32)] public float PreciseWheelY;
        [FieldOffset(8)] public nint DropText;
        [FieldOffset(16)] public nint ExtendedText;
        public string GetText()
        {
            fixed (byte* p = Text)
            {
                var bytes = new ReadOnlySpan<byte>(p, 32);
                int end = bytes.IndexOf((byte)0);
                return Encoding.UTF8.GetString(end < 0 ? bytes : bytes[..end]);
            }
        }
    }
    internal static string Error => Marshal.PtrToStringUTF8(GetError()) ?? "Unknown SDL error";
    internal static void Check(int result) { if (result < 0) throw new InvalidOperationException(Error); }
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern void SDL_GetVersion(out Version version);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern nint SDL_GetHint([MarshalAs(UnmanagedType.LPUTF8Str)] string name);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern int SDL_GetHintBoolean([MarshalAs(UnmanagedType.LPUTF8Str)] string name, int defaultValue);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern int SDL_SetHintWithPriority([MarshalAs(UnmanagedType.LPUTF8Str)] string name, [MarshalAs(UnmanagedType.LPUTF8Str)] string? value, int priority);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] private static extern nint SDL_GetError();
    private static nint GetError() => SDL_GetError();
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern uint SDL_WasInit(uint flags);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern int SDL_VideoInit([MarshalAs(UnmanagedType.LPUTF8Str)] string driver);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern void SDL_VideoQuit();
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern void SDL_EnableScreenSaver();
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern nint SDL_GetCurrentVideoDriver();
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern nint SDL_CreateWindow([MarshalAs(UnmanagedType.LPUTF8Str)] string title, int x, int y, int width, int height, uint flags);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern void SDL_DestroyWindow(nint window);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern void SDL_ShowWindow(nint window);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern uint SDL_GetWindowID(nint window);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern void SDL_GetWindowSize(nint window, out int width, out int height);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern void SDL_SetWindowMinimumSize(nint window, int width, int height);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern void SDL_SetWindowMaximumSize(nint window, int width, int height);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern int SDL_GetWindowWMInfo(nint window, ref WindowInfo info);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern int SDL_PollEvent(out Event value);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern int SDL_WaitEventTimeout(out Event value, int timeout);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern int SDL_CaptureMouse(int enabled);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern uint SDL_GetMouseState(out int x, out int y);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern ushort SDL_GetModState();
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern void SDL_StartTextInput();
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern void SDL_StopTextInput();
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern nint SDL_GetClipboardText();
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern int SDL_SetClipboardText([MarshalAs(UnmanagedType.LPUTF8Str)] string text);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern void SDL_free(nint value);
    [DllImport("libX11.so.6", CallingConvention = CallingConvention.Cdecl)] private static extern int XGetGeometry(nint display, nuint window, out nuint root, out int x, out int y, out uint width, out uint height, out uint borderWidth, out uint depth);
    [DllImport("libX11.so.6", CallingConvention = CallingConvention.Cdecl)] private static extern int XScreenCount(nint display);
    [DllImport("libX11.so.6", CallingConvention = CallingConvention.Cdecl)] private static extern nuint XRootWindow(nint display, int screen);
    internal static int WindowScreen(nint display, nuint window)
    {
        if (XGetGeometry(display, window, out var root, out _, out _, out _, out _, out _, out _) == 0)
            throw new InvalidOperationException("Cannot identify the X11 window's root.");
        int screens = XScreenCount(display);
        for (int screen = 0; screen < screens; screen++)
            if (XRootWindow(display, screen) == root) return screen;
        throw new InvalidOperationException("Cannot identify the X11 window's screen.");
    }
}

internal static class LinuxInput
{
    // YoUI's existing key contract uses Win32-compatible logical codes, not SDL scancodes.
    internal static int Key(int key) => key switch
    {
        >= 'a' and <= 'z' => key - 32,
        8 or 9 or 13 or 27 or 32 => key,
        127 => 46,
        1073741898 => 36, // Home
        1073741901 => 35, // End
        1073741903 => 39, // Right
        1073741904 => 37, // Left
        1073741905 => 40, // Down
        1073741906 => 38, // Up
        1073741899 => 33, // PageUp
        1073741902 => 34, // PageDown
        >= 1073741882 and <= 1073741893 => key - 1073741882 + 112, // F1..F12
        >= '0' and <= '9' => key,
        _ => 0
    };
}
