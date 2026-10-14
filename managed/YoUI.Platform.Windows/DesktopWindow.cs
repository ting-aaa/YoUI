using System.ComponentModel;
using System.Numerics;
using System.Runtime.InteropServices;

namespace YoUI.Platform.Windows;

/// <summary>Thin Win32 host. Owns window/events only; GPU and UI stay host independent.</summary>
public sealed class DesktopWindow : IDisposable
{
    private readonly WindowProc callback;
    private readonly int ownerThread = Environment.CurrentManagedThreadId;
    private nint handle;
    private readonly string className = "YoUI_" + Guid.NewGuid().ToString("N");
    private bool closed, disposed, releasingPointer, classRegistered;
    private int operationDepth;
    private bool cleanupPending, cleaningUp;
    private readonly MouseLeaveTracker mouseLeave = new();
    private Exception? callbackError;
    private char pendingHighSurrogate;
    private readonly int minimumWidth, minimumHeight;
    public NativeRenderer Renderer { get; }
    public UiDocument Document { get; }
    public FrameStats LastFrame { get; private set; }
    public float Scale { get; private set; }
    public nint Handle => handle;
    public event Action? AnimationFrame;
    public Func<bool>? Closing { get; set; }
    public void SetAnimationActive(bool active)
    {
        VerifyThread();
        if (closed || disposed) return;
        if (active) SetTimer(handle, 1, 16, 0); else KillTimer(handle, 1);
    }
    private const uint WsOverlappedWindow = 0x00CF0000;

