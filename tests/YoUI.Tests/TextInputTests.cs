using System.Buffers.Binary;
using System.Globalization;
using System.IO.Compression;
using System.Numerics;
using YoUI;

static class TextInputTests
{
    public static void RunLogic(Action<string, Action> test, Action<bool, string> require)
    {
        test("text layout snapshots caller geometry and exposes grapheme-safe carets", () =>
        {
            const string value = "A😀e\u0301";
            var cells = new List<TextCell> { new(0, 1, 0, new(0, 0, 10, 20)), new(1, 3, 0, new(10, 0, 20, 20)), new(3, 5, 0, new(30, 0, 10, 20)) };
            var layout = new TextLayout(value, cells); cells.Clear();
            require(layout.Text == value && layout.Cells.Count == 3, "layout must own a geometry snapshot");
            require(layout.GetCaret(new(2, TextAffinity.Upstream)).X == 10 && layout.GetCaret(new(2, TextAffinity.Downstream)).X == 30, "surrogate interior snaps with affinity");
            require(layout.GetCaret(new(4, TextAffinity.Upstream)).X == 30 && layout.GetCaret(new(4, TextAffinity.Downstream)).X == 40, "combining interior snaps with affinity");
            require(layout.GetCaret(new(int.MinValue)).X == 0 && layout.GetCaret(new(int.MaxValue)).X == 40, "caret indices clamp safely");
            require(layout.GetSelection(2, 4).SequenceEqual(layout.GetSelection(1, 5)), "selection expands to whole graphemes");
            require(layout.GetSelection(5, 1).SequenceEqual(layout.GetSelection(1, 5)), "backward selection has identical geometry");
            require(layout.GetSelection(int.MinValue, int.MaxValue).SequenceEqual(layout.GetSelection(0, value.Length)), "selection endpoints clamp without overflow");
            require(layout.GetSelection(2, 2).Count == 0, "collapsed interior selection has no ink");
        });
        test("empty text layout has a stable finite caret", () =>
        {
            var layout = new TextLayout("", [new(0, 0, 0, new(0, 0, 0, 20))]);
            require(layout.HitTest(new(-100, 50)).Index == 0 && layout.HitTest(new(100, -50)).Index == 0, "empty hit testing stays at zero");
            require(Finite(layout.GetCaret(new(0))) && layout.GetSelection(-1, 1).Count == 0, "empty geometry is finite and unselected");
        });
        test("wrapped text layout preserves boundary affinity", () =>
        {
            var layout = new TextLayout("abc", [new(0, 1, 0, new(0, 0, 10, 20)), new(1, 2, 0, new(10, 0, 10, 20)), new(2, 3, 1, new(0, 20, 10, 20))]);
            var before = layout.GetCaret(new(2, TextAffinity.Upstream)); var after = layout.GetCaret(new(2, TextAffinity.Downstream));
            require(before.X == 20 && before.Y == 0 && after.X == 0 && after.Y == 20, "wrap boundary has distinct upstream/downstream positions");
            require(layout.HitTest(new(100, 10)) == new TextPosition(2, TextAffinity.Upstream), "right edge of first line retains upstream affinity");
            require(layout.HitTest(new(-100, 30)) == new TextPosition(2, TextAffinity.Downstream), "left edge of wrapped line retains downstream affinity");
            require(layout.HitTest(new(4, -100)).Index == 0 && layout.HitTest(new(9, 100)).Index == 3, "vertical hit testing clamps to nearest line");
            var rectangles = layout.GetSelection(1, 3);
            require(rectangles.Count == 2 && rectangles.Any(r => r.Y == 0) && rectangles.Any(r => r.Y == 20), "selection emits separate visual-line rectangles");
        });
        test("newline-only selections paint no ink and trailing lines retain carets", () =>
        {
            var layout = new TextLayout("a\nb\n", [new(0, 1, 0, new(0, 0, 10, 20)), new(1, 2, 0, new(10, 0, 0, 20)), new(2, 3, 1, new(0, 20, 10, 20)), new(3, 4, 1, new(10, 20, 0, 20)), new(4, 4, 2, new(0, 40, 0, 20))]);
            require(layout.GetSelection(1, 2).Count == 0 && layout.GetSelection(3, 4).Count == 0, "newlines do not create ink rectangles");
            require(layout.GetCaret(new(4)).Y == 40 && layout.HitTest(new(2, 50)).Index == 4, "trailing empty line remains addressable");
        });
        test("RTL text uses directional cell edges and disjoint mixed selections", () =>
        {
            var rtl = new TextLayout("אב", [new(0, 1, 0, new(10, 0, 10, 20), true), new(1, 2, 0, new(0, 0, 10, 20), true)]);
            require(rtl.GetCaret(new(0)).X == 20 && rtl.GetCaret(new(2)).X == 0, "RTL logical endpoints use opposite edges");
            require(rtl.HitTest(new(19, 10)).Index == 0 && rtl.HitTest(new(1, 10)).Index == 2, "RTL hit testing follows shaped cell direction");
            var mixed = new TextLayout("aאבz", [new(0, 1, 0, new(0, 0, 10, 20)), new(1, 2, 0, new(20, 0, 10, 20), true), new(2, 3, 0, new(10, 0, 10, 20), true), new(3, 4, 0, new(30, 0, 10, 20))]);
            require(mixed.GetCaret(new(1, TextAffinity.Upstream)).X == 10 && mixed.GetCaret(new(1, TextAffinity.Downstream)).X == 30, "bidi boundary affinity chooses preceding/following logical cell");
            var selection = mixed.GetSelection(0, 2);
            require(selection.Count == 2 && selection.Sum(r => r.Width) == 20, "logical selection preserves disjoint visual spans");
        });
        test("text layout rejects nonfinite hit positions", () =>
        {
            var layout = Mono("abc");
            foreach (var point in new[] { new Vector2(float.NaN, 0), new Vector2(0, float.NaN), new Vector2(float.PositiveInfinity, 0), new Vector2(0, float.NegativeInfinity) })
                Throws<ArgumentException>(() => layout.HitTest(point));
            require(layout.HitTest(new(7, 5)).Index == 1, "finite hit remains usable after rejection");
        });
        test("text layout rejects malformed indices geometry and UTF16", () =>
        {
            Throws<ArgumentException>(() => new TextLayout("", Array.Empty<TextCell>()));
            Throws<ArgumentException>(() => new TextLayout("\ud800", [new(0, 1, 0, new(0, 0, 7, 20))]));
            foreach (var cell in new TextCell[] { new(-1, 1, 0, new(0, 0, 7, 20)), new(0, 3, 0, new(0, 0, 7, 20)), new(0, 1, 0, new(0, 0, 7, 20)), new(1, 2, 0, new(0, 0, 7, 20)), new(0, 2, -1, new(0, 0, 7, 20)), new(0, 2, 0, new(float.NaN, 0, 7, 20)), new(0, 2, 0, new(0, 0, -1, 20)), new(0, 2, 0, new(0, 0, 7, 0)), new(0, 2, 0, new(float.MaxValue, 0, float.MaxValue, 20)) })
                Throws<ArgumentException>(() => new TextLayout("😀", [cell]));
            Throws<ArgumentOutOfRangeException>(() => Mono("a").GetCaret(new(0, (TextAffinity)99)));
            require(true, "public geometry contracts reject invalid snapshots");
        });
        test("text layout follows provider graphemes across Unicode segmentation versions", () =>
        {
            const string conjunct = "क्षि";
            var layout = new TextLayout(conjunct, [new(0, conjunct.Length, 0, new(0, 0, 18, 20))]);
            require(layout.CaretStops.SequenceEqual(new[] { 0, 4 }), "layout publishes provider stop set without older-runtime interior boundaries");
            for (int index = 1; index < conjunct.Length; index++)
            {
                require(layout.GetCaret(new(index, TextAffinity.Upstream)).X == 0 && layout.GetCaret(new(index, TextAffinity.Downstream)).X == 18, "provider grapheme owns every interior caret stop");
                require(layout.GetSelection(index, index + 1).SequenceEqual(layout.GetSelection(0, conjunct.Length)), "partial Indic range expands to complete provider grapheme");
            }
            for (int x = -10; x < 30; x++)
                require(layout.HitTest(new(x, 10)).Index is 0 or 4, "hit testing never returns older-runtime interior stop");
        });
        test("TextBox editing and attachment use provider grapheme boundaries", () =>
        {
            AssertIndicEditing(new IndicClusterProvider(), require);
        });
        test("text draw flags survive append and scaled copies", () =>
        {
            var source = new DrawList(); source.Text(new(10, 20, 30, 24), "overflowing text", new(1, 1, 1), 14, bold: true, wrap: false, clip: false);
            var appended = new DrawList(); appended.Append(source, new(7, 9)); var scaled = new DrawList(); source.CopyScaledTo(scaled, 2);
            require(source.Commands[0].Flags == 7 && appended.Commands[0].Flags == 7 && scaled.Commands[0].Flags == 7, "text bold no-wrap and viewport-only clip flags survive IR transforms");
            require(appended.Commands[0].Rect == new Rect(17, 29, 30, 24) && scaled.Commands[0].Rect == new Rect(20, 40, 60, 48), "text IR transforms retain their coordinate behavior");
            var (input, doc, _) = Input("long overflowing text", 80); var command = doc.BuildFrame().Commands.ToArray().Single(c => c.Kind == 1);
            require((command.Flags & 6) == 6 && doc.BuildFrame().Commands.ToArray().Any(c => c.Kind == 2), "TextBox no-wrap text is clipped by its explicit viewport");
        });
        test("TextBox public selection supports backward spans and logical arrows", () =>
        {
            var (input, doc, _) = Input("abcdef");
            input.Select(5, -3);
            require(input.CaretIndex == 2 && input.SelectionStart == 2 && input.SelectionLength == 3 && input.SelectedText == "cde", "backward selection keeps active caret");
            Key(doc, 37); require(input.CaretIndex == 2 && input.SelectionLength == 0, "left collapses backward selection to logical start");
            input.Select(5, -3); Key(doc, 39); require(input.CaretIndex == 5 && input.SelectionLength == 0, "right collapses backward selection to logical end");
            Key(doc, 36); Key(doc, 39, shift: true); Key(doc, 39, shift: true);
            require(input.CaretIndex == 2 && input.SelectedText == "ab", "shift-right extends a stable anchor");
            Key(doc, 35, shift: true); require(input.SelectedText == "abcdef", "shift-end extends to text end");
            input.Select(int.MaxValue, int.MinValue); require(input.CaretIndex >= 0 && input.CaretIndex <= input.Text.Length, "public selection clamps overflow-prone endpoints");
        });
        test("TextBox selection expands to surrogate combining and ZWJ boundaries", () =>
        {
            var (input, doc, _) = Input("A😀e\u0301👨‍👩‍👧‍👦Z");
            input.Select(2, 1); require(input.SelectionStart == 1 && input.SelectedText == "😀", "surrogate interior selects complete scalar");
            input.Select(4, 1); require(input.SelectionStart == 3 && input.SelectedText == "e\u0301", "combining interior selects full grapheme");
            input.Select(7, 1); require(input.SelectionStart == 5 && input.SelectedText == "👨‍👩‍👧‍👦", "ZWJ interior selects entire family");
            Key(doc, 46); require(input.Text == "A😀e\u0301Z" && input.CaretIndex == 5, "delete selection removes exactly one family cluster");
            input.Select(2, 0); require(input.CaretIndex == 3 && input.SelectionLength == 0, "collapsed selection snaps downstream without splitting surrogate");
            Key(doc, 8); require(input.Text == "Ae\u0301Z" && input.CaretIndex == 1, "backspace removes a whole emoji");
            Key(doc, 46); require(input.Text == "AZ", "forward delete removes a whole combining cluster");
        });
        test("TextBox replacements and history preserve backward selections", () =>
        {
            var (input, doc, _) = Input("abcdef"); int changes = 0; input.Changed += _ => changes++;
            input.Select(5, -3); Text(doc, "Ω");
            require(input.Text == "abΩf" && input.CaretIndex == 3 && input.SelectionLength == 0 && changes == 1, "typed text replaces a backward selection once");
            Key(doc, 90, control: true);
            require(input.Text == "abcdef" && input.CaretIndex == 2 && input.SelectedText == "cde", "undo restores text and backward anchor/caret");
            Key(doc, 89, control: true); require(input.Text == "abΩf" && input.CaretIndex == 3, "redo restores replacement and caret");
            input.Select(1, 2); Key(doc, 8); require(input.Text == "af" && input.CaretIndex == 1, "backspace deletes selected span");
            input.Text = "a😀b"; input.Select(3, 0); Key(doc, 8); Key(doc, 90, control: true);
            require(input.Text == "a😀b" && input.CaretIndex == 3 && input.SelectionLength == 0, "backspace undo restores the original collapsed caret instead of deletion range");
            input.Select(1, 0); Key(doc, 46); Key(doc, 90, control: true);
            require(input.Text == "a😀b" && input.CaretIndex == 1 && input.SelectionLength == 0, "forward-delete undo restores original collapsed caret");
        });
        test("TextBox insertion seams never leave caret inside a newly joined grapheme", () =>
        {
            var (input, doc, _) = Input("a"); Text(doc, "\u0301");
            require(input.Text == "a\u0301" && input.CaretIndex == 2, "appended combining mark joins previous grapheme");
            Key(doc, 8); require(input.Text == "", "joined cluster deletes atomically");
            input.Text = "👩👧"; input.Select(2, 0); Text(doc, "\u200D");
            require(input.Text == "👩‍👧" && input.CaretIndex == input.Text.Length, "inserted ZWJ joins both neighboring emoji and caret snaps after full cluster");
            Key(doc, 8); require(input.Text == "", "new ZWJ grapheme deletes atomically");
            input.Text = "\u0301"; input.Select(0, 0); Text(doc, "a");
            require(input.Text == "a\u0301" && input.CaretIndex == 2, "inserted base joins following combining mark");
            input.Text = "🇦X🇧"; input.Select(2, 1); Key(doc, 46);
            require(input.Text == "🇦🇧" && input.CaretIndex == 4, "deleting a separator joins regional indicators and snaps the caret");
            Key(doc, 8); require(input.Text == "", "newly joined regional-indicator pair deletes atomically");
        });
        test("TextBox normalizes single-line setter paste and commits consistently", () =>
        {
            const string multiline = "a\r\nb\tc\0d\u0085e\u2028f\u2029g";
            var (input, doc, _) = Input(multiline); string normalized = input.Text;
            require(!normalized.Any(char.IsControl) && !normalized.Contains('\u2028') && !normalized.Contains('\u2029'), "single-line setter removes controls and Unicode line separators");
            input.Text = ""; Text(doc, multiline); require(input.Text == normalized, "text commit uses setter normalization");
            input.Text = ""; doc.Clipboard = new TestClipboard { Value = multiline }; Key(doc, 86, control: true);
            require(input.Text == normalized, "paste uses the same single-line normalization");
        });
        test("TextBox rejects malformed UTF16 atomically in setters paste and input", () =>
        {
            var (input, doc, _) = Input("safe"); input.Select(1, 2); int changes = 0; input.Changed += _ => changes++;
            foreach (string invalid in new[] { "\ud800", "\udc00", "a\ud800b" })
            {
                Throws<ArgumentException>(() => input.Text = invalid);
                Throws<ArgumentException>(() => Text(doc, invalid));
                Throws<ArgumentException>(() => doc.Dispatch(new(EventKind.Composition) { Text = invalid }));
                doc.Clipboard = new TestClipboard { Value = invalid }; Throws<ArgumentException>(() => Key(doc, 86, control: true));
                require(input.Text == "safe" && input.SelectedText == "af" && changes == 0, "invalid UTF16 leaves text selection and notifications unchanged");
            }
        });
        test("TextBox pointer click and drag share test layout geometry", () =>
        {
            var (input, doc, _) = Input("abcdef"); input.Select(0, 0); var origin = TextOrigin(doc);
            Click(doc, origin + new Vector2(14.1f, 5)); require(input.CaretIndex == 2 && input.SelectionLength == 0, "click places caret by glyph cell geometry");
            Pointer(doc, EventKind.PointerDown, origin + new Vector2(35, 5));
            Pointer(doc, EventKind.PointerMove, origin + new Vector2(7, 5));
            Pointer(doc, EventKind.PointerUp, origin + new Vector2(7, 5));
            require(input.CaretIndex == 1 && input.SelectedText == "bcde", "backward drag preserves anchor and active caret");
            Click(doc, origin + new Vector2(28, 5), shift: true);
            require(input.CaretIndex == 4 && input.SelectedText == "e", "shift-click extends existing selection anchor");
        });
        test("TextBox pointer hits include ancestor and element visual offsets", () =>
        {
            var root = new Element { VisualOffset = new(9, 7) };
            var panel = root.Add(new Element { Transform = RectTransform.Fixed(40, 30, 280, 100), VisualOffset = new(11, 5) });
            var input = panel.Add(new TextBox { Text = "abcdef", Transform = RectTransform.Fixed(13, 9, 220, 44), VisualOffset = new(6, 3) });
            var doc = new UiDocument(root, new TestMeasurer(), 500, 200); doc.Focus(input); input.Select(0, 0);
            var origin = TextOrigin(doc); Click(doc, origin + new Vector2(21, 5));
            require(input.CaretIndex == 3, "pointer converts through all visual offsets");
        });
        test("TextBox horizontal scrolling uses the same origin for paint and hit testing", () =>
        {
            var (input, doc, _) = Input("abcdefghijklmnopqrstuvwxyz", 100); input.Select(input.Text.Length, 0);
            var origin = TextOrigin(doc); require(origin.X < input.VisualBounds.X + 14, "long input scrolls to the active caret");
            var point = new Vector2(input.VisualBounds.X + 26, origin.Y + 5);
            int expected = new TestMeasurer().Layout(input.Text, 14, 100000).HitTest(point - origin).Index;
            Click(doc, point); require(input.CaretIndex == expected, "scrolled hit uses rendered origin, not unscrolled bounds");
        });
        test("TextBox captured drag reaches outside bounds and stops on pointer cancel", () =>
        {
            var (input, doc, _) = Input("abcdef", 100); input.Select(0, 0); var origin = TextOrigin(doc);
            Pointer(doc, EventKind.PointerDown, origin + new Vector2(14, 5));
            Pointer(doc, EventKind.PointerMove, new(500, origin.Y + 5));
            require(input.SelectedText == "cdef", "pointer capture extends selection outside input bounds");
            int beforeInvalid = input.CaretIndex;
            Pointer(doc, EventKind.PointerMove, new(float.NaN, origin.Y)); Pointer(doc, EventKind.PointerMove, new(origin.X, float.PositiveInfinity));
            require(input.CaretIndex == beforeInvalid, "nonfinite captured moves do not corrupt selection");
            doc.CancelPointer(); int caret = input.CaretIndex; string selected = input.SelectedText;
            Pointer(doc, EventKind.PointerMove, origin + new Vector2(0, 5)); Pointer(doc, EventKind.PointerUp, origin + new Vector2(0, 5));
            require(input.CaretIndex == caret && input.SelectedText == selected, "cancel stops drag before a later move/up");
        });
        test("TextBox blur and detach terminate captured drag", () =>
        {
            foreach (bool detach in new[] { false, true })
            {
                var (input, doc, root) = Input("abcdef"); input.Select(0, 0); var origin = TextOrigin(doc);
                Pointer(doc, EventKind.PointerDown, origin + new Vector2(14, 5));
                if (detach) { root.Remove(input); root.Add(input); } else doc.Focus(null);
                int caret = input.CaretIndex; Pointer(doc, EventKind.PointerMove, origin + new Vector2(35, 5));
                require(input.CaretIndex == caret && input.SelectionLength == 0, detach ? "detach/reattach does not revive drag" : "blur stops drag even while pointer is over input");
                Pointer(doc, EventKind.PointerUp, origin + new Vector2(35, 5));
            }
        });
        test("TextBox composition stays transient and suppresses pointer repositioning", () =>
        {
            var (input, doc, _) = Input("abc"); input.Select(1, 1); int changes = 0; input.Changed += _ => changes++;
            doc.Dispatch(new(EventKind.Composition) { Text = "中文" }); var origin = TextOrigin(doc);
            Click(doc, origin + new Vector2(0, 5)); Key(doc, 37);
            require(input.Text == "abc" && input.CaretIndex == 2 && input.SelectedText == "b" && changes == 0, "preedit does not change committed text or pointer/key selection");
            Text(doc, "中"); require(input.Text == "a中c" && input.CaretIndex == 2 && changes == 1, "commit replaces the original selection exactly once");
            doc.Dispatch(new(EventKind.Composition) { Text = "temporary" }); doc.Focus(null); doc.Focus(input); Text(doc, "!");
            require(input.Text == "a中!c", "blur cancels transient preedit");
        });
        test("TextBox keyboard editing remains available without layout provider", () =>
        {
            var root = new Element(); var input = root.Add(new TextBox { Text = "a😀b" });
            var doc = new UiDocument(root, new MeasurementOnly(), 200, 80); doc.Focus(input);
            Key(doc, 37); Key(doc, 8); Text(doc, "Ω"); doc.BuildFrame();
            require(input.Text == "aΩb" && input.CaretIndex == 2, "measurement-only integrations keep grapheme keyboard editing");
        });
        test("TextBox geometry refreshes on text edits provider changes resize and reattachment", () =>
        {
            var provider = new CountingProvider(); var root = new Element(); var input = root.Add(new TextBox { Text = "abc", Transform = RectTransform.Fixed(20, 20, 160, 44) });
            var doc = new UiDocument(root, provider, 300, 100); doc.Focus(input); doc.BuildFrame(); int first = provider.Calls;
            doc.BuildFrame(); require(provider.Calls == first, "idle frame reuses cached geometry");
            input.Select(1, 0); Text(doc, "Ω"); doc.BuildFrame(); require(provider.Calls > first && provider.LastText == input.Text, "edit refreshes shaped text");
            input.Select(1, 0); var beforeCaret = doc.BuildFrame().Commands.ToArray().Last(c => c.Kind == 0).Rect;
            provider.AdvanceMultiplier = 2; input.Invalidate(); var afterCaret = doc.BuildFrame().Commands.ToArray().Last(c => c.Kind == 0).Rect;
            require(afterCaret.X > beforeCaret.X, "provider metric changes refresh caret geometry after invalidation");
            provider.AdvanceMultiplier = 1; input.Invalidate();
            input.Transform = RectTransform.Fixed(20, 20, 90, 44); doc.BuildFrame(); var origin = TextOrigin(doc);
            int expected = Mono(input.Text).HitTest(new(7, 5)).Index;
            Click(doc, origin + new Vector2(7, 5)); require(input.CaretIndex == expected, "resizing retains current hit geometry");
            root.Remove(input); var nextProvider = new CountingProvider(); var nextRoot = new Element(); nextRoot.Add(input); var next = new UiDocument(nextRoot, nextProvider, 300, 100);
            next.Focus(input); next.BuildFrame(); require(nextProvider.Calls > 0, "reattachment shapes through new document provider");
        });
    }

