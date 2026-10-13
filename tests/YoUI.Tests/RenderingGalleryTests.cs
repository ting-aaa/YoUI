using System.Numerics;
using System.Text;
using YoUI;
using YoUI.Showcase;

internal static class RenderingGalleryTests
{
    private const uint Coral = 0xF06A52, Gold = 0xF2C85B, Teal = 0x59BDA4, Blue = 0x688FE5;
    private const uint Center = 0x243C50, Backdrop = 0x102033;
    private static readonly string ArtifactDirectory = Path.Combine("artifacts", "gallery-rendering");

    public static void RunLogic(Action<string, Action> test, Action<bool, string> require)
    {
        test("rendering gallery has a GPU-free placeholder and paint-only settings", () =>
        {
            var gallery = new RenderingGallery(); var doc = new UiDocument(gallery, new TestMeasurer(), 480, 480);
            var frame = doc.BuildFrame(); int layouts = doc.LayoutPasses;
            require(gallery.Clipped && gallery.GroupOpacity == .65f, "rendering controls have predictable defaults");
            require(Encoding.UTF8.GetString(frame.TextBytes).Contains("Rendering assets unavailable"), "unbound gallery explains how the host supplies its resources");
            require(!frame.Commands.ToArray().Any(c => c.Kind is 4 or 5 or 7 or 8), "placeholder never emits image or layer commands");
            gallery.Clipped = false; require(doc.NeedsRender, "clipping invalidates paint"); doc.BuildFrame();
            gallery.Clipped = false; require(!doc.NeedsRender, "same clipping value leaves a clean frame cached");
            gallery.GroupOpacity = .4f; require(doc.NeedsRender, "opacity invalidates paint"); doc.BuildFrame();
            gallery.GroupOpacity = .4f; require(!doc.NeedsRender, "same opacity leaves a clean frame cached");
            foreach (float invalid in new[] { -.01f, 1.01f, float.NaN, float.NegativeInfinity, float.PositiveInfinity })
            {
                Throws<ArgumentOutOfRangeException>(() => gallery.GroupOpacity = invalid);
                require(gallery.GroupOpacity == .4f && !doc.NeedsRender, "invalid opacity changes neither value nor frame");
            }
            gallery.GroupOpacity = 0; doc.BuildFrame(); gallery.GroupOpacity = 1; doc.BuildFrame();
            gallery.SetResources(null); require(doc.NeedsRender, "explicit host unbind refreshes the placeholder"); doc.BuildFrame();
            require(doc.LayoutPasses == layouts, "rendering-only settings never request layout");
            Throws<ArgumentNullException>(() => new GalleryRenderingResources(null!));
        });
    }

    // Explicit opt-in: these tests submit the real sample painter to a real GPU adapter.
    public static void RunGpu()
    {
        Directory.CreateDirectory(ArtifactDirectory);
        PixelsAndLifetime();
        PartialUploadFailure();
    }

