using System.Runtime.InteropServices;

namespace YoUI.Platform.Linux;

/// <summary>
/// Experimental single-window Linux X11 host using SDL2 2.0.18+ (LP64).
/// Owns the event loop and native handles. All calls, including Dispose, stay on the creating thread.
/// One X11 pixel is one logical unit; Wayland, automatic DPI scaling and IME acceptance are not provided.
/// </summary>
public sealed class DesktopWindow : IDisposable
{
    private static int activeHost;
    private readonly int ownerThread = Environment.CurrentManagedThreadId;
    private nint window;
    private uint windowId;
    private bool videoInitialized, ownsHost, disposed, closed, suspended, pointerDown;
    private bool focused;
    private NativeRenderer? renderer;
    public NativeRenderer Renderer => renderer ?? throw new ObjectDisposedException(nameof(DesktopWindow));
    public UiDocument Document { get; private set; } = null!;
    public nuint X11Window { get; private set; }
    public string SdlVersion { get; private set; } = "";
    public FrameStats LastFrame { get; private set; }
    public int RenderedFrames { get; private set; }
    public bool IsClosed => closed;
    public bool IsSuspended => suspended;
    public bool HasInputFocus => focused;
    public int Width { get; private set; }
    public int Height { get; private set; }
    public Func<bool>? Closing { get; set; }

    public DesktopWindow(string title, Element root, int width = 640, int height = 360,
        int minimumWidth = 320, int minimumHeight = 240)
    {
        if (!OperatingSystem.IsLinux() || IntPtr.Size != 8)
            throw new PlatformNotSupportedException("The experimental X11 host requires 64-bit Linux.");
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(title);
        if (width is <= 0 or > 8192 || height is <= 0 or > 8192 || minimumWidth <= 0 || minimumHeight <= 0 || minimumWidth > width || minimumHeight > height)
            throw new ArgumentOutOfRangeException(nameof(width), "Require positive minimum <= initial size <= 8192.");
        if (Interlocked.CompareExchange(ref activeHost, 1, 0) != 0)
            throw new InvalidOperationException("Only one YoUI SDL host may be active in a process.");
        ownsHost = true;
        try
        {
            Sdl.SDL_GetVersion(out var version);
            SdlVersion = $"{version.Major}.{version.Minor}.{version.Patch}";
            if (version.Major != 2 || (version.Minor == 0 && version.Patch < 18))
                throw new PlatformNotSupportedException("SDL2 2.0.18 or newer is required.");
            if (Sdl.SDL_WasInit(0) != 0)
                throw new InvalidOperationException("SDL is already owned by another host; sharing its global event queue is unsupported.");
            Sdl.Check(Sdl.SDL_VideoInit("x11"));
            videoInitialized = true;
            Sdl.SDL_EnableScreenSaver(); // Desktop UI must not suppress the user's screensaver.
            if (Marshal.PtrToStringUTF8(Sdl.SDL_GetCurrentVideoDriver()) != "x11")
                throw new PlatformNotSupportedException("This host requires SDL's X11 driver.");
            // Hidden during renderer construction. Resizable; no SDL OpenGL/Vulkan renderer is created.
            window = Sdl.SDL_CreateWindow(title, 0x2FFF0000, 0x2FFF0000, width, height, 0x8 | 0x20);
            if (window == 0) throw new InvalidOperationException(Sdl.Error);
            Sdl.SDL_SetWindowMinimumSize(window, minimumWidth, minimumHeight);
            Sdl.SDL_SetWindowMaximumSize(window, 8192, 8192);
            var info = new Sdl.WindowInfo { Version = version };
            if (Sdl.SDL_GetWindowWMInfo(window, ref info) == 0 || info.Subsystem != 2 || info.Display == 0 || info.Window == 0)
                throw new InvalidOperationException("SDL did not provide a valid X11 display/window: " + Sdl.Error);
            X11Window = info.Window;
            windowId = Sdl.SDL_GetWindowID(window);
            Sdl.SDL_GetWindowSize(window, out width, out height);
            Width = width; Height = height;
            renderer = NativeRenderer.CreateXlib((uint)width, (uint)height, info.Display, info.Window, Sdl.WindowScreen(info.Display, info.Window));
            Document = new(root, renderer, width, height);
            Document.Clipboard = new Clipboard(this);
            Sdl.SDL_StartTextInput();
            Sdl.SDL_ShowWindow(window);
        }
        catch { Release(); disposed = true; throw; }
    }

