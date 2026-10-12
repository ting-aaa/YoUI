using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using YoUI;

static class OffscreenTrimTests
{
    public static void RunLogic(Action<string, Action> test, Action<bool, string> require)
    {
        test("offscreen trim exposes an additive 64-bit Cdecl binding", () =>
        {
            var method = typeof(NativeRenderer).GetMethod(nameof(NativeRenderer.TrimOffscreenLayers))!;
            require(method.ReturnType == typeof(void) && method.GetParameters().Length == 0, "public trim signature");
            var native = typeof(NativeRenderer).GetNestedType("Native", BindingFlags.NonPublic)!;
            var binding = native.GetMethod(nameof(NativeRenderer.TrimOffscreenLayers), BindingFlags.Static | BindingFlags.NonPublic)!;
            var import = binding.GetCustomAttribute<DllImportAttribute>()!;
            require(import.Value == "youi_render" && import.EntryPoint == "youi_trim_offscreen_layers" && import.CallingConvention == CallingConvention.Cdecl, "additive native symbol and calling convention");
            require(binding.ReturnType == typeof(int) && binding.GetParameters().Select(p => p.ParameterType).SequenceEqual(new[] { typeof(ulong) }), "native status and 64-bit renderer handle");
        });
        test("offscreen trim rejects disposed renderers before native access", () =>
        {
            var disposed = (NativeRenderer)RuntimeHelpers.GetUninitializedObject(typeof(NativeRenderer));
            require(Capture(disposed.TrimOffscreenLayers) is ObjectDisposedException, "disposed trim must not load or call native code");
        });
        test("offscreen trim rejects foreign managed threads before native access", () =>
        {
            // No device is created: the ownership guard must reject before native lookup.
            var error = OnOtherThread(HandleFixture().TrimOffscreenLayers);
            require(error is InvalidOperationException && error.Message.Contains("creating thread", StringComparison.Ordinal), "foreign-thread trim must fail in the managed ownership guard");
        });
    }

    // Run only against an older ABI-v1 library without the additive trim export.
    // Symbol resolution fails before the fixture handle can reach native code.
    public static void RunMissingApi()
    {
        var error = Capture(HandleFixture().TrimOffscreenLayers);
        Require(error is InvalidOperationException && error.InnerException is EntryPointNotFoundException, "missing offscreen-trim export must have a helpful managed wrapper");
        Require(error!.Message.Contains("youi_trim_offscreen_layers", StringComparison.Ordinal) &&
            error.Message.Contains("matching native library", StringComparison.Ordinal) &&
            error.Message.Contains("ABI v1", StringComparison.Ordinal), "diagnostic identifies the missing additive symbol and deployment remedy");
        Console.WriteLine("PASS managed missing additive offscreen-trim API diagnostic (legacy native library)");
    }