    private static void PixelsAndLifetime()
    {
        var gallery = new RenderingGallery();
        UiDocument doc;
        var staleOriginal = new DrawList(); var staleReplacement = new DrawList();
        using (var renderer = new NativeRenderer(480, 480))
        using (var original = new GalleryRenderingResources(renderer))
        {
            Console.WriteLine($"Gallery rendering adapter: {renderer.Adapter}");
            gallery.SetResources(original); doc = new UiDocument(gallery, renderer, 480, 480);
            var initial = Render(renderer, doc, "dark-clipped");
            staleOriginal.Append(doc.BuildFrame(), Vector2.Zero);
            ImageAndMesh(initial);
            NineSlice(initial, 20, 132); NineSlice(initial, 172, 288);
            Pixel(initial, 440, 450, 0x121B29, "dark theme surface");
            Group(initial, .65f);
            ClippedEdges(initial, true, .65f);
            var repeated = Render(renderer, doc, "dark-repeated");
            Require(initial.Pixels.SequenceEqual(repeated.Pixels), "idle gallery renders identical pixels");
            Console.WriteLine("PASS gallery image quadrants, rotated mesh UVs, fixed nine-slice borders, nested layers and repeated pixels");

            gallery.Clipped = false;
            var unclipped = Render(renderer, doc, "dark-unclipped");
            Group(unclipped, .65f); ClippedEdges(unclipped, false, .65f);
            gallery.Clipped = true; gallery.GroupOpacity = 0;
            var transparent = Render(renderer, doc, "zero-opacity");
            Group(transparent, 0); ClippedEdges(transparent, true, 0); ImageAndMesh(transparent);
            gallery.GroupOpacity = 1;
            var opaque = Render(renderer, doc, "full-opacity");
            Group(opaque, 1); ClippedEdges(opaque, true, 1);
            gallery.GroupOpacity = .65f;
            Require(initial.Pixels.SequenceEqual(Render(renderer, doc, "restored-opacity").Pixels), "restoring clipping and opacity restores the same image");
            Console.WriteLine("PASS gallery clipping on/off and group opacity zero/one/intermediate with uniform rectangle overlap and nested half-opacity image");

            foreach (int width in new[] { 640, 320 })
            {
                renderer.Resize((uint)width, 520); doc.Resize(width, 520); doc.Theme = Theme.Light;
                var resized = Render(renderer, doc, $"light-{width}");
                Require(resized.Width == width && resized.Height == 520, "readback follows resized target dimensions");
                Pixel(resized, width - 20, 480, 0xFFFFFF, "light theme surface after resize");
                ImageAndMesh(resized); NineSlice(resized, 20, 132); NineSlice(resized, 172, width - 192);
                Group(resized, .65f);
            }
            renderer.Resize(480, 480); doc.Resize(480, 480); doc.Theme = Theme.Dark;
            Require(initial.Pixels.SequenceEqual(Render(renderer, doc, "dark-resized-back").Pixels), "resize and theme round trip preserve resource contents and composition");
            Console.WriteLine("PASS gallery dark/light themes and wide/narrow/resized-back image, mesh and nine-slice pixels");

            using var replacement = new GalleryRenderingResources(renderer);
            gallery.SetResources(replacement); original.Dispose(); original.Dispose();
            Require(original.IsDisposed && original.Texture.IsDisposed && original.Border.IsDisposed, "host disposes both old resources before renderer shutdown");
            Require(!replacement.IsDisposed, "replacing resources does not dispose the new borrowed assets");
            Throws<ObjectDisposedException>(() => gallery.SetResources(original));
            var switched = Render(renderer, doc, "replacement-resources");
            // Relocated atlas UVs can round interpolated RGB by one 8-bit step; geometry/alpha must stay exact.
            Require(initial.Width == switched.Width && initial.Height == switched.Height &&
                initial.Pixels.Select((value, index) => Math.Abs(value - switched.Pixels[index]) <= (index % 4 == 3 ? 0 : 1)).All(value => value),
                "disposed-resource binding rejection preserves the replacement, within one RGB step for relocated atlas sampling");
            staleReplacement.Append(doc.BuildFrame(), Vector2.Zero);
            RejectStale(renderer, staleOriginal, switched, "released-original");

            gallery.SetResources(null);
            var placeholder = Render(renderer, doc, "unbound-placeholder");
            Pixel(placeholder, 48, 98, 0x121B29, "unbinding removes the old image");
            Pixel(placeholder, 100, 230, 0x121B29, "unbinding removes the old nine-slice");
            Require(!replacement.IsDisposed, "the painter borrows assets and never disposes them on unbind");
            gallery.SetResources(replacement);
            Require(switched.Pixels.SequenceEqual(Render(renderer, doc, "rebound-resources").Pixels), "borrowed assets remain usable after unbind/rebind");
            gallery.SetResources(null); replacement.Dispose(); replacement.Dispose();
            Require(replacement.IsDisposed && replacement.Texture.IsDisposed && replacement.Border.IsDisposed, "replacement disposal releases both images idempotently");
            Throws<ObjectDisposedException>(() => gallery.SetResources(replacement));
            var disposed = Render(renderer, doc, "disposed-placeholder");
            Require(placeholder.Pixels.SequenceEqual(disposed.Pixels), "rejected disposed resources leave the placeholder intact");
            RejectStale(renderer, staleReplacement, disposed, "released-replacement");
        }

        // Reuse the retained painter/document, but explicitly replace all renderer-owned assets and text measurement.
        using (var fresh = new NativeRenderer(480, 480))
        using (var resources = new GalleryRenderingResources(fresh))
        {
            gallery.SetResources(resources); doc.RebindTextMeasurer(fresh);
            var rebuilt = Render(fresh, doc, "fresh-renderer");
            ImageAndMesh(rebuilt); NineSlice(rebuilt, 20, 132); NineSlice(rebuilt, 172, 288); Group(rebuilt, .65f);
            RejectStale(fresh, staleOriginal, rebuilt, "foreign-original");
            RejectStale(fresh, staleReplacement, rebuilt, "foreign-replacement");
            Require(rebuilt.Pixels.SequenceEqual(Render(fresh, doc, "fresh-after-rejections").Pixels), "fresh reupload and rebind remain usable after rejecting stale frames");
            gallery.SetResources(null);
        }
        Console.WriteLine("PASS gallery borrowed resource switching, unbind/rebind, disposal, stale-frame rejection and fresh-renderer reupload");
    }

