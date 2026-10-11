using System.Numerics;
using YoUI;

static class LayoutUpdateTests
{
    public static void Run(Action<string, Action> test, Action<bool, string> require)
    {
        void Check(bool condition, string message = "layout update assertion failed") => require(condition, message);
        static UiDocument Doc(Element root) => new(root, new TestMeasurer(), 320, 240);
        static void Clean(UiDocument doc) { doc.UpdateLayout(); doc.BuildFrame(); doc.Semantics(); }
        static bool Has(Element node, DirtyFlags flags) => (node.Dirty & flags) == flags;
        static void Throws(Action action)
        {
            try { action(); } catch (InvalidOperationException) { return; }
            throw new Exception("Expected nested update or callback failure");
        }

        test("layout edits coalesce and stage diagnostics become clean", () =>
        {
            var root = new Element(); var panel = root.Add(new Element()); var leaf = panel.Add(new Label("A")); var doc = Doc(root);
            Clean(doc); int layouts = doc.LayoutPasses, paints = doc.PaintPasses;
            Check(doc.PendingUpdates == DirtyFlags.None && root.Dirty == DirtyFlags.None && leaf.Dirty == DirtyFlags.None);
            for (int i = 1; i <= 100; i++) leaf.Width = i;
            Check(Has(leaf, DirtyFlags.All) && Has(panel, DirtyFlags.All) && Has(root, DirtyFlags.All));
            Check(doc.PendingUpdates == DirtyFlags.All && doc.UpdateLayout());
            Check(doc.LayoutPasses == layouts + 1 && doc.PaintPasses == paints && !Has(leaf, DirtyFlags.Layout));
            Check(doc.PendingUpdates == (DirtyFlags.Paint | DirtyFlags.Semantics));
            Clean(doc); Check(doc.LayoutPasses == layouts + 1 && doc.PaintPasses == paints + 1);
            var frame = doc.BuildFrame(); leaf.Width = 100; doc.Resize(320, 240); doc.Theme = doc.Theme with { };
            leaf.Invalidate(DirtyFlags.None);
            Check(doc.UpdateLayout() && ReferenceEquals(frame, doc.BuildFrame()));
            Check(doc.LayoutPasses == layouts + 1 && doc.PaintPasses == paints + 1 && doc.PendingUpdates == DirtyFlags.None);
        });
        test("paint and semantics are independently consumed without layout", () =>
        {
            var root = new Element(); var label = root.Add(new Label("A")); var doc = Doc(root); Clean(doc);
            int layouts = doc.LayoutPasses, paints = doc.PaintPasses;
            label.AccessibleName = "Renamed";
            Check(!doc.NeedsRender && doc.PendingUpdates == DirtyFlags.Semantics);
            doc.BuildFrame(); Check(doc.PendingUpdates == DirtyFlags.Semantics && Has(label, DirtyFlags.Semantics));
            Check(doc.Semantics().Single().Name == "Renamed" && label.Dirty == DirtyFlags.None);
            label.Background = Color.Hex(0x123456); doc.Semantics();
            Check(doc.NeedsRender && doc.PendingUpdates == DirtyFlags.Paint);
            doc.BuildFrame(); Check(doc.LayoutPasses == layouts && doc.PaintPasses == paints + 1);
            label.VisualOffset = new(4, 6);
            Check(doc.PendingUpdates == (DirtyFlags.Paint | DirtyFlags.Semantics));
            Check(doc.Semantics().Single().Bounds.X == label.Bounds.X + 4);
            doc.BuildFrame(); Check(doc.LayoutPasses == layouts && doc.PendingUpdates == DirtyFlags.None);
        });
        test("measure-time and before-base Arrange invalidations survive the entire pass", () =>
        {
            foreach (bool inMeasure in new[] { true, false })
            {
                var root = new Probe(); var leaf = root.Add(new Probe()); var doc = Doc(root);
                Action invalidate = () => leaf.Invalidate(DirtyFlags.Layout);
                if (inMeasure) leaf.Measuring = invalidate; else leaf.Arranging = invalidate;
                doc.EnsureLayout();
                Check(Has(leaf, DirtyFlags.Layout) && Has(root, DirtyFlags.Layout) && doc.NeedsRender);
                leaf.Measuring = leaf.Arranging = null; Check(doc.UpdateLayout());
                Clean(doc); Check(leaf.Dirty == DirtyFlags.None && root.Dirty == DirtyFlags.None);
            }
        });
        test("a sibling invalidated before its paint retains pending paint", () =>
        {
            var root = new Element(); var first = root.Add(new Probe()); var later = root.Add(new Probe()); var doc = Doc(root); Clean(doc);
            first.Painting = () => later.Invalidate(); first.Invalidate(); doc.BuildFrame();
            Check(Has(later, DirtyFlags.Paint) && Has(root, DirtyFlags.Paint) && doc.NeedsRender);
            first.Painting = null; Clean(doc); Check(!doc.NeedsRender && later.Dirty == DirtyFlags.None);
        });
        test("bounded UpdateLayout converges or reports pending work without painting", () =>
        {
            var root = new Probe(); var doc = Doc(root); int remaining = 2;
            root.Arranging = () => { if (remaining-- > 0) root.Invalidate(DirtyFlags.Layout); };
            Check(doc.UpdateLayout(3) && doc.LayoutPasses == 3 && doc.PaintPasses == 0);
            root.Arranging = () => root.Invalidate(DirtyFlags.Layout); root.Invalidate(DirtyFlags.Layout);
            Check(!doc.UpdateLayout(2) && doc.LayoutPasses == 5 && Has(root, DirtyFlags.Layout));
            Check(!doc.UpdateLayout() && doc.LayoutPasses == 13 && doc.PaintPasses == 0);
            root.Arranging = null; Check(doc.UpdateLayout(1) && doc.LayoutPasses == 14);
            foreach (int invalid in new[] { 0, -1 })
            {
                bool rejected = false; try { doc.UpdateLayout(invalid); } catch (ArgumentOutOfRangeException) { rejected = true; }
                Check(rejected && doc.LayoutPasses == 14);
            }
        });
        test("BuildFrame remains one layout pass and defers further layout", () =>
        {
            var root = new Probe(); var doc = Doc(root);
            root.Arranging = () => root.Width = 120;
            doc.BuildFrame(); Check(doc.LayoutPasses == 1 && doc.PaintPasses == 1 && Has(root, DirtyFlags.Layout));
            root.Arranging = null; doc.BuildFrame(); Check(doc.LayoutPasses == 2 && doc.PaintPasses == 2 && !doc.NeedsRender);
        });
        test("nested update methods fail explicitly and the outer stage remains retryable", () =>
        {
            foreach (int stage in new[] { 0, 1, 2 })
            foreach (int operation in new[] { 0, 1, 2, 3, 4 })
            {
                var root = new Probe(); var doc = Doc(root); Clean(doc);
                Action nested = operation switch
                {
                    0 => () => doc.EnsureLayout(), 1 => () => doc.UpdateLayout(),
                    2 => () => doc.BuildFrame(), 3 => () => doc.Semantics(),
                    _ => () => doc.Dispatch(new(EventKind.PointerMove))
                };
                Action run = stage switch { 0 => () => doc.EnsureLayout(), 1 => () => doc.BuildFrame(), _ => () => doc.Semantics() };
                DirtyFlags flags = stage switch { 0 => DirtyFlags.Layout, 1 => DirtyFlags.Paint, _ => DirtyFlags.Semantics };
                if (stage == 0) root.Measuring = nested;
                else if (stage == 1) root.Painting = nested;
                else root.ReadingSemantics = nested;
                root.Invalidate(flags); Throws(run); Check(Has(root, flags) && (doc.PendingUpdates & flags) == flags);
                root.Measuring = root.Painting = root.ReadingSemantics = null; Clean(doc);
                Check(doc.PendingUpdates == DirtyFlags.None && root.Dirty == DirtyFlags.None);
            }
        });
        test("caught nested update leaves callback invalidation pending", () =>
        {
            var root = new Probe(); var doc = Doc(root); Clean(doc);
            root.Painting = () => { root.Invalidate(); Throws(() => doc.BuildFrame()); };
            root.Invalidate(); doc.BuildFrame(); Check(doc.NeedsRender && Has(root, DirtyFlags.Paint));
            root.Painting = null; Clean(doc); Check(!doc.NeedsRender);
        });
        test("failed stages restore consumed child flags and preserve other work", () =>
        {
            foreach (int stage in new[] { 0, 1, 2 })
            {
                var root = new Element(); var child = root.Add(new Probe()); var doc = Doc(root); Clean(doc);
                Action fail = () => { child.Invalidate(DirtyFlags.Semantics); throw new InvalidOperationException("retry"); };
                if (stage == 0) child.Measuring = fail;
                else if (stage == 1) child.Painting = fail;
                else child.ReadingSemantics = fail;
                DirtyFlags flags = stage switch { 0 => DirtyFlags.Layout, 1 => DirtyFlags.Paint, _ => DirtyFlags.Semantics };
                child.Invalidate(flags);
                Throws(stage == 2 ? () => doc.Semantics() : () => doc.BuildFrame());
                Check(Has(root, flags) && Has(child, flags) && Has(child, DirtyFlags.Semantics));
                child.Measuring = child.Painting = child.ReadingSemantics = null; Clean(doc);
                Check(doc.PendingUpdates == DirtyFlags.None && child.Dirty == DirtyFlags.None);
            }
        });
        test("hidden nodes are acknowledged and become layout-dirty when shown", () =>
        {
            var root = new Element(); var hidden = root.Add(new Element { Visible = false }); var label = hidden.Add(new Label("A"));
            var doc = Doc(root); Clean(doc); Check(hidden.Dirty == DirtyFlags.None && label.Dirty == DirtyFlags.None);
            label.Text = "Changed"; Clean(doc); Check(label.Dirty == DirtyFlags.None && hidden.LayoutRevision == 0);
            hidden.Visible = true; Clean(doc); Check(hidden.LayoutRevision == 1 && label.LayoutRevision == 1 && label.DesiredSize.X > 0);
        });
        test("detached edits do not wake the old document and reparenting refreshes both trees", () =>
        {
            var a = new Element(); var b = new Element(); var child = a.Add(new Label("A")); var first = Doc(a); var second = Doc(b);
            Clean(first); Clean(second); a.Remove(child); Clean(first);
            child.Text = "A much wider caption"; Check(!first.NeedsRender && !second.NeedsRender && Has(child, DirtyFlags.Layout));
            b.Add(child); Check(second.NeedsRender && child.Document == second && !first.NeedsRender);
            Clean(second); Check(child.Dirty == DirtyFlags.None && child.DesiredSize.X > 100);
            b.Remove(child); a.Add(child); Clean(first); Clean(second);
            Check(child.Document == first && first.PendingUpdates == DirtyFlags.None && second.PendingUpdates == DirtyFlags.None);
        });
        test("failed callback restores dirty work through a reparented node's new owner", () =>
        {
            var a = new Probe(); var b = new Element(); var child = a.Add(new Probe()); var first = Doc(a); var second = Doc(b);
            Clean(first); Clean(second); child.Invalidate(DirtyFlags.Layout);
            a.Measuring = () => { a.Remove(child); b.Add(child); Clean(second); throw new InvalidOperationException("moved"); };
            Throws(() => first.BuildFrame());
            Check(child.Document == second && Has(child, DirtyFlags.Layout) && first.NeedsRender && second.NeedsRender);
            a.Measuring = null; Clean(first); Clean(second); Check(child.Dirty == DirtyFlags.None);
        });
        test("semantics callback invalidation survives snapshot export", () =>
        {
            var root = new Probe(); var doc = Doc(root); Clean(doc);
            root.ReadingSemantics = () => root.Invalidate(DirtyFlags.Semantics);
            doc.Semantics(); Check(doc.PendingUpdates == DirtyFlags.Semantics && Has(root, DirtyFlags.Semantics) && !doc.NeedsRender);
            root.ReadingSemantics = null; doc.Semantics(); Check(doc.PendingUpdates == DirtyFlags.None);
        });
        test("focus modal and visual geometry changes request fresh semantics", () =>
        {
            var root = new Element(); var a = root.Add(new Button("A")); var b = root.Add(new Button("B")); var doc = Doc(root); Clean(doc);
            doc.Focus(a); doc.BuildFrame(); Check(doc.PendingUpdates == DirtyFlags.Semantics && doc.Semantics().Single(n => n.Name == "A").Focused);
            doc.SetModal(b); Clean(doc); Check(doc.Semantics().Any(n => n.Name == "B") && !doc.Semantics().Any(n => n.Name == "A"));
            doc.SetModal(null); Check((doc.PendingUpdates & DirtyFlags.Semantics) != 0); Clean(doc);
        });
    }

    private sealed class Probe : Element
    {
        public Action? Measuring, Arranging, Painting, ReadingSemantics;
        public Probe() => Role = "text";
        public override Vector2 Measure(Vector2 available) { Measuring?.Invoke(); return base.Measure(available); }
        public override void Arrange(Rect bounds) { Arranging?.Invoke(); base.Arrange(bounds); }
        protected override void Paint(DrawList list, Rect bounds, float opacity, Theme theme) { Painting?.Invoke(); base.Paint(list, bounds, opacity, theme); }
        public override string SemanticValue { get { ReadingSemantics?.Invoke(); return "probe"; } }
    }
}