    private void Verify()
    {
        if (ownerThread != Environment.CurrentManagedThreadId) throw new InvalidOperationException("Window calls must stay on the creating thread.");
        ObjectDisposedException.ThrowIf(disposed, this);
    }
    public void RenderNow()
    {
        Verify();
        if (closed || suspended) return;
        var frame = Document.BuildFrame();
        if (closed || disposed) return; // Layout/paint callbacks may dispose the host.
        LastFrame = Renderer.Render(frame);
        RenderedFrames++;
    }
    public void PumpEvents()
    {
        Verify();
        while (!closed && Sdl.SDL_PollEvent(out var value) != 0) Process(value);
    }
    /// <summary>Run until accepted OS close. Optional tick runs approximately every 16 ms for animation/diagnostics.</summary>
    public void Run(Action? tick = null)
    {
        Verify();
        while (!closed)
        {
            PumpEvents();
            if (closed) break;
            tick?.Invoke();
            if (closed) break; // Tick/input callbacks may dispose the native window.
            if (Document.NeedsRender && !suspended) RenderNow();
            if (closed) break;
            // Wake for native input without spinning while idle, hidden or minimized.
            if (Sdl.SDL_WaitEventTimeout(out var value, tick == null ? 100 : 16) != 0) Process(value);
        }
    }
    private void CancelPointer()
    {
        if (disposed) return;
        if (pointerDown) { Sdl.SDL_CaptureMouse(0); pointerDown = false; }
        Document.CancelPointer();
    }
    private void RequestClose()
    {
        if (Closing?.Invoke() == false || closed) return;
        CancelPointer();
        closed = true; // Native window/display remain alive until the renderer has been destroyed.
    }
    private void Resize()
    {
        Sdl.SDL_GetWindowSize(window, out int width, out int height);
        if (width <= 0 || height <= 0) { suspended = true; return; }
        if (width == Width && height == Height) return;
        Renderer.Resize((uint)width, (uint)height);
        Width = width; Height = height;
        Document.Resize(width, height);
    }
    private void Process(Sdl.Event value)
    {
        // SDL allocates drop/extended preedit payloads; free them even for unrelated windows.
        if (value.Type is 0x1000 or 0x1001) { Sdl.SDL_free(value.DropText); return; }
        if (value.Type == 0x305)
        {
            try
            {
                if (value.WindowId == windowId && focused && !suspended)
                    Document.Dispatch(new(EventKind.Composition) { Text = Marshal.PtrToStringUTF8(value.ExtendedText) ?? "" });
            }
            finally { Sdl.SDL_free(value.ExtendedText); }
            return;
        }
        if (value.Type == 0x100) { RequestClose(); return; }
        if (value.WindowId != windowId) return;
        if (value.Type == 0x200)
        {
            switch (value.WindowEvent)
            {
                case 1: case 8: case 9: // shown/maximized/restored
                    suspended = false; Resize(); Document.Root.Invalidate(); break;
                case 2: case 7: // hidden/minimized
                    suspended = true; focused = false; CancelPointer();
                    if (!closed) Document.Dispatch(new(EventKind.Composition) { Text = "" });
                    break;
                case 3: Document.Root.Invalidate(); break; // exposed
                case 5: case 6: Resize(); break;
                case 11:
                    if (!pointerDown) Document.Dispatch(new(EventKind.PointerMove) { Position = new(-1, -1) });
                    break;
                case 12: focused = true; break;
                case 13:
                    focused = false; CancelPointer();
                    if (!closed) Document.Dispatch(new(EventKind.Composition) { Text = "" });
                    break;
                case 14: RequestClose(); break;
            }
            return;
        }
        if (suspended) return;
        ushort modifiers = value.Type == 0x300 ? value.Modifiers : Sdl.SDL_GetModState();
        bool shift = (modifiers & 3) != 0, control = (modifiers & 0xC0) != 0;
        switch (value.Type)
        {
            case 0x400:
                Document.Dispatch(new(EventKind.PointerMove) { Position = new(value.X, value.Y) }); break;
            case 0x401 when value.Button == 1:
                if (pointerDown) CancelPointer();
                if (closed) return;
                Sdl.Check(Sdl.SDL_CaptureMouse(1)); pointerDown = true;
                try { Document.Dispatch(new(EventKind.PointerDown) { Position = new(value.X, value.Y), Shift = shift, Control = control }); }
                catch { CancelPointer(); throw; }
                break;
            case 0x402 when value.Button == 1:
                // Release OS capture before callbacks, which can reenter application code.
                Sdl.SDL_CaptureMouse(0);
                bool wasDown = pointerDown; pointerDown = false;
                if (wasDown) Document.Dispatch(new(EventKind.PointerUp) { Position = new(value.X, value.Y) });
                break;
            case 0x403:
                Sdl.SDL_GetMouseState(out int x, out int y);
                float delta = value.PreciseWheelY * (value.WheelDirection == 1 ? -1 : 1);
                if (float.IsFinite(delta) && delta != 0)
                    Document.Dispatch(new(EventKind.Scroll) { Position = new(x, y), Delta = delta });
                break;
            case 0x300 when focused:
                int key = LinuxInput.Key(value.Key);
                if (key != 0) Document.Dispatch(new(EventKind.KeyDown) { Key = key, Shift = shift, Control = control });
                break;
            case 0x303 when focused:
                Document.Dispatch(new(EventKind.TextInput) { Text = value.GetText() }); break;
            case 0x302 when focused:
                Document.Dispatch(new(EventKind.Composition) { Text = value.GetText() }); break;
        }
    }
    public void Dispose()
    {
        if (disposed) return;
        Verify();
        closed = true; // Terminal before cleanup: event/tick/paint callbacks can dispose synchronously.
        // Do not invalidate native handles if renderer disposal throws (e.g. a wrong-thread call).
        renderer?.Dispose(); renderer = null;
        Release(); disposed = true;
    }
    private void Release()
    {
        renderer?.Dispose(); renderer = null;
        if (videoInitialized) { Sdl.SDL_CaptureMouse(0); Sdl.SDL_StopTextInput(); }
        if (window != 0) { Sdl.SDL_DestroyWindow(window); window = 0; X11Window = 0; }
        if (videoInitialized) { Sdl.SDL_VideoQuit(); videoInitialized = false; }
        if (ownsHost) { Interlocked.Exchange(ref activeHost, 0); ownsHost = false; }
    }
    private sealed class Clipboard(DesktopWindow owner) : IClipboard
    {
        public string GetText()
        {
            owner.Verify(); var text = Sdl.SDL_GetClipboardText();
            if (text == 0) throw new InvalidOperationException(Sdl.Error);
            try { return Marshal.PtrToStringUTF8(text) ?? ""; }
            finally { Sdl.SDL_free(text); }
        }
        public void SetText(string text) { owner.Verify(); Sdl.Check(Sdl.SDL_SetClipboardText(text)); }
    }
}