    // Explicit opt-in: loads the matching native library and creates an actual adapter.
    // Software adapters are reported verbatim and are not hardware-GPU/window/IME evidence.
    public static void RunGpu()
    {
        static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
        using var renderer = new NativeRenderer(480, 180);
        Console.WriteLine($"GPU text-input adapter: {renderer.Adapter}");
        string[] samples = ["", "office ffi", "A😀e\u0301👨‍👩‍👧‍👦Z", "مرحبا بالعالم", "abc אבג xyz", "क्षि", "क्‍ष", "a\n\nb\n", "a\r\nb", new string('w', 100)];
        foreach (string value in samples)
        {
            var layout = renderer.Layout(value, 18, 180); var repeated = renderer.Layout(value, 18, 180);
            var boundaries = layout.CaretStops.ToHashSet();
            Require(layout.Text == value && layout.Cells.SequenceEqual(repeated.Cells), "native shape snapshot is deterministic: " + value);
            foreach (var cell in layout.Cells)
            {
                Require(ScalarBoundary(value, cell.Start) && ScalarBoundary(value, cell.End) && cell.Start <= cell.End, "native UTF8 offsets map only to valid UTF16 scalar boundaries");
                Require(Finite(cell.Bounds) && cell.Bounds.Width >= 0 && cell.Bounds.Height > 0 && cell.Line >= 0, "native geometry is finite and nonnegative");
            }
            foreach (int boundary in boundaries)
                foreach (var affinity in new[] { TextAffinity.Upstream, TextAffinity.Downstream })
                {
                    var caret = layout.GetCaret(new(boundary, affinity));
                    Require(Finite(caret) && caret.Height > 0, "native boundary has usable caret geometry");
                    var hit = layout.HitTest(new(caret.X, caret.Y + caret.Height * .5f));
                    Require(boundaries.Contains(hit.Index), "native hit never splits a grapheme");
                    var roundTrip = layout.GetCaret(hit);
                    Require(Math.Abs(roundTrip.X - caret.X) < .1f && Math.Abs(roundTrip.Y - caret.Y) < .1f, "native caret-hit roundtrip preserves visual edge");
                }
            for (int y = -20; y < 220; y += 13)
                for (int x = -20; x < 240; x += 11)
                    Require(boundaries.Contains(layout.HitTest(new(x, y)).Index), "arbitrary native geometry hit stays grapheme-safe");
            var selected = layout.GetSelection(0, value.Length);
            Require(selected.All(r => Finite(r) && r.Width > 0 && r.Height > 0), "native selection rectangles are finite and contain ink spans");
            Require(selected.SequenceEqual(layout.GetSelection(value.Length, 0)), "native reverse selection geometry is stable");
        }
        var unicodeClusters = renderer.Layout("A😀e\u0301👨‍👩‍👧‍👦Z", 18, 400, wrap: false);
        Require(unicodeClusters.CaretStops.SequenceEqual(new[] { 0, 1, 3, 5, 16, 17 }), "native UTF8-to-UTF16 bridge retains complete surrogate combining and ZWJ graphemes");
        foreach (string conjunct in new[] { "क्षि", "क्‍ष" })
        {
            foreach (float width in new[] { 1f, 5f, 10f, 180f })
            {
                var layout = renderer.Layout(conjunct, 18, width);
                Require(layout.Cells.Count == 1 && layout.Cells[0].Start == 0 && layout.Cells[0].End == conjunct.Length, "a native Indic grapheme has one cell even under narrow wrapping");
                Require(layout.CaretStops.SequenceEqual(new[] { 0, conjunct.Length }), "native Indic caret stops contain only whole-cluster endpoints");
                for (int interior = 1; interior < conjunct.Length; interior++)
                {
                    Require(layout.GetCaret(new(interior, TextAffinity.Upstream)) == layout.GetCaret(new(0, TextAffinity.Upstream)), "Indic upstream caret snaps before full native grapheme");
                    Require(layout.GetCaret(new(interior, TextAffinity.Downstream)) == layout.GetCaret(new(conjunct.Length, TextAffinity.Downstream)), "Indic downstream caret snaps after full native grapheme");
                }
            }
        }
        AssertIndicEditing(renderer, Require);
        var mixed = renderer.Layout("aאבz", 18, 400);
        Require(mixed.Cells.Any(c => c.RightToLeft) && mixed.Cells.Any(c => !c.RightToLeft), "native bridge retains actual bidirectional cell direction");
        var wrapped = renderer.Layout(new string('w', 30), 18, 60);
        Require(wrapped.Cells.Select(c => c.Line).Distinct().Count() > 1, "native layout exposes real wrapping");
        var unwrapped = renderer.Layout(new string('w', 30), 18, 60, wrap: false);
        Require(unwrapped.Cells.Select(c => c.Line).Distinct().Count() == 1 && unwrapped.Cells.Max(c => c.Bounds.Right) > 60, "native no-wrap geometry stays on one overflowing line");
        Throws<ArgumentException>(() => renderer.Layout("\ud800", 18, 200));
        Exception? wrongThread = null; var thread = new Thread(() => { try { renderer.Layout("thread", 18, 200); } catch (Exception ex) { wrongThread = ex; } });
        thread.Start(); thread.Join(); Require(wrongThread is InvalidOperationException, "layout rejects a different managed thread before native access");

        var root = new Element(); var input = root.Add(new TextBox { Text = "office e\u0301 😀 אבג", Transform = RectTransform.Fixed(24, 48, 420, 48) });
        var doc = new UiDocument(root, renderer, 480, 180); doc.Focus(input); input.Select(0, 0);
        var origin = TextOrigin(doc); var textCommand = doc.BuildFrame().Commands.ToArray().First(c => c.Kind == 1);
        var actual = renderer.Layout(input.Text, 14, textCommand.Rect.Width, wrap: false);
        var pick = actual.Cells.First(c => c.End > c.Start && c.Bounds.Width > 1 && !c.RightToLeft);
        var pointer = origin + new Vector2(pick.Bounds.Right - .1f, pick.Bounds.Y + pick.Bounds.Height * .5f);
        int expectedHit = actual.HitTest(pointer - origin).Index; Click(doc, pointer);
        Require(input.CaretIndex == expectedHit, "TextBox pointer uses actual native-shaped geometry");
        input.Select(0, 0); renderer.Render(doc.BuildFrame());
        string directory = Path.Combine("artifacts", "text-input"); Directory.CreateDirectory(directory);
        string baselinePath = Path.Combine(directory, "native-unselected.png"), selectedPath = Path.Combine(directory, "native-selected.png");
        renderer.SavePng(baselinePath); var baseline = ReadRgbaPng(baselinePath);
        input.Select(1, input.Text.Length - 2); var selectedFrame = doc.BuildFrame();
        var selectionCommands = selectedFrame.Commands.ToArray().Where(c => c.Kind == 0 && c.Color.A > .2f && c.Color.A < .4f).ToArray();
        Require(selectionCommands.Length > 0, "TextBox emits selection spans from native layout");
        renderer.Render(selectedFrame); renderer.SavePng(selectedPath); var selectionPixels = ReadRgbaPng(selectedPath);
        Require(baseline.Width == selectionPixels.Width && baseline.Height == selectionPixels.Height, "pixel captures have matching dimensions");
        int changedSelection = 0;
        for (int y = 0; y < baseline.Height; y++) for (int x = 0; x < baseline.Width; x++)
        {
            int at = (y * baseline.Width + x) * 4;
            if (selectionCommands.Any(c => c.Rect.Contains(new(x + .5f, y + .5f))) && !baseline.Pixels.AsSpan(at, 4).SequenceEqual(selectionPixels.Pixels.AsSpan(at, 4))) changedSelection++;
        }
        Require(changedSelection > 20, "selection changes actual rendered RGBA pixels inside shaped selection spans");
        renderer.Render(doc.BuildFrame()); string repeatedPath = Path.Combine(directory, "native-selected-repeat.png"); renderer.SavePng(repeatedPath);
        Require(selectionPixels.Pixels.SequenceEqual(ReadRgbaPng(repeatedPath).Pixels), "unchanged native text selection renders identical pixels");
        var retained = renderer.Layout("snapshot 😀", 18, 200); renderer.Dispose();
        Throws<ObjectDisposedException>(() => renderer.Layout("disposed", 18, 200));
        Require(retained.HitTest(new(0, 0)).Index == 0 && retained.GetSelection(0, retained.Text.Length).Count > 0, "detached managed layout snapshot remains usable after renderer disposal");
        Console.WriteLine($"PASS managed native text-layout bridge, Unicode geometry, thread/disposal guards and selection pixels ({changedSelection} changed); screenshots: {directory}");
    }

