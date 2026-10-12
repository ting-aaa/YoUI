using System.Runtime.InteropServices;
using System.Text;
using System.Reflection;
using System.Runtime.CompilerServices;
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
    // This is a no-window lifecycle regression, not native window evidence. SDL_PollEvent
    // sees an uninitialized queue; no X display or renderer is created. The public Run path
    // must terminate when its tick or logical button callback disposes the host.
    DesktopWindow LogicalHost(Element root)
    {
        var host = (DesktopWindow)RuntimeHelpers.GetUninitializedObject(typeof(DesktopWindow));
        typeof(DesktopWindow).GetField("ownerThread", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(host, Environment.CurrentManagedThreadId);
        typeof(DesktopWindow).GetProperty(nameof(DesktopWindow.Document))!.SetValue(host, new UiDocument(root, new StubMeasurer(), 640, 400));
        return host;
    }
    using (var host = LogicalHost(new StackPanel()))
    {
        host.Run(() => host.Dispose());
        Check(host.IsClosed, "tick disposal terminates Run before another native call");
        host.Dispose();
        Check(host.IsClosed, "Dispose is idempotent and terminal");
    }
    var button = new Button("dispose");
    using (var host = LogicalHost(button))
    {
        button.Clicked += host.Dispose;
        host.Run(() =>
        {
            host.Document.Dispatch(new(EventKind.PointerDown) { Position = new(20, 20) });
            host.Document.Dispatch(new(EventKind.PointerUp) { Position = new(20, 20) });
        });
        Check(host.IsClosed, "logical button callback disposal terminates Run");
    }
    foreach (var nativeEvent in new[]
    {
        new Sdl.Event { Type = 0x200, WindowId = 42, WindowEvent = 13 },
        new Sdl.Event { Type = 0x200, WindowId = 42, WindowEvent = 2 },
        new Sdl.Event { Type = 0x401, WindowId = 42, Button = 1, X = 20, Y = 20 }
    })
    {
        var target = new Button("cancel");
        using var host = LogicalHost(target);
        typeof(DesktopWindow).GetField("windowId", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(host, (uint)42);
        typeof(DesktopWindow).GetField("pointerDown", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(host, true);
        host.Document.Dispatch(new(EventKind.PointerDown) { Position = new(20, 20) });
        int compositions = 0;
        host.Document.PreviewInput += e => { if (e.Kind == EventKind.Composition) compositions++; };
        target.Event += e => { if (e.Kind == EventKind.PointerCancel && e.Phase == EventPhase.Target) host.Dispose(); };
        typeof(DesktopWindow).GetMethod("Process", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(host, [nativeEvent]);
        Check(host.IsClosed && compositions == 0, "logical cancellation disposal stops event continuation");
    }
    DesktopWindow? painting = null;
    using (painting = LogicalHost(new DisposeOnPaint(() => painting!.Dispose())))
    {
        painting.RenderNow();
        Check(painting.IsClosed, "paint callback disposal skips renderer submission");
    }
}
Console.WriteLine($"PASS {checks} Linux host ABI/key/text/argument logical checks; no native window or OS input was executed.");

sealed class StubMeasurer : ITextMeasurer
{
    public (float Width, float Height) Measure(string text, float size, float width, bool bold = false) => (Math.Min(width, text.Length * size), size);
}
sealed class DisposeOnPaint(Action dispose) : Element
{
    protected override void Paint(DrawList list, Rect bounds, float opacity, Theme theme) => dispose();
}