    // Explicit opt-in: real adapter and matching native library, no native window.
    // Report a software adapter as software rather than physical-GPU evidence.
    public static void RunGpu()
    {
        const uint width = 320, height = 160;
        const string text = "Keep glyphs warm";
        const ulong outputBytes = (ulong)width * height * 4;
        var budget = new ResourceBudget(2048UL * 2048 * 4 + outputBytes * 4, 2048UL * 112);
        using var renderer = new NativeRenderer(width, height, 0, budget);
        Console.WriteLine($"GPU offscreen-trim adapter: {renderer.Adapter}");
        var initial = renderer.ResourceUsage;
        renderer.TrimOffscreenLayers(); renderer.TrimOffscreenLayers();
        Require(SameUsage(initial, renderer.ResourceUsage), "empty repeated trim is a no-op");

        // Grow both caches above the coming frame's needs. Trimming must drop even
        // the unused third layer while keeping the enlarged instance buffer.
        var highWater = new DrawList();
        for (int i = 0; i < 3; i++) highWater.BeginLayer(.5f);
        highWater.Rectangle(new(8, 8, 40, 40), new(1, 0, 0));
        for (int i = 0; i < 3; i++) highWater.EndLayer();
        for (int i = 0; i < 1025; i++) highWater.Rectangle(new(0, 0, 1, 1), new(0, 0, 0, 0));
        renderer.Render(highWater);
        var grown = renderer.ResourceUsage;
        Require(grown.LayerCount == 3 && grown.LayerBytes == outputBytes * 3 && grown.InstanceBufferBytes == 2048UL * 112, "test grows three layer targets and the instance buffer");

        using var image = renderer.UploadImage(2, 2, Enumerable.Range(0, 4).SelectMany(_ => new byte[] { 255, 0, 255, 255 }).ToArray());
        var measured = renderer.Measure(text, 20, 240);
        var layout = renderer.Layout(text, 20, 240);
        var frame = new DrawList();
        frame.BeginLayer(.5f);
        frame.Rectangle(new(8, 8, 120, 96), new(1, 0, 0, .5f));
        frame.BeginLayer(.5f);
        frame.Rectangle(new(40, 8, 120, 96), new(0, 1, 0, .5f));
        frame.EndLayer(); frame.EndLayer();
        frame.Text(new(8, 112, 240, 40), text, new(1, 1, 1), 20);
        frame.Image(new(264, 112, 32, 32), image);
        var first = renderer.Render(frame);
        var warm = renderer.Render(frame);
        Require(first.AtlasGlyphs > 0 && warm.AtlasGlyphs == first.AtlasGlyphs && warm.TextCacheHits == 1 && warm.Layers == 2, "test warms glyphs and nested transparency");
        var retained = renderer.ResourceUsage;
        Require(SameUsage(grown, retained), "a smaller frame retains the larger scratch pool and instance buffer");
        string directory = Path.Combine("artifacts", "offscreen-trim"); Directory.CreateDirectory(directory);
        string baselinePath = Path.Combine(directory, "managed-before-trim.png"); renderer.SavePng(baselinePath);
        var baseline = TextInputTests.ReadRgbaPng(baselinePath);
        Require(baseline.Width == width && baseline.Height == height, "readback dimensions");
        RequirePixelNear(baseline.Pixels, (int)width, 20, 40, new byte[] { 137, 0, 0, 64 }, "outer layer translucent red");
        RequirePixelNear(baseline.Pixels, (int)width, 144, 40, new byte[] { 0, 99, 0, 32 }, "nested layer translucent green");
        RequirePixelNear(baseline.Pixels, (int)width, 80, 40, new byte[] { 120, 99, 0, 80 }, "ordered nested premultiplied overlap");
        Require(baseline.Pixels.AsSpan((40 * (int)width + 220) * 4, 4).SequenceEqual(new byte[4]), "untouched output remains transparent");
        Require(baseline.Pixels.AsSpan((128 * (int)width + 280) * 4, 4).SequenceEqual(new byte[] { 255, 0, 255, 255 }), "live image renders before trim");
        Require(Enumerable.Range(112, 40).Any(y => Enumerable.Range(8, 240).Any(x => baseline.Pixels[(y * (int)width + x) * 4 + 3] > 0)), "glyphs produce visible pixels");

        // No wait or readback between submission and dropping all scratch targets.
        // The native test also checks the renderer's retained last-stats object;
        // the managed API exposes returned FrameStats snapshots, not a stats getter.
        var pending = renderer.Render(frame);
        renderer.TrimOffscreenLayers(); renderer.TrimOffscreenLayers();
        var trimmed = renderer.ResourceUsage;
        Require(trimmed.LayerBytes == 0 && trimmed.LayerCount == 0, "trim drops all current and unused scratch targets");
        Require(SameNonLayerUsage(retained, trimmed), "trim preserves atlas, output and grown instance-buffer payload");
        Require(SameWorkload(warm, pending), "queued frame returns unchanged workload counters");
        var actualBudget = renderer.ResourceBudget;
        Require(actualBudget.MaxTextureBytes == budget.MaxTextureBytes && actualBudget.MaxInstanceBufferBytes == budget.MaxInstanceBufferBytes, "trim preserves configured budgets");
        string pendingPath = Path.Combine(directory, "managed-pending-after-trim.png"); renderer.SavePng(pendingPath);
        Require(baseline.Pixels.SequenceEqual(TextInputTests.ReadRgbaPng(pendingPath).Pixels), "trim preserves the last submitted output without an intervening GPU wait");
        Require(renderer.Measure(text, 20, 240) == measured && renderer.Layout(text, 20, 240).Cells.SequenceEqual(layout.Cells), "measurement, cached layout and snapshots survive trim");

        // A rejected frame must not recreate even part of the scratch pool.
        var excessive = new DrawList();
        for (int i = 0; i < 4; i++) excessive.BeginLayer(.5f);
        excessive.Rectangle(new(0, 0, 16, 16), new(0, 0, 1));
        for (int i = 0; i < 4; i++) excessive.EndLayer();
        var budgetError = Capture(() => renderer.Render(excessive));
        Require(budgetError is InvalidOperationException && budgetError.Message.Contains("budget", StringComparison.OrdinalIgnoreCase), "trim does not bypass the texture budget");
        Require(SameUsage(trimmed, renderer.ResourceUsage), "rejected post-trim frame leaves allocations untouched");

        var flat = new DrawList(); flat.Image(new(264, 112, 32, 32), image);
        var flatStats = renderer.Render(flat);
        Require(flatStats.Layers == 0 && flatStats.AtlasGlyphs == warm.AtlasGlyphs, "trim retains glyph residency even when the next frame contains no text or layers");
        Require(SameUsage(trimmed, renderer.ResourceUsage), "a flat frame does not recreate layer targets");
        for (int cycle = 0; cycle < 3; cycle++)
        {
            var restored = renderer.Render(frame);
            Require(SameWorkload(warm, restored), "recreated nested layers retain workload, glyph count and warm shaping cache");
            var recreated = renderer.ResourceUsage;
            Require(recreated.LayerCount == 2 && recreated.LayerBytes == outputBytes * 2 && SameNonLayerUsage(retained, recreated), "later admitted frames recreate only the needed layer targets");
            renderer.TrimOffscreenLayers();
            Require(SameUsage(trimmed, renderer.ResourceUsage), "repeated queued trim/render cycles reclaim exactly the layer payload");
        }
        string restoredPath = Path.Combine(directory, "managed-recreated-after-trim.png"); renderer.SavePng(restoredPath);
        Require(baseline.Pixels.SequenceEqual(TextInputTests.ReadRgbaPng(restoredPath).Pixels), "recreated nested transparency, glyphs and live image pixels match exactly");
        var wrongThread = OnOtherThread(renderer.TrimOffscreenLayers);
        Require(wrongThread is InvalidOperationException && wrongThread.Message.Contains("creating thread", StringComparison.Ordinal), "live renderer enforces the managed owner thread");
        Require(SameUsage(trimmed, renderer.ResourceUsage), "rejected foreign-thread trim preserves resource usage");
        renderer.Dispose();
        Require(Capture(renderer.TrimOffscreenLayers) is ObjectDisposedException, "live renderer rejects trim after disposal");
        Console.WriteLine($"PASS managed offscreen-trim binding, all-layer reclamation, pending output, nested transparent recreation, retained glyphs/images/caches/budgets, and lifetime guards; screenshots: {directory}");
    }

