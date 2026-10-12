using System.Runtime.InteropServices;
using System.Text;
using YoUI;
using YoUI.Platform.Linux;

if (args.Length != 0 && !(args.Length == 1 && args[0] == "--native-init-failure"))
    throw new ArgumentException("Usage: YoUI.Platform.Linux.Tests [--native-init-failure]");
int checks = 0;
void Check(bool value, string message) { if (!value) throw new Exception(message); checks++; }
Check(Marshal.SizeOf<Sdl.Event>() == 56, "SDL event union size");
Check(Marshal.SizeOf<Sdl.WindowInfo>() == 72, "SDL LP64 WM info size");
Check(Marshal.OffsetOf<Sdl.Event>(nameof(Sdl.Event.Key)).ToInt32() == 20, "key offset");
Check(Marshal.OffsetOf<Sdl.Event>(nameof(Sdl.Event.Modifiers)).ToInt32() == 24, "modifier offset");
Check(Marshal.OffsetOf<Sdl.Event>(nameof(Sdl.Event.PreciseWheelY)).ToInt32() == 32, "precise wheel offset");
Check(Marshal.OffsetOf<Sdl.WindowInfo>(nameof(Sdl.WindowInfo.Display)).ToInt32() == 8, "X display offset");
Check(Marshal.OffsetOf<Sdl.WindowInfo>(nameof(Sdl.WindowInfo.Window)).ToInt32() == 16, "X window offset");
foreach (var (sdl, logical) in new[] { (97, 65), (122, 90), (48, 48), (8, 8), (9, 9), (13, 13), (27, 27), (32, 32), (127, 46), (1073741898, 36), (1073741901, 35), (1073741903, 39), (1073741904, 37), (1073741905, 40), (1073741906, 38), (1073741899, 33), (1073741902, 34), (1073741882, 112), (1073741893, 123), (1073742049, 0) })
    Check(LinuxInput.Key(sdl) == logical, $"key {sdl}");
unsafe
{
    Sdl.Event value = default;
    var text = "é中👩‍🚀";
    byte[] bytes = Encoding.UTF8.GetBytes(text);
    for (int i = 0; i < bytes.Length; i++) value.Text[i] = bytes[i];
    Check(value.GetText() == text, "UTF-8 committed/preedit text");
    value.Text[0] = 0;
    Check(value.GetText() == "", "empty composition terminates preedit");
    for (int i = 0; i < 32; i++) value.Text[i] = (byte)'x';
    Check(value.GetText() == new string('x', 32), "bounded unterminated native text");
}
// Invalid dimensions fail before native library/GPU initialization, including on Linux without DISPLAY.
if (OperatingSystem.IsLinux() && IntPtr.Size == 8)
{
    foreach (int size in new[] { 0, -1, 8193 })
    {
        bool rejected = false;
        try { using var window = new DesktopWindow("invalid", new StackPanel(), size, 400); }
        catch (ArgumentOutOfRangeException) { rejected = true; }
        Check(rejected, "invalid dimensions");
    }
}
if (OperatingSystem.IsLinux())
{
    foreach (var (display, xid, screen) in new[] { ((nint)0, (nuint)1, 0), ((nint)1, (nuint)0, 0), ((nint)1, (nuint)1, -1) })
    {
        bool rejected = false;
        try { using var renderer = NativeRenderer.CreateXlib(640, 400, display, xid, screen); }
        catch (ArgumentException) { rejected = true; }
        Check(rejected, "managed Xlib rejects invalid handles before loading native library");
    }
    bool invalidBudget = false;
    try { using var renderer = NativeRenderer.CreateXlib(640, 400, 1, 1, 0, new ResourceBudget(0, 0)); }
    catch (ArgumentOutOfRangeException) { invalidBudget = true; }
    Check(invalidBudget, "managed Xlib rejects invalid budget before loading native library");
}
else
{
    bool rejected = false;
    try { using var renderer = NativeRenderer.CreateXlib(640, 400, 0, 0, 0); }
    catch (PlatformNotSupportedException) { rejected = true; }
    Check(rejected, "managed Xlib rejects unsupported host before loading native library");
}
if (args.Length != 0)
{
    if (!OperatingSystem.IsLinux() || IntPtr.Size != 8 || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DISPLAY")))
        throw new InvalidOperationException("Native failure probe requires Linux LP64 with DISPLAY unset.");
    for (int attempt = 0; attempt < 2; attempt++)
    {
        bool failedAtVideo = false;
        try { using var window = new DesktopWindow("no display", new StackPanel()); }
        catch (InvalidOperationException ex) when (!ex.Message.Contains("Only one") && !ex.Message.Contains("already owned")) { failedAtVideo = true; }
        Check(failedAtVideo, "missing display fails explicitly and releases singleton ownership");
        Check(Sdl.SDL_WasInit(0) == 0, "failed native video init leaves no SDL subsystem");
    }
}
Console.WriteLine($"PASS {checks} Linux host ABI/key/text/argument logical checks; no native window or OS input was executed.");
