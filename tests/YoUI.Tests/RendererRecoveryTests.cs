using YoUI;

static class RendererRecoveryTests
{
    private static T Throws<T>(Action action) where T : Exception
    {
        try { action(); } catch (T error) { return error; }
        throw new Exception($"Expected {typeof(T).Name}");
    }
    private static void Require(bool value, string message) { if (!value) throw new Exception(message); }
    public static void RunLogic(Action<string, Action> test, Action<bool, string> require)
    {
        test("provider rebind clears stale commands and refreshes measurement, paint and geometry", () =>
        {
            var first = new Provider(5); var second = new Provider(15);
            var root = new Element(); var label = root.Add(new Label("abc"));
            var editor = root.Add(new TextBox { Text = "abc", Transform = RectTransform.Fixed(0, 40, 160, 40) });
            var doc = new UiDocument(root, first, 240, 120);
            var frame = doc.BuildFrame(); doc.Semantics();
            int layouts = doc.LayoutPasses, paints = doc.PaintPasses, calls = first.Measures + first.Layouts;
            float oldWidth = label.DesiredSize.X;
            require(frame.Commands.Length > 0 && !doc.NeedsRender, "warm document fixture");
            doc.RebindTextMeasurer(second);
            require(frame.Commands.Length == 0 && doc.PendingUpdates == DirtyFlags.All, "cached command arena cleared; all stages invalidated");
            doc.BuildFrame(); doc.Semantics();
            require(doc.LayoutPasses == layouts + 1 && doc.PaintPasses == paints + 1 && label.DesiredSize.X > oldWidth, "new measurement and paint");
            require(second.Measures > 0 && second.Layouts > 0 && first.Measures + first.Layouts == calls, "text editing geometry also uses replacement");
            require(!first.Disposed && !second.Disposed, "document does not dispose host-owned providers");
            require(doc.PendingUpdates == DirtyFlags.None, "rebind consumed cleanly");
            doc.RebindTextMeasurer(second);
            require(doc.NeedsRender, "same-provider configuration invalidates too");
            Throws<ArgumentNullException>(() => doc.RebindTextMeasurer(null!));
            Exception? foreign = null;
            var thread = new Thread(() => { try { doc.RebindTextMeasurer(first); } catch (Exception error) { foreign = error; } });
            thread.Start(); thread.Join();
            require(foreign is InvalidOperationException && ReferenceEquals(doc.TextMeasurer, second), "foreign thread cannot mutate provider");
        });
        test("provider rebind during layout or paint is rejected without replacing provider", () =>
        {
            var first = new Provider(5); var replacement = new Provider(10);
            var root = new Probe(); var doc = new UiDocument(root, first, 100, 100);
            root.Callback = () => Throws<InvalidOperationException>(() => doc.RebindTextMeasurer(replacement));
            doc.BuildFrame();
            require(root.Calls == 2 && ReferenceEquals(doc.TextMeasurer, first), "layout and paint are non-reentrant");
        });
        test("provider changes normalize Indic caret, selection, edits and undo-redo", () =>
        {
            const string cluster = "क्ष";
            foreach (bool repaint in new[] { false, true })
            foreach (bool sameProvider in new[] { false, true })
            foreach (int operation in new[] { 0, 8, 46, 88 })
            {
                var provider = new IndicProvider(); var input = new TextBox { Text = cluster };
                var doc = new UiDocument(input, provider, 160, 44) { Clipboard = new Clipboard() };
                doc.BuildFrame(); doc.Focus(input);
                input.Select(2, operation == 88 ? 1 : 0);
                require(input.CaretIndex == (operation == 88 ? 3 : 2), "split-provider fixture");
                if (sameProvider) { provider.Grouped = true; doc.RebindTextMeasurer(provider); }
                else doc.RebindTextMeasurer(new IndicProvider { Grouped = true });
                if (repaint)
                {
                    doc.BuildFrame();
                    require(input.CaretIndex == 3 && (operation != 88 || input.SelectionLength == 3), "paint repairs stored offsets, not just visual caret");
                }
                if (operation == 0) doc.Dispatch(new(EventKind.TextInput) { Text = "x" });
                else doc.Dispatch(new(EventKind.KeyDown) { Key = operation, Control = operation == 88 });
                string expected = operation == 0 ? cluster + "x" : operation == 46 ? cluster : "";
                require(input.Text == expected, "editing never splits the replacement-provider cell");
                if (operation == 88) require(doc.Clipboard!.GetText() == cluster, "cut copies the complete replacement-provider cell");
                if (operation != 46)
                {
                    doc.Dispatch(new(EventKind.KeyDown) { Key = 90, Control = true });
                    require(input.Text == cluster && input.CaretIndex == 3 && (operation != 88 || input.SelectionLength == 3), "undo restores current-provider boundaries");
                    doc.Dispatch(new(EventKind.KeyDown) { Key = 89, Control = true });
                    require(input.Text == expected, "redo preserves safe replacement result");
                }
            }
        });
        test("failed provider query preserves text and selection and rejects rebind reentrancy", () =>
        {
            var provider = new IndicProvider(); var input = new TextBox { Text = "क्ष" };
            var doc = new UiDocument(input, provider, 160, 44); doc.BuildFrame(); doc.Focus(input); input.Select(2, 0);
            var failing = new IndicProvider { Grouped = true, Fail = true };
            doc.RebindTextMeasurer(failing);
            Throws<InvalidOperationException>(() => doc.BuildFrame());
            require(doc.NeedsRender && input.CaretIndex == 2 && input.Text == "क्ष", "failed paint leaves stored editing state and pending paint intact");
            Throws<InvalidOperationException>(() => doc.Dispatch(new(EventKind.TextInput) { Text = "x" }));
            require(input.CaretIndex == 2 && input.Text == "क्ष", "failed input leaves text and selection intact");
            failing.Fail = false;
            failing.Callback = () => Throws<InvalidOperationException>(() => doc.RebindTextMeasurer(provider));
            doc.Dispatch(new(EventKind.TextInput) { Text = "x" });
            require(input.Text == "क्षx" && ReferenceEquals(doc.TextMeasurer, failing), "input provider callback cannot replace the active provider");
            doc.BuildFrame();
            doc.Dispatch(new(EventKind.KeyDown) { Key = 90, Control = true });
            require(input.Text == "क्ष", "failed attempts did not corrupt undo history");
        });
    }
    public static void RunGpu()
    {
        const uint size = 64;
        var old = new NativeRenderer(size, size);
        Console.WriteLine($"Managed recovery adapter: {old.Adapter}; explicit host rebuild after invalid-resource error, no hardware loss injection");
        var image = old.UploadImage(2, 2, Enumerable.Repeat(new byte[] { 255, 0, 0, 255 }, 4).SelectMany(x => x).ToArray());
        var root = new ImageProbe { Image = image };
        var doc = new UiDocument(root, old, size, size);
        var stale = new DrawList(); stale.Append(doc.BuildFrame(), default);
        old.Render(stale);
        image.Dispose(); image.Dispose();
        int attempts = 0;
        var failure = Throws<NativeRendererException>(() => { attempts++; old.Render(stale); });
        Require(failure.NativeStatus == -1, "native operation classification");
        Require(attempts == 1, "host stops on first failure without retry loop");
        var orphan = old.UploadImage(1, 1, new byte[] { 255, 255, 255, 255 });
        var orphanCommands = new DrawList(); orphanCommands.Image(new(0, 0, 32, 32), orphan);
        old.Dispose(); old.Dispose();
        Require(old.IsDisposed && orphan.IsDisposed, "owner disposal invalidates managed image liveness");
        Throws<ObjectDisposedException>(() => new DrawList().Image(new(0, 0, 1, 1), orphan));
        orphan.Dispose(); orphan.Dispose(); // Does not access destroyed native renderer.
        Throws<ObjectDisposedException>(() => old.Render(stale));
        using var fresh = new NativeRenderer(size, size);
        using var replacement = fresh.UploadImage(2, 2, Enumerable.Repeat(new byte[] { 0, 255, 0, 255 }, 4).SelectMany(x => x).ToArray());
        root.Image = replacement;
        doc.RebindTextMeasurer(fresh);
        fresh.Render(doc.BuildFrame());
        string dir = Path.Combine("artifacts", "renderer-recovery"); Directory.CreateDirectory(dir);
        string baseline = Path.Combine(dir, "managed-rebuilt.png"); fresh.SavePng(baseline);
        var pixels = TextInputTests.ReadRgbaPng(baseline).Pixels;
        Require(pixels.AsSpan((32 * (int)size + 32) * 4, 4).SequenceEqual(new byte[] { 0, 255, 0, 255 }), "fresh resource pixels");
        foreach (var commands in new[] { stale, orphanCommands })
            Require(Throws<NativeRendererException>(() => fresh.Render(commands)).NativeStatus == -1, "old commands rejected after reconstruction");
        string after = Path.Combine(dir, "managed-after-rejected-stale.png"); fresh.SavePng(after);
        Require(pixels.SequenceEqual(TextInputTests.ReadRgbaPng(after).Pixels), "stale rejection preserves last submitted output");
        fresh.Render(doc.BuildFrame());
        Console.WriteLine("PASS managed renderer dispose/recreate/reupload/rebind, stale rejection and preserved pixels");
    }
    private sealed class Clipboard : IClipboard
    {
        private string text = "";
        public string GetText() => text;
        public void SetText(string value) => text = value;
    }
    private sealed class IndicProvider : ITextLayoutProvider
    {
        public bool Grouped, Fail;
        public Action? Callback;
        public (float Width, float Height) Measure(string text, float size, float width, bool bold = false) => (text.Length * 7, size);
        public TextLayout Layout(string text, float size, float width, bool bold = false, bool wrap = true)
        {
            if (Fail) throw new InvalidOperationException("injected provider failure");
            Callback?.Invoke();
            var cells = new List<TextCell>();
            for (int index = 0; index < text.Length;)
            {
                int count = text.AsSpan(index).StartsWith("क्ष") ? (Grouped ? 3 : 2) : 1;
                cells.Add(new(index, index + count, 0, new(index * 7, 0, count * 7, size))); index += count;
            }
            if (cells.Count == 0) cells.Add(new(0, 0, 0, new(0, 0, 0, size)));
            return new(text, cells);
        }
    }
    private sealed class Provider(float advance) : ITextLayoutProvider, IDisposable
    {
        public int Measures, Layouts;
        public bool Disposed;
        public (float Width, float Height) Measure(string text, float size, float width, bool bold = false) { Measures++; return (text.Length * advance, size); }
        public TextLayout Layout(string text, float size, float width, bool bold = false, bool wrap = true)
        {
            Layouts++; return new(text, new[] { new TextCell(0, text.Length, 0, new(0, 0, text.Length * advance, size)) });
        }
        public void Dispose() => Disposed = true;
    }
    private sealed class Probe : Element
    {
        public Action? Callback; public int Calls;
        protected override void ArrangeChildren(Rect bounds) { Calls++; Callback?.Invoke(); base.ArrangeChildren(bounds); }
        protected override void Paint(DrawList list, Rect bounds, float opacity, Theme theme) { Calls++; Callback?.Invoke(); }
    }
    private sealed class ImageProbe : Element
    {
        public NativeImage Image = null!;
        protected override void Paint(DrawList list, Rect bounds, float opacity, Theme theme) => list.Image(bounds, Image);
    }
}
