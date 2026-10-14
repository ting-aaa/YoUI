using System.Numerics;
using YoUI;

internal static class ReviewRegressionTests
{
    internal static void Run(Action<string, Action> test, Action<bool, string> require)
    {
        test("small virtual row heights keep a positive clickable row", () =>
        {
            foreach (float height in new[] { 1f, 5f, 6f, 7f })
            {
                var root = new Element(); var list = root.Add(new VirtualList { RowHeight = height, ItemCount = 100 });
                var doc = new UiDocument(root, new Measurer(), 200, 10); doc.BuildFrame();
                require(list.Children.All(row => row.Bounds.Height > 0 && row.Bounds.Height <= height), "row geometry within its stride");
                doc.Dispatch(new(EventKind.PointerDown) { Position = new(20, .5f) });
                doc.Dispatch(new(EventKind.PointerUp) { Position = new(20, .5f) });
                require(list.SelectedIndex == 0, "small row remains selectable");
            }
        });
        test("virtual row height rejects nonfinite and subpixel stride before layout", () =>
        {
            foreach (float height in new[] { 0f, .5f, float.NaN, float.PositiveInfinity })
            {
                bool rejected = false;
                try { _ = new VirtualList { RowHeight = height }; } catch (ArgumentOutOfRangeException) { rejected = true; }
                require(rejected, "invalid row stride rejected at assignment");
            }
        });
        test("tree pointer selection and expansion use accumulated visual offsets", () =>
        {
            var root = new Element(); var parent = root.Add(new Element { VisualOffset = new(80, 50) });
            var tree = parent.Add(new TreeView { VisualOffset = new(10, 5), Transform = RectTransform.Fixed(0, 0, 200, 160) });
            tree.SetItems([new("parent", "Parent", "", true, [new("child", "Child", "", false, [])]), new("second", "Second", "", false, [])]);
            var doc = new UiDocument(root, new Measurer(), 400, 250); doc.BuildFrame();
            Click(doc, new(150, 65)); require(tree.SelectedId == "parent", "first visible row selected rather than later logical row");
            Click(doc, new(100, 65)); doc.BuildFrame();
            Click(doc, new(150, 95)); require(tree.SelectedId == "second", "visual expander collapsed children");
            tree.VisualOffset = new(-10, -15); doc.BuildFrame();
            Click(doc, new(120, 45)); require(tree.SelectedId == "parent", "negative local offset remains consistent");
        });
        test("throwing Blur completes subtree detach and cancels the old gesture", () =>
        {
            var root = new Element(); var button = root.Add(new Button("Focused"));
            var doc = new UiDocument(root, new Measurer(), 200, 100); int clicks = 0; button.Clicked += () => clicks++;
            doc.Dispatch(new(EventKind.PointerMove) { Position = new(20, 20) });
            doc.Dispatch(new(EventKind.PointerDown) { Position = new(20, 20) });
            Action<UiEvent> throwing = e => { if (e.Kind == EventKind.Blur) throw new InvalidOperationException("blur fixture"); };
            button.Event += throwing; bool failed = false;
            try { root.Remove(button); } catch (InvalidOperationException e) { failed = e.Message == "blur fixture"; }
            require(failed && root.Children.Count == 0 && button.Parent == null && button.Document == null, "error propagated without ghost attachment");
            require(doc.Focused == null && !button.IsHovered && doc.NeedsRender, "interaction and invalidation cleaned despite Blur failure");
            doc.Dispatch(new(EventKind.PointerUp) { Position = new(20, 20) }); require(clicks == 0, "detached capture cannot activate");
            button.Event -= throwing; root.Add(button); doc.BuildFrame(); Click(doc, new(20, 20)); require(clicks == 1, "node reusable after failing detach");
        });
        test("failing document notification leaves every descendant detached and reusable", () =>
        {
            var root = new Element(); var panel = root.Add(new Element());
            var first = panel.Add(new Attachment()); var second = panel.Add(new Attachment());
            var doc = new UiDocument(root, new Measurer(), 200, 100); doc.BuildFrame(); first.Throw = true;
            bool failed = false; try { root.Remove(panel); } catch (InvalidOperationException) { failed = true; }
            require(failed && panel.Parent == null && panel.Document == null && first.Document == null && second.Document == null, "all associations committed before callbacks");
            require(second.Detaches == 1 && doc.NeedsRender, "later notifications and invalidation are not skipped");
            first.Throw = false; root.Add(panel); require(first.Document == doc && second.Document == doc, "subtree can be reattached after notification failure");
        });
        test("failed document construction rolls back association and permits root reuse", () =>
        {
            var root = new Element(); var first = root.Add(new Attachment { ThrowOnAttach = true, Throw = true });
            var second = root.Add(new Attachment());
            bool invalidSize = false;
            try { _ = new UiDocument(root, new Measurer(), 0, 100); } catch (ArgumentOutOfRangeException) { invalidSize = true; }
            require(invalidSize && first.Detaches == 0 && root.Document == null, "dimensions validated before notifications");
            bool failed = false;
            try { _ = new UiDocument(root, new Measurer(), 200, 100); } catch (InvalidOperationException e) { failed = e.Message == "initial attachment fixture"; }
            require(failed && root.Document == null && first.Document == null && second.Document == null, "rollback preserves initial error and clears all associations");
            require(first.Parent == root && second.Parent == root && second.Detaches == 1, "rollback preserves structure and attempts later cleanup callbacks");
            first.ThrowOnAttach = first.Throw = false;
            var doc = new UiDocument(root, new Measurer(), 200, 100); doc.BuildFrame();
            require(first.Document == doc && second.Document == doc, "root reusable after failed construction");
        });
        test("nested detach cancels stale queued document notifications", () =>
        {
            var root = new Element(); var doc = new UiDocument(root, new Measurer(), 200, 100);
            var panel = new Element();
            var first = panel.Add(new Transition()); var second = panel.Add(new Transition());
            first.Attached = () => root.Remove(panel);
            root.Add(panel);
            require(panel.Parent == null && panel.Document == null && first.Document == null && second.Document == null, "nested detach commits the subtree transition");
            require(first.Attachments == 1 && first.Detaches == 1 && second.Attachments == 0 && second.Detaches == 1, "obsolete attach notifications are not replayed after detach");
            first.Attached = null; root.Add(panel);
            require(first.Attachments == 2 && second.Attachments == 1 && first.Document == doc && second.Document == doc, "a later valid attachment notifies both descendants");
        });
    }
    private static void Click(UiDocument doc, Vector2 point)
    {
        doc.Dispatch(new(EventKind.PointerDown) { Position = point }); doc.Dispatch(new(EventKind.PointerUp) { Position = point });
    }
    private sealed class Measurer : ITextMeasurer
    {
        public (float Width, float Height) Measure(string text, float size, float width, bool bold = false) => (Math.Min(width, text.Length * size / 2), size);
    }
    private sealed class Attachment : Element
    {
        public bool Throw, ThrowOnAttach; public int Detaches;
        protected override void OnDocumentChanged(UiDocument? previous, UiDocument? current)
        {
            if (current != null && ThrowOnAttach) throw new InvalidOperationException("initial attachment fixture");
            if (current == null) { Detaches++; if (Throw) throw new InvalidOperationException("attachment fixture"); }
        }
    }
    private sealed class Transition : Element
    {
        public Action? Attached; public int Attachments, Detaches;
        protected override void OnDocumentChanged(UiDocument? previous, UiDocument? current)
        {
            if (current == null) Detaches++;
            else { Attachments++; Attached?.Invoke(); }
        }
    }
}
