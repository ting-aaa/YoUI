using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using YoUI;

static class GlyphResidencyTests
{
    public static void RunLogic(Action<string, Action> test, Action<bool, string> require)
    {
        test("glyph purge exposes an additive 64-bit Cdecl binding", () =>
        {
            var method = typeof(NativeRenderer).GetMethod(nameof(NativeRenderer.PurgeGlyphResidency))!;
            require(method.ReturnType == typeof(void) && method.GetParameters().Length == 0, "public purge signature");
            var native = typeof(NativeRenderer).GetNestedType("Native", BindingFlags.NonPublic)!;
            var binding = native.GetMethod(nameof(NativeRenderer.PurgeGlyphResidency), BindingFlags.Static | BindingFlags.NonPublic)!;
            var import = binding.GetCustomAttribute<DllImportAttribute>()!;
            require(import.Value == "youi_render" && import.EntryPoint == "youi_purge_glyph_residency" && import.CallingConvention == CallingConvention.Cdecl, "additive native symbol and calling convention");
            require(binding.ReturnType == typeof(int) && binding.GetParameters().Select(p => p.ParameterType).SequenceEqual(new[] { typeof(ulong) }), "native status and 64-bit renderer handle");
        });
        test("glyph purge rejects disposed renderers before native access", () =>
        {
            var disposed = (NativeRenderer)RuntimeHelpers.GetUninitializedObject(typeof(NativeRenderer));
            var error = Capture(disposed.PurgeGlyphResidency);
            require(error is ObjectDisposedException, "disposed purge must not load or call native code");
        });
        test("glyph purge rejects foreign managed threads before native access", () =>
        {
            // No device or native handle is created. This fixture can only pass the
            // managed ownership check on the constructing test thread.
            var renderer = HandleFixture();
            var error = OnOtherThread(renderer.PurgeGlyphResidency);
            require(error is InvalidOperationException && error.Message.Contains("creating thread", StringComparison.Ordinal), "foreign-thread purge must fail in the managed ownership guard");
        });
    }

    // Run only with an older ABI-v1 native library that lacks the additive symbol.
    // The fixture needs no GPU because symbol lookup fails before native invocation.
    public static void RunMissingApi()
    {
        var error = Capture(HandleFixture().PurgeGlyphResidency);
        Require(error is InvalidOperationException && error.InnerException is EntryPointNotFoundException, "missing glyph-residency export must have a helpful managed wrapper");
        Require(error!.Message.Contains("youi_purge_glyph_residency", StringComparison.Ordinal) &&
            error.Message.Contains("matching native library", StringComparison.Ordinal) &&
            error.Message.Contains("ABI v1", StringComparison.Ordinal), "diagnostic identifies the missing additive symbol and deployment remedy");
        Console.WriteLine("PASS managed missing additive glyph-residency API diagnostic (legacy native library)");
    }