    private static NativeRenderer HandleFixture()
    {
        var renderer = (NativeRenderer)RuntimeHelpers.GetUninitializedObject(typeof(NativeRenderer));
        typeof(NativeRenderer).GetField("handle", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(renderer, ulong.MaxValue);
        typeof(NativeRenderer).GetField("ownerThread", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(renderer, Environment.CurrentManagedThreadId);
        return renderer;
    }
    private static Exception? Capture(Action operation)
    {
        try { operation(); return null; } catch (Exception ex) { return ex; }
    }
    private static Exception? OnOtherThread(Action operation)
    {
        Exception? error = null;
        var thread = new Thread(() => error = Capture(operation)); thread.Start(); thread.Join();
        return error;
    }
    private static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static bool SameNonLayerUsage(ResourceUsage a, ResourceUsage b) =>
        a.AtlasBytes == b.AtlasBytes && a.OutputBytes == b.OutputBytes && a.InstanceBufferBytes == b.InstanceBufferBytes && a.Reserved == b.Reserved;
    private static bool SameUsage(ResourceUsage a, ResourceUsage b) =>
        SameNonLayerUsage(a, b) && a.LayerBytes == b.LayerBytes && a.LayerCount == b.LayerCount;
    private static bool SameWorkload(FrameStats a, FrameStats b) =>
        a.Commands == b.Commands && a.Instances == b.Instances && a.DrawCalls == b.DrawCalls && a.Layers == b.Layers &&
        a.UploadBytes == b.UploadBytes && a.AtlasGlyphs == b.AtlasGlyphs && a.TextCacheHits == b.TextCacheHits;
    private static void RequirePixelNear(byte[] pixels, int width, int x, int y, byte[] expected, string message)
    {
        var actual = pixels.AsSpan((y * width + x) * 4, 4);
        for (int channel = 0; channel < 4; channel++)
            Require(Math.Abs(actual[channel] - expected[channel]) <= 4, $"{message}: channel {channel}, expected {expected[channel]}, got {actual[channel]}");
    }
}