    private static void PartialUploadFailure()
    {
        // Images occupy the existing fixed atlas; lowering the allocation budget cannot reject just the second upload.
        // A 2012 x 2046 image leaves exactly one padded 32 x 32 slot in the 2048-square atlas.
        const ulong atlasBytes = 2048UL * 2048 * 4;
        using var renderer = new NativeRenderer(64, 64, 0, new ResourceBudget(atlasBytes + 64UL * 64 * 4, 1024UL * 112));
        using var blocker = renderer.UploadImage(2012, 2046, Solid(2012, 2046, 0x000000));
        using (var onlySlot = renderer.UploadImage(32, 32, Solid(32, 32, Coral)))
        {
            var full = Throws<NativeRendererException>(() => renderer.UploadImage(32, 32, Solid(32, 32, Gold)));
            Require(full.NativeStatus == -1 && full.Message.Contains("atlas budget exhausted", StringComparison.OrdinalIgnoreCase), "fixture leaves exactly one image slot, independent of a larger texture budget");
        }
        for (int cycle = 0; cycle < 3; cycle++)
        {
            var failure = Throws<NativeRendererException>(() => new GalleryRenderingResources(renderer));
            Require(failure.NativeStatus == -1 && failure.Message.Contains("atlas budget exhausted", StringComparison.OrdinalIgnoreCase), "second gallery upload must fail after the first succeeds");
            // A leaked first image would consume the only reusable slot and make this upload fail.
            using var recovered = renderer.UploadImage(32, 32, Solid(32, 32, 0xDB34E8));
            var frame = new DrawList(); frame.Image(new(0, 0, 64, 64), recovered); renderer.Render(frame);
            Pixel(Snapshot(renderer, $"partial-upload-recovery-{cycle}"), 32, 32, 0xDB34E8, "constructor failure releases its first upload for immediate reuse");
        }
        Require(!blocker.IsDisposed, "failed gallery construction never releases an unrelated live image");
        Console.WriteLine("PASS gallery second-upload failure releases the first image and permits immediate pixel-verified reuse on three attempts");
    }

    private static void ImageAndMesh((int Width, int Height, byte[] Pixels) image)
    {
        Pixel(image, 48, 74, Coral, "image top-left UV quadrant");
        Pixel(image, 104, 74, Gold, "image top-right UV quadrant");
        Pixel(image, 48, 122, Teal, "image bottom-left UV quadrant");
        Pixel(image, 104, 122, Blue, "image bottom-right UV quadrant");
        // Interior barycentric points of the sample's two triangles: these deliberately differ from the axis-aligned image orientation.
        Pixel(image, 274, 76, Coral, "rotated mesh UV top-left lies near screen top-right");
        Pixel(image, 273, 123, Gold, "rotated mesh UV top-right lies near screen bottom-right");
        Pixel(image, 208, 75, Teal, "rotated mesh UV bottom-left lies near screen top-left");
        Pixel(image, 202, 117, Blue, "rotated mesh UV bottom-right lies near screen bottom-left");
    }

    private static void NineSlice((int Width, int Height, byte[] Pixels) image, int x, int width)
    {
        const int y = 194;
        Pixel(image, x + 3, y + 3, Coral, $"{width}-wide nine-slice top-left corner");
        Pixel(image, x + width - 4, y + 3, Gold, $"{width}-wide nine-slice top-right corner");
        Pixel(image, x + 3, y + 60, Teal, $"{width}-wide nine-slice bottom-left corner");
        Pixel(image, x + width - 4, y + 60, Blue, $"{width}-wide nine-slice bottom-right corner");
        // Probe the fixed border and the stretched center. Leave one source texel of room
        // inside the center because linear filtering widens its color transition as the slice stretches.
        int inner = 8 + (int)Math.Ceiling((width - 16) / 16.0);
        Pixel(image, x + 5, y + 16, Coral, $"{width}-wide left border remains eight DIP");
        Pixel(image, x + inner, y + 16, Center, $"{width}-wide left center starts after eight DIP");
        Pixel(image, x + width - 6, y + 16, Gold, $"{width}-wide right border remains eight DIP");
        Pixel(image, x + width - inner - 1, y + 16, Center, $"{width}-wide right center starts after eight DIP");
        Pixel(image, x + width / 3, y + 5, Coral, $"{width}-wide top border remains eight DIP");
        Pixel(image, x + width / 3, y + 11, Center, $"{width}-wide top center starts after eight DIP");
        Pixel(image, x + width / 3, y + 58, Teal, $"{width}-wide bottom border remains eight DIP");
        Pixel(image, x + width / 3, y + 52, Center, $"{width}-wide bottom center starts after eight DIP");
        Pixel(image, x + width / 2, y + 32, Center, $"{width}-wide stretched center");
    }