    // Explicit opt-in: creates an actual adapter and loads the matching native library.
    // Software adapters are reported verbatim; this does not exercise a native window.
    public static void RunGpu()
    {
        const uint width = 320, height = 160;
        const string text = "Hello glyph cache";
        const ulong outputBytes = (ulong)width * height * 4;
        var budget = new ResourceBudget(2048UL * 2048 * 4 + outputBytes * 2, 1024UL * 112);
        using var renderer = new NativeRenderer(width, height, 0, budget);
        Console.WriteLine($"GPU glyph-residency adapter: {renderer.Adapter}");
        var initialUsage = renderer.ResourceUsage;
        renderer.PurgeGlyphResidency(); renderer.PurgeGlyphResidency();
        Require(SameUsage(initialUsage, renderer.ResourceUsage), "empty repeated purge preserves allocations");
        using var liveImage = renderer.UploadImage(2, 2, new byte[] { 255, 0, 255, 255 }.Repeat(4));
        var measured = renderer.Measure(text, 24, 300);
        var layout = renderer.Layout(text, 24, 300);
        var frame = new DrawList();
        frame.Rectangle(new(0, 0, width, height), new(0, 0, 0));
        frame.BeginLayer(.75f);
        frame.Text(new(8, 8, 300, 60), text, new(1, 1, 1), 24);
        frame.EndLayer();
        frame.Image(new(8, 112, 32, 32), liveImage);
        var first = renderer.Render(frame);
        Require(first.AtlasGlyphs > 0 && first.Layers == 1, "test requires resident Latin glyphs and a retained layer");
        var warm = renderer.Render(frame);
        Require(warm.TextCacheHits == 1 && warm.AtlasGlyphs == first.AtlasGlyphs, "warm frame reuses cached shaping and residency");
        var retainedUsage = renderer.ResourceUsage;
        Require(retainedUsage.LayerCount == 1 && retainedUsage.LayerBytes == outputBytes, "test exercises pooled GPU allocations");
        string directory = Path.Combine("artifacts", "glyph-residency"); Directory.CreateDirectory(directory);
        string baselinePath = Path.Combine(directory, "managed-before-purge.png");
        renderer.SavePng(baselinePath);
        var baseline = TextInputTests.ReadRgbaPng(baselinePath);
        Require(baseline.Pixels.Take((int)width * 68 * 4).Where((_, index) => index % 4 != 3).Any(b => b > 0), "text produces visible pixels before purge");

        // Purge and overwrite reusable atlas space after submission, with no readback
        // or GPU wait in between. The already submitted frame must remain unchanged.
        renderer.Render(frame);
        renderer.PurgeGlyphResidency(); renderer.PurgeGlyphResidency();
        using var laterImage = renderer.UploadImage(3, 3, new byte[] { 0, 255, 0, 255 }.Repeat(9));
        string pendingPath = Path.Combine(directory, "managed-pending-after-purge.png");
        renderer.SavePng(pendingPath);
        Require(baseline.Pixels.SequenceEqual(TextInputTests.ReadRgbaPng(pendingPath).Pixels), "purge and later uploads preserve the last submitted pixels");
        Require(SameUsage(retainedUsage, renderer.ResourceUsage), "purge preserves retained GPU allocations");
        var actualBudget = renderer.ResourceBudget;
        Require(actualBudget.MaxTextureBytes == budget.MaxTextureBytes && actualBudget.MaxInstanceBufferBytes == budget.MaxInstanceBufferBytes, "purge preserves configured budgets");
        Require(renderer.Measure(text, 24, 300) == measured && renderer.Layout(text, 24, 300).Cells.SequenceEqual(layout.Cells), "measurement and layout snapshots remain stable");

        var images = new DrawList();
        images.Rectangle(new(0, 0, width, height), new(0, 0, 0));
        images.Image(new(8, 112, 32, 32), liveImage);
        images.Image(new(280, 112, 32, 32), laterImage);
        Require(renderer.Render(images).AtlasGlyphs == 0, "purge empties glyph residency without releasing live images");
        string imagesPath = Path.Combine(directory, "managed-live-images.png"); renderer.SavePng(imagesPath);
        var imagePixels = TextInputTests.ReadRgbaPng(imagesPath).Pixels;
        Require(imagePixels.AsSpan((128 * (int)width + 24) * 4, 4).SequenceEqual(new byte[] { 255, 0, 255, 255 }), "pre-purge live image pixels survive");
        Require(imagePixels.AsSpan((128 * (int)width + 296) * 4, 4).SequenceEqual(new byte[] { 0, 255, 0, 255 }), "post-purge image upload remains valid");

        var restored = renderer.Render(frame);
        Require(restored.AtlasGlyphs == first.AtlasGlyphs && restored.TextCacheHits == 1, "cached layout rehydrates all required glyphs after purge");
        for (int cycle = 0; cycle < 3; cycle++)
        {
            renderer.PurgeGlyphResidency();
            var repeated = renderer.Render(frame);
            Require(repeated.AtlasGlyphs == first.AtlasGlyphs && repeated.TextCacheHits == 1, "repeated queued purge/render cycles retain shaping cache behavior");
        }
        string restoredPath = Path.Combine(directory, "managed-after-rehydration.png"); renderer.SavePng(restoredPath);
        Require(baseline.Pixels.SequenceEqual(TextInputTests.ReadRgbaPng(restoredPath).Pixels), "rehydrated text and live image pixels match exactly");
        Require(SameUsage(retainedUsage, renderer.ResourceUsage), "repeated cycles preserve resource usage");
        var wrongThread = OnOtherThread(renderer.PurgeGlyphResidency);
        Require(wrongThread is InvalidOperationException && wrongThread.Message.Contains("creating thread", StringComparison.Ordinal), "live renderer enforces managed owner thread");
        renderer.Dispose();
        Require(Capture(renderer.PurgeGlyphResidency) is ObjectDisposedException, "live renderer rejects purge after disposal");
        Require(layout.Cells.Count > 0 && layout.Text == text, "managed layout snapshot outlives purge and renderer disposal");
        Console.WriteLine($"PASS managed glyph-residency binding, idempotence, queued pixels, cache rehydration, live images, budgets and lifetime guards; screenshots: {directory}");
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
    private static bool SameUsage(ResourceUsage a, ResourceUsage b) =>
        a.AtlasBytes == b.AtlasBytes && a.OutputBytes == b.OutputBytes && a.LayerBytes == b.LayerBytes &&
        a.InstanceBufferBytes == b.InstanceBufferBytes && a.LayerCount == b.LayerCount && a.Reserved == b.Reserved;
    private static byte[] Repeat(this byte[] pixel, int count) => Enumerable.Range(0, count).SelectMany(_ => pixel).ToArray();
}