    private static void AssertIndicEditing(ITextLayoutProvider provider, Action<bool, string> require)
    {
        const string conjunct = "क्षि";
        var input = new TextBox { Text = conjunct, Transform = RectTransform.Fixed(20, 20, 200, 44) };
        input.Select(2, 0); // Older .NET segmentation can leave this at 2 while detached.
        var root = new Element(); root.Add(input); var doc = new UiDocument(root, provider, 300, 100); doc.Focus(input);
        require(input.CaretIndex == conjunct.Length && input.SelectionLength == 0, "attachment normalizes detached selection to provider grapheme boundaries");
        Key(doc, 37); require(input.CaretIndex == 0, "left traverses the full provider Indic grapheme");
        Key(doc, 39); require(input.CaretIndex == conjunct.Length, "right traverses the full provider Indic grapheme");
        Key(doc, 8); require(input.Text == "" && input.CaretIndex == 0, "backspace removes the full provider Indic grapheme");
        Key(doc, 90, control: true); require(input.Text == conjunct && input.CaretIndex == conjunct.Length && input.SelectionLength == 0, "Indic backspace undo restores original collapsed endpoint");
        input.Text = conjunct; input.Select(0, 0); Key(doc, 46);
        require(input.Text == "" && input.CaretIndex == 0, "forward delete removes the full provider Indic grapheme");
        input.Text = conjunct; input.Select(2, 1);
        require(input.SelectionStart == 0 && input.SelectionLength == conjunct.Length && input.SelectedText == conjunct, "interior forward selection expands to native grapheme");
        input.Select(3, -1); require(input.CaretIndex == 0 && input.SelectedText == conjunct, "interior backward selection expands and preserves direction");
        input.Select(2, 0); require(input.CaretIndex == conjunct.Length, "collapsed interior selection snaps downstream using provider");
        Key(doc, 37, shift: true); require(input.SelectedText == conjunct && input.CaretIndex == 0, "shift-left selects exactly one provider grapheme");
        Text(doc, "X"); require(input.Text == "X", "typing replaces full provider grapheme selection");
        Key(doc, 90, control: true); require(input.Text == conjunct && input.SelectedText == conjunct, "undo restores whole provider selection");
        input.Text = "षि"; input.Select(0, 0); Text(doc, "क्");
        require(input.Text == conjunct && input.CaretIndex == conjunct.Length, "insertion seam snaps through newly joined Indic provider grapheme");
        Key(doc, 8); require(input.Text == "", "newly joined Indic grapheme deletes atomically");
        input.Text = conjunct; input.Select(0, 0); var origin = TextOrigin(doc);
        var layout = provider.Layout(conjunct, 14, input.Bounds.Width - 28, wrap: false); var cell = layout.Cells.Single();
        Click(doc, origin + new Vector2(cell.Bounds.Right - .01f, cell.Bounds.Y + cell.Bounds.Height * .5f));
        require(input.CaretIndex == conjunct.Length, "pointer and keyboard use the same provider Indic endpoint");
        root.Remove(input); input.Select(2, 0); root.Add(input);
        require(input.CaretIndex == conjunct.Length && input.SelectionLength == 0, "reattachment renormalizes detached fallback selection again");

        var fallbackRoot = new Element(); var historyInput = fallbackRoot.Add(new TextBox { Text = conjunct });
        var fallbackDoc = new UiDocument(fallbackRoot, new LegacyIndicProvider(), 300, 100); fallbackDoc.Focus(historyInput);
        Key(fallbackDoc, 37); require(historyInput.CaretIndex == 2, "deterministic legacy provider reproduces old Indic segmentation on every host");
        Text(fallbackDoc, "X"); fallbackRoot.Remove(historyInput);
        var currentRoot = new Element(); currentRoot.Add(historyInput); var currentDoc = new UiDocument(currentRoot, provider, 300, 100); currentDoc.Focus(historyInput);
        Key(currentDoc, 90, control: true);
        require(historyInput.Text == conjunct && historyInput.CaretIndex == conjunct.Length && historyInput.SelectionLength == 0, "undo renormalizes a fallback-era stored caret under the current provider");
        Key(currentDoc, 89, control: true); Key(currentDoc, 90, control: true);
        require(historyInput.Text == conjunct && historyInput.CaretIndex == conjunct.Length && historyInput.SelectionLength == 0, "redo/undo retains provider-safe history positions after reattachment");
        Key(currentDoc, 8); require(historyInput.Text == "", "backspace after restored fallback history removes the whole provider grapheme");
        Key(currentDoc, 90, control: true); Key(currentDoc, 37); Key(currentDoc, 46);
        require(historyInput.Text == "", "forward delete after restored history removes the whole provider grapheme");
    }
    private static bool ScalarBoundary(string text, int index) => index >= 0 && index <= text.Length && (index == 0 || index == text.Length || !char.IsHighSurrogate(text[index - 1]) || !char.IsLowSurrogate(text[index]));
    private static bool Finite(Rect r) => float.IsFinite(r.X) && float.IsFinite(r.Y) && float.IsFinite(r.Width) && float.IsFinite(r.Height);
    private static TextLayout Mono(string text) => new TestMeasurer().Layout(text, 14, 100000);
    private static void Throws<T>(Action operation) where T : Exception
    {
        try { operation(); } catch (T) { return; }
        throw new Exception($"Expected {typeof(T).Name}.");
    }
    private static (TextBox Input, UiDocument Document, Element Root) Input(string text, float width = 240)
    {
        var root = new Element(); var input = root.Add(new TextBox { Text = text, Transform = RectTransform.Fixed(20, 20, width, 44) });
        var doc = new UiDocument(root, new TestMeasurer(), 640, 160); doc.Focus(input); doc.BuildFrame(); return (input, doc, root);
    }
    private static Vector2 TextOrigin(UiDocument doc)
    {
        var command = doc.BuildFrame().Commands.ToArray().First(c => c.Kind == 1); return new(command.Rect.X, command.Rect.Y);
    }
    private static void Text(UiDocument doc, string text) => doc.Dispatch(new(EventKind.TextInput) { Text = text });
    private static void Key(UiDocument doc, int key, bool shift = false, bool control = false) => doc.Dispatch(new(EventKind.KeyDown) { Key = key, Shift = shift, Control = control });
    private static void Pointer(UiDocument doc, EventKind kind, Vector2 point, bool shift = false) => doc.Dispatch(new(kind) { Position = point, Shift = shift });
    private static void Click(UiDocument doc, Vector2 point, bool shift = false) { Pointer(doc, EventKind.PointerDown, point, shift); Pointer(doc, EventKind.PointerUp, point, shift); }
    private sealed class MeasurementOnly : ITextMeasurer
    {
        public (float Width, float Height) Measure(string text, float size, float width, bool bold = false) => new TestMeasurer().Measure(text, size, width, bold);
    }
    // Deliberately models a provider with newer Indic grapheme rules than the host .NET runtime.
    private sealed class IndicClusterProvider : ITextLayoutProvider
    {
        public (float Width, float Height) Measure(string text, float size, float width, bool bold = false) => new TestMeasurer().Measure(text, size, width, bold);
        public TextLayout Layout(string text, float size, float width, bool bold = false, bool wrap = true)
        {
            var stops = StringInfo.ParseCombiningCharacters(text).Append(text.Length).ToList();
            foreach (string cluster in new[] { "क्षि", "क्‍ष" })
                for (int at = text.IndexOf(cluster, StringComparison.Ordinal); at >= 0; at = text.IndexOf(cluster, at + cluster.Length, StringComparison.Ordinal))
                    stops.RemoveAll(stop => stop > at && stop < at + cluster.Length);
            var cells = new List<TextCell>();
            for (int i = 0; i + 1 < stops.Count; i++) cells.Add(new(stops[i], stops[i + 1], 0, new(i * size * .5f, 0, size * .5f, size * 1.35f)));
            if (cells.Count == 0) cells.Add(new(0, 0, 0, new(0, 0, 0, size * 1.35f)));
            return new(text, cells);
        }
    }
    // Deliberately pins the older [0,2,4] boundary set, independent of host ICU/.NET tables.
    private sealed class LegacyIndicProvider : ITextLayoutProvider
    {
        public (float Width, float Height) Measure(string text, float size, float width, bool bold = false) => new TestMeasurer().Measure(text, size, width, bold);
        public TextLayout Layout(string text, float size, float width, bool bold = false, bool wrap = true) => text == "क्षि"
            ? new(text, [new(0, 2, 0, new(0, 0, size * .5f, size * 1.35f)), new(2, 4, 0, new(size * .5f, 0, size * .5f, size * 1.35f))])
            : new TestMeasurer().Layout(text, size, width, bold, wrap);
    }
    private sealed class CountingProvider : ITextLayoutProvider
    {
        public int Calls; public string LastText = ""; public float AdvanceMultiplier = 1;
        public (float Width, float Height) Measure(string text, float size, float width, bool bold = false) => new TestMeasurer().Measure(text, size, width, bold);
        public TextLayout Layout(string text, float size, float width, bool bold = false, bool wrap = true)
        {
            Calls++; LastText = text; var layout = new TestMeasurer().Layout(text, size, width, bold, wrap);
            return new(text, layout.Cells.Select(c => c with { Bounds = c.Bounds with { X = c.Bounds.X * AdvanceMultiplier, Width = c.Bounds.Width * AdvanceMultiplier } }));
        }
    }
    // Decode the renderer's non-interlaced RGBA8 PNG readback without a test-only image package.
    private static (int Width, int Height, byte[] Pixels) ReadRgbaPng(string path)
    {
        byte[] bytes = File.ReadAllBytes(path); int width = 0, height = 0; using var compressed = new MemoryStream();
        if (!bytes.AsSpan(0, 8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 })) throw new Exception("Invalid PNG signature.");
        for (int offset = 8; offset < bytes.Length;)
        {
            int length = checked((int)BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(offset, 4))); string type = System.Text.Encoding.ASCII.GetString(bytes, offset + 4, 4); var data = bytes.AsSpan(offset + 8, length);
            if (type == "IHDR")
            {
                width = checked((int)BinaryPrimitives.ReadUInt32BigEndian(data)); height = checked((int)BinaryPrimitives.ReadUInt32BigEndian(data[4..]));
                if (data[8] != 8 || data[9] != 6 || data[12] != 0) throw new Exception("Expected non-interlaced RGBA8 renderer PNG.");
            }
            else if (type == "IDAT") compressed.Write(data);
            offset += length + 12;
        }
        compressed.Position = 0; using var inflater = new ZLibStream(compressed, CompressionMode.Decompress); int stride = checked(width * 4); byte[] encoded = new byte[checked((stride + 1) * height)]; inflater.ReadExactly(encoded); byte[] pixels = new byte[checked(stride * height)];
        for (int y = 0; y < height; y++)
        {
            int row = y * stride, source = y * (stride + 1); byte filter = encoded[source];
            for (int x = 0; x < stride; x++)
            {
                int left = x >= 4 ? pixels[row + x - 4] : 0, up = y > 0 ? pixels[row + x - stride] : 0, upperLeft = x >= 4 && y > 0 ? pixels[row + x - stride - 4] : 0;
                int predictor = filter switch { 0 => 0, 1 => left, 2 => up, 3 => (left + up) / 2, 4 => Paeth(left, up, upperLeft), _ => throw new Exception("Unsupported PNG filter.") };
                pixels[row + x] = unchecked((byte)(encoded[source + 1 + x] + predictor));
            }
        }
        return (width, height, pixels);
    }
    private static int Paeth(int a, int b, int c) { int p = a + b - c, pa = Math.Abs(p - a), pb = Math.Abs(p - b), pc = Math.Abs(p - c); return pa <= pb && pa <= pc ? a : pb <= pc ? b : c; }
}