    public DesktopWindow(string title, Element root, int width = 1280, int height = 820, int minimumWidth = 790, int minimumHeight = 740)
    {
        this.minimumWidth = minimumWidth; this.minimumHeight = minimumHeight;
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("This desktop host requires Windows.");
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(title);
        if (root.Parent != null || root.Document != null) throw new InvalidOperationException("Root already belongs to a tree.");
        if (width <= 0 || height <= 0 || minimumWidth <= 0 || minimumHeight <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        SetProcessDpiAwarenessContext(-4);
        callback = WndProc;
        NativeRenderer? createdRenderer = null;
        try
        {
            var wc = new WindowClass { Size = (uint)Marshal.SizeOf<WindowClass>(), Proc = callback, Instance = GetModuleHandle(null), ClassName = className, Cursor = LoadCursor(0, 32512) };
            if (RegisterClassEx(ref wc) == 0) throw new Win32Exception(Marshal.GetLastWin32Error());
            classRegistered = true;
            uint dpi = GetDpiForSystem(); Scale = dpi / 96f;
            var rect = new NativeRect { Right = (int)(width * Scale), Bottom = (int)(height * Scale) }; AdjustWindowRectExForDpi(ref rect, WsOverlappedWindow, false, 0, dpi);
            handle = CreateWindowEx(0, className, title, WsOverlappedWindow, unchecked((int)0x80000000), unchecked((int)0x80000000), rect.Right - rect.Left, rect.Bottom - rect.Top, 0, 0, wc.Instance, 0);
            if (handle == 0) throw new Win32Exception(Marshal.GetLastWin32Error());
            Scale = Math.Max(1, GetDpiForWindow(handle) / 96f);
            GetClientRect(handle, out var client);
            createdRenderer = new((uint)client.Right, (uint)client.Bottom, handle);
            Renderer = createdRenderer;
            Document = new(root, new LogicalTextMeasurer(Renderer, () => Scale), client.Right / Scale, client.Bottom / Scale);
            Document.Clipboard = new WindowsClipboard(() => handle);
        }
        catch
        {
            // Failed construction still owns its class/window. Suppress teardown callbacks,
            // release the renderer while its HWND is alive, then release only this host's HWND.
            closed = disposed = true;
            try { createdRenderer?.Dispose(); ReleaseWindow(); }
            finally { GC.KeepAlive(callback); }
            throw;
        }
    }
    private void VerifyThread()
    {
        if (ownerThread != Environment.CurrentManagedThreadId) throw new InvalidOperationException("Window calls must stay on the creating thread.");
    }
    private sealed class LogicalTextMeasurer(NativeRenderer renderer, Func<float> scale) : ITextLayoutProvider
    {
        public (float Width, float Height) Measure(string text, float size, float width, bool bold = false) { float s = scale(); var m = renderer.Measure(text, size * s, width * s, bold); return (m.Width / s, m.Height / s); }
        public TextLayout Layout(string text, float size, float width, bool bold = false, bool wrap = true)
        {
            float s = scale(); var layout = renderer.Layout(text, size * s, width * s, bold, wrap);
            return new(text, layout.Cells.Select(c => c with { Bounds = new(c.Bounds.X / s, c.Bounds.Y / s, c.Bounds.Width / s, c.Bounds.Height / s) }));
        }
    }
    private readonly DrawList scaled = new();
    private readonly PresentationLoop presentation = new();
    private bool suspended;
    public void RenderNow()
    {
        VerifyThread();
        if (closed || disposed || suspended) return;
        presentation.CompleteAttempt(RenderFrame());
    }
    private bool RenderFrame()
    {
        operationDepth++;
        try
        {
            var list = Document.BuildFrame();
            if (closed || disposed) return false; // Layout/paint callbacks can dispose the host.
            LastFrame = Renderer.Render(Scale == 1 ? list : ScaleFrame(list));
            return Renderer.PresentationPending;
        }
        finally { EndOperation(); }
    }
    private DrawList ScaleFrame(DrawList source)
    {
        source.CopyScaledTo(scaled, Scale);
        return scaled;
    }
    public void Run(bool visible = true, int? frameLimit = null, Action<int>? tick = null)
    {
        VerifyThread();
        if (closed || disposed) return;
        operationDepth++;
        try
        {
            if (visible) ShowWindow(handle, 5);
            int iterations = 0;
            while (!closed && !disposed)
            {
                PumpEvents();
                if (closed || disposed) break;
                tick?.Invoke(iterations);
                if (closed || disposed) break;
                presentation.Step(Document.NeedsRender, suspended, RenderFrame, Renderer.RetryPresentation);
                if (closed || disposed) break; // Never wait after disposal from painting.
                iterations++;
                if (frameLimit.HasValue && iterations >= frameLimit) break;
                int? retryWait = presentation.WaitMilliseconds(suspended);
                if (tick != null || retryWait.HasValue)
                    MsgWaitForMultipleObjectsEx(0, 0, (uint)Math.Min(16, retryWait ?? 16), 0x04FF, 0x0004);
                else if (suspended || !Document.NeedsRender) WaitMessage();
            }
        }
        finally { EndOperation(); }
    }
    public void PumpEvents()
    {
        VerifyThread();
        if (closed || disposed) return;
        operationDepth++;
        try
        {
            while (!closed && !disposed && PeekMessage(out var message, 0, 0, 0, 1))
            {
                if (message.MessageId == 0x0012) { closed = true; break; }
                TranslateMessage(ref message); DispatchMessage(ref message);
            }
            if (callbackError != null) throw new InvalidOperationException("Window callback failed.", callbackError);
        }
        finally { EndOperation(); }
    }
    private nint WndProc(nint hwnd, uint message, nuint wParam, nint lParam)
    {
        operationDepth++;
        try { return ProcessMessage(hwnd, message, wParam, lParam); }
        catch (Exception e) { callbackError ??= e; closed = true; return 0; }
        finally
        {
            // Physical teardown can synchronously enter WndProc again. Never let
            // an error escape through the unmanaged callback boundary.
            try { EndOperation(); }
            catch (Exception e) { callbackError ??= e; closed = true; }
        }
    }
    private nint ProcessMessage(nint hwnd, uint message, nuint wParam, nint lParam)
    {
        if (closed || disposed) return DefWindowProc(hwnd, message, wParam, lParam);
        if (message == 0x0024)
        {
            uint dpi = GetDpiForWindow(hwnd); if (dpi == 0) dpi = GetDpiForSystem(); float s = dpi / 96f;
            var minimum = new NativeRect { Right = (int)(minimumWidth * s), Bottom = (int)(minimumHeight * s) }; AdjustWindowRectExForDpi(ref minimum, WsOverlappedWindow, false, 0, dpi);
            var limits = Marshal.PtrToStructure<MinMaxInfo>(lParam); limits.MinTrackSize = new() { X = minimum.Right - minimum.Left, Y = minimum.Bottom - minimum.Top }; Marshal.StructureToPtr(limits, lParam, false); return 0;
        }
        // WM_NCCREATE/WM_SIZE can arrive before the document is initialized.
        if (Document == null) return DefWindowProc(hwnd, message, wParam, lParam);
        Vector2 point = new((short)((long)lParam & 0xffff) / Scale, (short)(((long)lParam >> 16) & 0xffff) / Scale);
        bool shift = GetKeyState(0x10) < 0, control = GetKeyState(0x11) < 0;
        switch (message)
        {
            case 0x0010: if (Closing?.Invoke() != false) closed = true; return 0; // Keep HWND alive until GPU is disposed.
            case 0x000F: ValidateRect(hwnd, 0); Document.Root.Invalidate(); return 0;
            case 0x0014: return 1;
            case 0x0113: AnimationFrame?.Invoke(); return 0;
            case 0x0005:
                GetClientRect(hwnd, out var client);
                suspended = wParam == 1 || client.Right <= 0 || client.Bottom <= 0;
                if (!suspended) { Renderer.Resize((uint)client.Right, (uint)client.Bottom); Document.Resize(client.Right / Scale, client.Bottom / Scale); Document.Root.Invalidate(); }
                return 0;
            case 0x02E0:
                Scale = (float)(wParam & 0xffff) / 96;
                Document.Root.Invalidate(DirtyFlags.All);
                var suggested = Marshal.PtrToStructure<NativeRect>(lParam);
                SetWindowPos(hwnd, 0, suggested.Left, suggested.Top, suggested.Right - suggested.Left, suggested.Bottom - suggested.Top, 0x0014); return 0;
            case 0x0200:
                GetClientRect(hwnd, out var mouseClient);
                mouseLeave.Move(hwnd, point.X >= 0 && point.Y >= 0 && point.X * Scale < mouseClient.Right && point.Y * Scale < mouseClient.Bottom);
                Document.Dispatch(new(EventKind.PointerMove) { Position = point }); return 0;
            case 0x02A3: // WM_MOUSELEAVE has no coordinates; clear hover, but preserve captured drags.
                if (mouseLeave.Leave(GetCapture() == hwnd))
                    Document.Dispatch(new(EventKind.PointerMove) { Position = new(-1, -1) });
                return 0;
            case 0x0201: SetCapture(hwnd); Document.Dispatch(new(EventKind.PointerDown) { Position = point, Shift = shift, Control = control }); UpdateIme(hwnd); return 0;
            case 0x0202:
                // A Clicked handler can enter a modal file dialog. Release OS capture before that
                // nested message loop, while preserving the logical target for this PointerUp.
                releasingPointer = true;
                try { ReleaseCapture(); } finally { releasingPointer = false; }
                Document.Dispatch(new(EventKind.PointerUp) { Position = point }); return 0;
            case 0x0215: if (!releasingPointer) Document.CancelPointer(); return 0;
            case 0x001F: Document.CancelPointer(); return 0;
            case 0x0008:
                Document.CancelPointer();
                if (!closed && !disposed) Document.Dispatch(new(EventKind.Composition) { Text = "" });
                return 0;
            case 0x020A:
                var p = new NativePoint { X = (short)((long)lParam & 0xffff), Y = (short)(((long)lParam >> 16) & 0xffff) }; ScreenToClient(hwnd, ref p);
                Document.Dispatch(new(EventKind.Scroll) { Position = new(p.X / Scale, p.Y / Scale), Delta = (short)((ulong)wParam >> 16) / 120f }); return 0;
            case 0x0100: Document.Dispatch(new(EventKind.KeyDown) { Key = (int)wParam, Shift = shift, Control = control }); UpdateIme(hwnd); return 0;
            case 0x0300: Document.Dispatch(new(EventKind.KeyDown) { Key = 88, Control = true }); return 0;
            case 0x0301: Document.Dispatch(new(EventKind.KeyDown) { Key = 67, Control = true }); return 0;
            case 0x0302: Document.Dispatch(new(EventKind.KeyDown) { Key = 86, Control = true }); return 0;
            case 0x0102:
                char ch = (char)wParam;
                if (char.IsHighSurrogate(ch)) pendingHighSurrogate = ch;
                else if (char.IsLowSurrogate(ch) && pendingHighSurrogate != 0) { Document.Dispatch(new(EventKind.TextInput) { Text = new string([pendingHighSurrogate, ch]) }); pendingHighSurrogate = '\0'; }
                else if (!char.IsControl(ch)) { pendingHighSurrogate = '\0'; Document.Dispatch(new(EventKind.TextInput) { Text = ch.ToString() }); }
                return 0;
            case 0x010F: // IME composition is distinct from committed text.
                var context = ImmGetContext(hwnd);
                if (context != 0)
                {
                    try
                    {
                        if (((long)lParam & 0x800) != 0) Document.Dispatch(new(EventKind.TextInput) { Text = Composition(context, 0x800) });
                        if (!closed && !disposed && ((long)lParam & 8) != 0) Document.Dispatch(new(EventKind.Composition) { Text = Composition(context, 8) });
                    }
                    finally { ImmReleaseContext(hwnd, context); }
                }
                return 0;
            case 0x010E: Document.Dispatch(new(EventKind.Composition) { Text = "" }); return 0;
        }
        return DefWindowProc(hwnd, message, wParam, lParam);
    }
    private static string Composition(nint context, uint index)
    {
        int bytes = ImmGetCompositionString(context, index, null, 0); if (bytes <= 0) return "";
        var buffer = new byte[bytes]; ImmGetCompositionString(context, index, buffer, (uint)bytes); return System.Text.Encoding.Unicode.GetString(buffer);
    }
    private void UpdateIme(nint hwnd)
    {
        if (closed || disposed) return;
        if (Document.Focused is not TextBox input) return;
        var context = ImmGetContext(hwnd); if (context == 0) return;
        try
        {
            var b = input.VisualBounds;
            var form = new CompositionForm { Style = 2, Position = new() { X = (int)((b.X + 14) * Scale), Y = (int)((b.Bottom - 8) * Scale) } };
            ImmSetCompositionWindow(context, ref form);
        }
        finally { ImmReleaseContext(hwnd, context); }
    }
    public void Dispose()
    {
        // A rejected foreign-thread call must not consume the owner's cleanup opportunity.
        VerifyThread();
        if (disposed) return;
        closed = disposed = true;
        cleanupPending = true;
        ReleaseResources();
    }
    private void EndOperation()
    {
        operationDepth--;
        ReleaseResources();
    }
    private void ReleaseResources()
    {
        // A document update/dispatch may continue measuring text after a callback
        // requests disposal. Mark the host terminal immediately, but keep its
        // renderer and HWND alive until the outermost host operation unwinds.
        if (!cleanupPending || operationDepth != 0 || cleaningUp) return;
        cleaningUp = true;
        try { Renderer.Dispose(); ReleaseWindow(); cleanupPending = false; }
        catch { disposed = false; throw; }
        finally { cleaningUp = false; GC.KeepAlive(callback); }
    }
    private void ReleaseWindow()
    {
        if (handle != 0)
        {
            if (!DestroyWindow(handle)) throw new Win32Exception(Marshal.GetLastWin32Error());
            handle = 0;
        }
        if (classRegistered)
        {
            if (!UnregisterClass(className, GetModuleHandle(null))) throw new Win32Exception(Marshal.GetLastWin32Error());
            classRegistered = false;
        }
    }
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate nint WindowProc(nint h, uint m, nuint w, nint l);
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct WindowClass { public uint Size, Style; public WindowProc Proc; public int ClassExtra, WindowExtra; public nint Instance, Icon, Cursor, Background; public string? MenuName; public string ClassName; public nint SmallIcon; }
    [StructLayout(LayoutKind.Sequential)] private struct NativeRect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct NativePoint { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] private struct Message { public nint Hwnd; public uint MessageId; public nuint WParam; public nint LParam; public uint Time; public NativePoint Point; public uint Private; }
    [StructLayout(LayoutKind.Sequential)] private struct CompositionForm { public uint Style; public NativePoint Position; public NativeRect Area; }
    [StructLayout(LayoutKind.Sequential)] private struct MinMaxInfo { public NativePoint Reserved, MaxSize, MaxPosition, MinTrackSize, MaxTrackSize; }
    [DllImport("user32", CharSet = CharSet.Unicode, SetLastError = true)] private static extern ushort RegisterClassEx(ref WindowClass wc);
    [DllImport("user32", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool UnregisterClass(string name, nint instance);
    [DllImport("user32", CharSet = CharSet.Unicode, SetLastError = true)] private static extern nint CreateWindowEx(uint ex, string cls, string title, uint style, int x, int y, int w, int h, nint parent, nint menu, nint instance, nint data);
    [DllImport("user32", CharSet = CharSet.Unicode)] private static extern nint DefWindowProc(nint h, uint m, nuint w, nint l);
    [DllImport("kernel32", CharSet = CharSet.Unicode)] private static extern nint GetModuleHandle(string? name);
    [DllImport("user32")] private static extern nint LoadCursor(nint instance, int id);
    [DllImport("user32")] private static extern bool ShowWindow(nint h, int command);
    [DllImport("user32", SetLastError = true)] private static extern bool DestroyWindow(nint h);
    [DllImport("user32")] private static extern bool AdjustWindowRect(ref NativeRect r, uint style, bool menu);
    [DllImport("user32")] private static extern bool GetClientRect(nint h, out NativeRect r);
    [DllImport("user32")] private static extern bool PeekMessage(out Message m, nint h, uint min, uint max, uint remove);
    [DllImport("user32")] private static extern bool TranslateMessage(ref Message m);
    [DllImport("user32", CharSet = CharSet.Unicode)] private static extern nint DispatchMessage(ref Message m);
    [DllImport("user32")] private static extern bool WaitMessage();
    [DllImport("user32")] private static extern uint MsgWaitForMultipleObjectsEx(uint count, nint handles, uint milliseconds, uint wakeMask, uint flags);
    [DllImport("user32")] private static extern nuint SetTimer(nint h, nuint id, uint ms, nint callback);
    [DllImport("user32")] private static extern bool KillTimer(nint h, nuint id);
    [DllImport("user32")] private static extern bool ValidateRect(nint h, nint rect);
    [DllImport("user32")] private static extern short GetKeyState(int key);
    [DllImport("user32")] private static extern nint SetCapture(nint h);
    [DllImport("user32")] private static extern nint GetCapture();
    [DllImport("user32")] private static extern bool ReleaseCapture();
    [DllImport("user32")] private static extern bool ScreenToClient(nint h, ref NativePoint p);
    [DllImport("user32")] private static extern bool SetProcessDpiAwarenessContext(nint context);
    [DllImport("user32")] private static extern uint GetDpiForWindow(nint h);
    [DllImport("user32")] private static extern uint GetDpiForSystem();
    [DllImport("user32")] private static extern bool AdjustWindowRectExForDpi(ref NativeRect r, uint style, bool menu, uint exStyle, uint dpi);
    [DllImport("user32")] private static extern bool SetWindowPos(nint h, nint after, int x, int y, int w, int height, uint flags);
    [DllImport("imm32")] private static extern nint ImmGetContext(nint h);
    [DllImport("imm32")] private static extern bool ImmReleaseContext(nint h, nint context);
    [DllImport("imm32", EntryPoint = "ImmGetCompositionStringW")] private static extern int ImmGetCompositionString(nint context, uint index, [Out] byte[]? buffer, uint length);
    [DllImport("imm32")] private static extern bool ImmSetCompositionWindow(nint context, ref CompositionForm form);
}
