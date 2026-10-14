using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using YoUI;
using YoUI.Platform.Windows;

internal static class NativeLifecycleTests
{
    internal static void Run()
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Native lifecycle acceptance requires Windows.");
        var checks = new List<string>();
        string? adapter = null, failure = null;
        void Check(bool value, string description)
        {
            if (!value) throw new Exception(description);
            checks.Add(description); Console.WriteLine("PASS " + description);
        }
        void Reject<T>(Action action, string description) where T : Exception
        {
            bool rejected = false;
            try { action(); } catch (T) { rejected = true; }
            Check(rejected, description);
        }
        DesktopWindow Window(Element root)
        {
            var window = new DesktopWindow("YoUI hidden lifecycle acceptance", root, 200, 128, 1, 1);
            adapter ??= window.Renderer.Adapter;
            return window;
        }
        void Released(DesktopWindow window, nint hwnd, string description)
        {
            Check(window.Renderer.IsDisposed && window.Handle == 0 && !IsWindow(hwnd), description);
        }
        try
        {
            var baseline = Windows();
            string? backend = Environment.GetEnvironmentVariable("YOUI_BACKEND");
            try
            {
                Environment.SetEnvironmentVariable("YOUI_BACKEND", "lifecycle-invalid-backend");
                Reject<NativeRendererException>(() => { using var window = Window(new Element()); }, "invalid backend rejects construction");
            }
            finally { Environment.SetEnvironmentVariable("YOUI_BACKEND", backend); }
            Check(Windows().Keys.ToHashSet().SetEquals(baseline.Keys), "failed renderer construction leaves no HWND");

            var attachedRoot = new Element();
            var originalDocument = new UiDocument(attachedRoot, new Measurer(), 200, 128);
            Reject<InvalidOperationException>(() => { using var window = Window(attachedRoot); }, "already attached root rejects construction");
            Check(ReferenceEquals(attachedRoot.Document, originalDocument), "rejected construction preserves the existing document");
            Check(Windows().Keys.ToHashSet().SetEquals(baseline.Keys), "already attached root creates no foreign HWND");
            originalDocument.BuildFrame();

            // This fails after both HWND and renderer were created, unlike root prevalidation.
            var attachmentFailure = new FailingAttachmentRoot(() => Windows().Where(w => !baseline.ContainsKey(w.Key)).Select(w => w.Value).ToArray());
            var failedChild = attachmentFailure.Add(new TextBox { Text = "Reusable after construction failure" });
            var failedPanel = attachmentFailure.Add(new Element()); var failedGrandchild = failedPanel.Add(new Label("Nested child"));
            Reject<InvalidOperationException>(() => { using var window = Window(attachmentFailure); }, "attachment callback failure rejects construction");
            Check(Windows().Keys.ToHashSet().SetEquals(baseline.Keys), "failed document construction releases its HWND");
            Check(attachmentFailure.CreatedClasses.Length == 1 && attachmentFailure.CreatedClasses.All(name => !ClassExists(name)), "failed document construction unregisters its window class");
            Reject<ObjectDisposedException>(() => attachmentFailure.Measurer!.Measure("probe", 14, 100), "failed document construction disposes its renderer");
            Check(attachmentFailure.Document == null && failedChild.Document == null && failedPanel.Document == null && failedGrandchild.Document == null,
                "failed document construction clears root and descendant associations");
            attachmentFailure.ThrowOnAttach = false;
            using (var recovered = Window(attachmentFailure))
            {
                nint hwnd = recovered.Handle;
                recovered.Run(visible: false, frameLimit: 1);
                Check(recovered.LastFrame.Commands > 0 && failedChild.Document == recovered.Document && failedGrandchild.Document == recovered.Document,
                    "failed constructor root and descendants can be reused by a new native host");
                recovered.Dispose(); Released(recovered, hwnd, "recovered host releases renderer and HWND");
            }

            using (var window = Window(new Element()))
            {
                nint hwnd = window.Handle; int ticks = 0;
                window.Run(visible: false, tick: _ => { ticks++; window.Dispose(); });
                Check(ticks == 1 && window.LastFrame.Commands == 0, "tick disposal stops Run before render or another tick");
                Released(window, hwnd, "tick disposal releases renderer and HWND");
                window.Run(visible: false, tick: _ => ticks++);
                window.RenderNow(); window.PumpEvents(); window.SetAnimationActive(true); window.Dispose();
                Check(ticks == 1, "disposed host entry points are terminal and disposal is idempotent");
            }

            DesktopWindow? painting = null; int paints = 0;
            var paintingRoot = new PaintCallback(() =>
            {
                paints++; painting!.Dispose();
                Check(!painting.Renderer.IsDisposed && IsWindow(painting.Handle), "paint disposal retains renderer and HWND until the document update unwinds");
            });
            paintingRoot.Add(new TextBox { Text = "Text shaping continues during unwind" });
            using (var window = Window(paintingRoot))
            {
                painting = window; nint hwnd = window.Handle;
                window.Run(visible: false, frameLimit: 2);
                Check(paints == 1 && window.Document.PaintPasses == 1 && window.LastFrame.Commands == 0,
                    "paint disposal permits TextBox provider work to unwind and skips submission and idle wait");
                Released(window, hwnd, "paint disposal releases renderer and HWND");
            }

            paintingRoot = new PaintCallback(() => painting!.Dispose());
            paintingRoot.Add(new TextBox { Text = "Standalone RenderNow unwind" });
            using (var window = Window(paintingRoot))
            {
                painting = window; nint hwnd = window.Handle;
                window.RenderNow();
                Check(window.Document.PaintPasses == 1 && window.LastFrame.Commands == 0,
                    "standalone RenderNow disposal unwinds TextBox painting without native submission");
                Released(window, hwnd, "standalone RenderNow deferred cleanup releases renderer and HWND");
            }

            var inputRoot = new Element(); var input = inputRoot.Add(new TextBox { Text = "Dispatch unwind" });
            using (var window = Window(inputRoot))
            {
                nint hwnd = window.Handle; bool providerAlive = false;
                input.Event += e =>
                {
                    if (e.Kind != EventKind.PointerDown || e.Phase != EventPhase.Target) return;
                    window.Dispose();
                    var measurement = window.Document.TextMeasurer.Measure("callback unwind", 14, 100);
                    providerAlive = !window.Renderer.IsDisposed && IsWindow(hwnd) && measurement.Height > 0;
                };
                int coordinate = (int)(20 * window.Scale);
                SendMessage(hwnd, 0x0201, 1, (nint)(coordinate | (coordinate << 16)));
                Check(providerAlive, "input disposal retains the text provider until dispatch continuation unwinds");
                Released(window, hwnd, "native input deferred cleanup releases renderer and HWND");
            }

            var root = new Element(); var button = root.Add(new Button("Close"));
            using (var window = Window(root))
            {
                nint hwnd = window.Handle; int ticks = 0, clicks = 0;
                button.Clicked += () => { clicks++; window.Dispose(); };
                window.Document.Focus(button);
                Check(PostMessage(hwnd, 0x0100, 13, 0), "post native key activation for callback disposal");
                window.Run(visible: false, frameLimit: 2, tick: _ => ticks++);
                Check(clicks == 1 && ticks == 0 && window.LastFrame.Commands == 0, "input disposal stops before tick, IME continuation and rendering");
                Released(window, hwnd, "input disposal releases renderer and HWND");
            }

            root = new Element(); button = root.Add(new Button("Cancel"));
            using (var window = Window(root))
            {
                nint hwnd = window.Handle; int cancellations = 0, compositions = 0;
                button.Event += e =>
                {
                    if (e.Kind != EventKind.PointerCancel) return;
                    cancellations++; window.Dispose();
                };
                window.Document.PreviewInput += e => { if (e.Kind == EventKind.Composition) compositions++; };
                int coordinate = (int)(20 * window.Scale);
                nint point = (nint)(coordinate | (coordinate << 16));
                SendMessage(hwnd, 0x0201, 1, point);
                Check(GetCapture() == hwnd, "native press acquires capture before cancellation disposal");
                SendMessage(hwnd, 0x0008, 0, 0);
                Check(cancellations == 1 && compositions == 0, "focus-loss cancellation disposal skips composition continuation");
                Released(window, hwnd, "cancellation disposal releases renderer and HWND");
                Check(GetCapture() != hwnd, "destroyed host leaves no native pointer capture");
            }

            using (var window = Window(new Element()))
            {
                nint hwnd = window.Handle; string name = Windows()[hwnd];
                Reject<InvalidOperationException>(() => Task.Run(window.Dispose).GetAwaiter().GetResult(), "foreign-thread disposal is rejected before changing state");
                Check(!window.Renderer.IsDisposed && window.Handle == hwnd && IsWindow(hwnd), "foreign-thread disposal preserves owner cleanup opportunity");
                foreach (Action operation in new Action[] { window.RenderNow, window.PumpEvents, () => window.Run(visible: false, frameLimit: 1), () => window.SetAnimationActive(true) })
                    Reject<InvalidOperationException>(() => Task.Run(operation).GetAwaiter().GetResult(), "foreign-thread window operation is rejected");
                window.Run(visible: false, frameLimit: 1);
                window.Dispose();
                Released(window, hwnd, "owner-thread disposal succeeds after foreign-thread rejection");
                Check(!ClassExists(name), "successful disposal unregisters the window class");
            }

            using (var window = Window(new Element()))
            {
                nint hwnd = window.Handle; var original = new InvalidOperationException("synchronous native callback fixture");
                window.AnimationFrame += () => throw original;
                SendMessage(hwnd, 0x0113, 1, 0);
                bool reported = false;
                try { window.PumpEvents(); }
                catch (InvalidOperationException error) { reported = ReferenceEquals(error.InnerException, original); }
                Check(reported, "terminal PumpEvents reports the original synchronous callback error");
                window.Dispose();
                Released(window, hwnd, "owner can dispose after the recorded callback error");
            }

            for (int cycle = 0; cycle < 3; cycle++)
            {
                using var window = Window(new Element()); nint hwnd = window.Handle; string name = Windows()[hwnd]; int ticks = 0;
                window.Run(visible: false, frameLimit: 2, tick: _ => ticks++);
                window.Run(visible: false, frameLimit: 2, tick: _ => ticks++);
                Check(ticks == 4 && window.LastFrame.Commands > 0, $"normal cycle {cycle + 1}: repeated bounded Run renders and returns");
                window.Dispose(); window.Dispose();
                Released(window, hwnd, $"normal cycle {cycle + 1}: renderer and HWND released");
                Check(!ClassExists(name) && Windows().Keys.ToHashSet().SetEquals(baseline.Keys), $"normal cycle {cycle + 1}: no retained class or HWND");
            }
            Console.WriteLine($"PASS native Windows lifecycle: {checks.Count} checks; hidden HWNDs and real renderer; input messages injected.");
        }
        catch (Exception error)
        {
            failure = error.ToString(); Console.Error.WriteLine("FAIL native Windows lifecycle: " + failure); Environment.ExitCode = 1;
        }
        finally
        {
            string artifacts = Path.GetFullPath(Environment.GetEnvironmentVariable("YOUI_ARTIFACT_DIR") ?? "artifacts");
            Directory.CreateDirectory(artifacts);
            File.WriteAllText(Path.Combine(artifacts, "windows-lifecycle.json"), JsonSerializer.Serialize(new
            {
                status = failure == null ? "passed" : "failed", adapter, checks, error = failure,
                nativeWindow = "hidden Win32 HWND; injected messages", physicalInput = "NotRun"
            }, new JsonSerializerOptions { WriteIndented = true }));
        }
    }

    private sealed class PaintCallback(Action paint) : Element
    {
        protected override void Paint(DrawList list, Rect bounds, float opacity, Theme theme) => paint();
    }
    private sealed class FailingAttachmentRoot(Func<string[]> classes) : Element
    {
        internal string[] CreatedClasses = [];
        internal ITextMeasurer? Measurer;
        internal bool ThrowOnAttach = true;
        protected override void OnDocumentChanged(UiDocument? previous, UiDocument? current)
        {
            if (current == null || !ThrowOnAttach) return;
            Measurer = current.TextMeasurer; CreatedClasses = classes();
            throw new InvalidOperationException("Injected attachment failure after renderer creation.");
        }
    }
    private static Dictionary<nint, string> Windows()
    {
        var result = new Dictionary<nint, string>();
        EnumProc callback = (hwnd, _) =>
        {
            var name = new StringBuilder(256); GetClassName(hwnd, name, name.Capacity);
            if (name.ToString().StartsWith("YoUI_", StringComparison.Ordinal)) result[hwnd] = name.ToString();
            return true;
        };
        if (!EnumThreadWindows(GetCurrentThreadId(), callback, 0)) throw new InvalidOperationException("Cannot enumerate current-thread windows.");
        GC.KeepAlive(callback); return result;
    }
    private static bool ClassExists(string name)
    {
        var info = new WindowClass { Size = (uint)Marshal.SizeOf<WindowClass>() };
        return GetClassInfoEx(GetModuleHandle(null), name, ref info);
    }
    private delegate bool EnumProc(nint hwnd, nint param);
    [StructLayout(LayoutKind.Sequential)] private struct WindowClass
    {
        public uint Size, Style; public nint Proc; public int ClassExtra, WindowExtra;
        public nint Instance, Icon, Cursor, Background, MenuName, ClassName, SmallIcon;
    }
    [DllImport("user32")] private static extern bool EnumThreadWindows(uint thread, EnumProc callback, nint param);
    [DllImport("kernel32")] private static extern uint GetCurrentThreadId();
    [DllImport("kernel32", CharSet = CharSet.Unicode)] private static extern nint GetModuleHandle(string? name);
    [DllImport("user32", CharSet = CharSet.Unicode)] private static extern int GetClassName(nint hwnd, StringBuilder name, int size);
    [DllImport("user32", CharSet = CharSet.Unicode)] private static extern bool GetClassInfoEx(nint instance, string name, ref WindowClass info);
    [DllImport("user32")] private static extern bool IsWindow(nint hwnd);
    [DllImport("user32")] private static extern nint GetCapture();
    [DllImport("user32", CharSet = CharSet.Unicode)] private static extern bool PostMessage(nint hwnd, uint message, nuint wParam, nint lParam);
    [DllImport("user32", CharSet = CharSet.Unicode)] private static extern nint SendMessage(nint hwnd, uint message, nuint wParam, nint lParam);
}