    private static void Group((int Width, int Height, byte[] Pixels) image, float opacity)
    {
        uint expected = Blend(Coral, Backdrop, opacity);
        Pixel(image, 70, 350, expected, "group first rectangle only");
        Pixel(image, 160, 350, expected, "group overlapping rectangles have uniform opacity");
        Pixel(image, 230, 350, expected, "group second rectangle only");
        Pixel(image, 280, 326, Blend(Coral, Backdrop, opacity * .5f), "nested half-opacity image without rectangle underneath");
        Pixel(image, 280, 380, Blend(Blend(Teal, Coral, .5f), Backdrop, opacity), "nested image blends over a rectangle before outer group compositing");
    }

    private static void ClippedEdges((int Width, int Height, byte[] Pixels) image, bool clipped, float opacity)
    {
        Pixel(image, 24, 350, clipped ? Backdrop : Blend(Coral, Backdrop, opacity), "left clipping edge");
        Pixel(image, 314, 326, clipped ? Backdrop : Blend(Gold, Backdrop, opacity * .5f), "right clipping edge");
        Pixel(image, 280, 314, clipped ? Backdrop : Blend(Coral, Backdrop, opacity * .5f), "top clipping edge");
        Pixel(image, 280, 410, clipped ? Backdrop : Blend(Teal, Backdrop, opacity * .5f), "bottom clipping edge");
    }

    private static (int Width, int Height, byte[] Pixels) Render(NativeRenderer renderer, UiDocument doc, string name)
    {
        renderer.Render(doc.BuildFrame()); return Snapshot(renderer, name);
    }
    private static (int Width, int Height, byte[] Pixels) Snapshot(NativeRenderer renderer, string name)
    {
        string path = Path.Combine(ArtifactDirectory, name + ".png"); renderer.SavePng(path); return TextInputTests.ReadRgbaPng(path);
    }
    private static void RejectStale(NativeRenderer renderer, DrawList stale, (int Width, int Height, byte[] Pixels) expected, string name)
    {
        Require(Throws<NativeRendererException>(() => renderer.Render(stale)).NativeStatus == -1, "stale gallery resources fail with an ordinary invalid-resource error");
        Require(expected.Pixels.SequenceEqual(Snapshot(renderer, name + "-preserved").Pixels), "stale gallery frame rejection preserves the last submitted pixels");
    }
    private static void Pixel((int Width, int Height, byte[] Pixels) image, int x, int y, uint expected, string message)
    {
        Require(x >= 0 && x < image.Width && y >= 0 && y < image.Height, "pixel probe must lie inside readback");
        var actual = image.Pixels.AsSpan((y * image.Width + x) * 4, 4);
        int red = (int)((expected >> 16) & 255), green = (int)((expected >> 8) & 255), blue = (int)(expected & 255);
        Require(Math.Abs(actual[0] - red) <= 3 && Math.Abs(actual[1] - green) <= 3 && Math.Abs(actual[2] - blue) <= 3 && actual[3] == 255,
            $"{message} at ({x},{y}): expected #{expected:X6}FF, got #{actual[0]:X2}{actual[1]:X2}{actual[2]:X2}{actual[3]:X2}");
    }
    private static uint Blend(uint foreground, uint background, float opacity)
    {
        // The renderer composites linear-light premultiplied colors and encodes its sRGB output.
        static double Linear(uint channel) { double c = channel / 255.0; return c <= .04045 ? c / 12.92 : Math.Pow((c + .055) / 1.055, 2.4); }
        static uint Srgb(double c) => (uint)Math.Clamp((int)Math.Round((c <= .0031308 ? c * 12.92 : 1.055 * Math.Pow(c, 1 / 2.4) - .055) * 255), 0, 255);
        uint result = 0;
        foreach (int shift in new[] { 16, 8, 0 }) result |= Srgb(Linear((foreground >> shift) & 255) * opacity + Linear((background >> shift) & 255) * (1 - opacity)) << shift;
        return result;
    }
    private static byte[] Solid(int width, int height, uint rgb)
    {
        var pixels = new byte[checked(width * height * 4)];
        for (int offset = 0; offset < pixels.Length; offset += 4)
        {
            pixels[offset] = (byte)(rgb >> 16); pixels[offset + 1] = (byte)(rgb >> 8); pixels[offset + 2] = (byte)rgb; pixels[offset + 3] = 255;
        }
        return pixels;
    }
    private static T Throws<T>(Action action) where T : Exception
    {
        try { action(); } catch (T error) { return error; }
        throw new Exception($"Expected {typeof(T).Name}");
    }
    private static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
}
