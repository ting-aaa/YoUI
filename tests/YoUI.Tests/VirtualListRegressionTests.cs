using System.Numerics;
using YoUI;

static class VirtualListRegressionTests
{
    public static void Run(Action<string, Action> test, Action<bool, string> require)
    {
        RunNavigation(test, require);
        RunRecycling(test, require);
    }

    public static void RunNavigation(Action<string, Action> test, Action<bool, string> require)
    {
        foreach (float height in new[] { 1f, 20, 53, 54, 55, 200 })
        foreach (float padding in new[] { 0f, 7 })
        test($"virtual list navigation height {height} padding {padding}", () =>
        {
            var (doc, list) = Fixture(height, padding, 10, 54);
            float viewport = Math.Max(0, height - padding * 2);
            float maximum = Math.Max(0, 10 * 54 - viewport);
            void CheckOffset()
            {
                require(float.IsFinite(list.ScrollOffset) && list.ScrollOffset >= 0 && list.ScrollOffset <= maximum, "Offset must stay finite and inside the content scroll range");
                if (list.SelectedIndex < 0) return;
                float start = list.SelectedIndex * 54;
                if (viewport <= 54) require(list.ScrollOffset == start, "An oversized row must align its start with the content viewport");
                else require(start >= list.ScrollOffset && start + 54 <= list.ScrollOffset + viewport, "Selected row must fit in the content viewport");
            }
            doc.Focus(list);
            foreach (int key in new[] { 36, 35, 38, 40, 36, 38, 40, 40, 35, 40 })
            {
                doc.Dispatch(new(EventKind.KeyDown) { Key = key });
                doc.UpdateLayout();
                CheckOffset();
                if (key == 36) require(list.SelectedIndex == 0 && list.ScrollOffset == 0, "Home must reach the first item");
                if (key == 35) require(list.SelectedIndex == 9, "End must reach the last item");
            }
            list.ScrollOffset = float.MaxValue;
            require(list.ScrollOffset == maximum, "Public offset must use the padded content viewport before layout");
            doc.UpdateLayout();
            require(list.ScrollOffset == maximum, "Layout must preserve the legal maximum offset");
            var thumbs = doc.BuildFrame().Commands.ToArray().Where(c => c.Kind == 0 && c.Radius == 1.5f && c.Rect.Width == 3).ToArray();
            require(thumbs.Length == (viewport > 0 ? 1 : 0), "Only a nonempty content viewport should have a scroll thumb");
            foreach (var thumb in thumbs)
                require(thumb.Rect.Y >= padding - .001f && thumb.Rect.Bottom <= padding + viewport + .001f, "The scroll thumb must stay inside the padded content viewport, even below its preferred height");
            list.ItemCount = 0;
            foreach (int key in new[] { 36, 35, 38, 40 })
            {
                doc.Dispatch(new(EventKind.KeyDown) { Key = key });
                doc.UpdateLayout();
                require(list.SelectedIndex == -1 && list.ScrollOffset == 0 && list.RealizedCount == 0, "Empty-list navigation must remain empty and unscrolled");
            }
        });
    }

    public static void RunRecycling(Action<string, Action> test, Action<bool, string> require)
    {
        foreach (bool wheel in new[] { false, true })
        test($"virtual list cancels a pressed row before {(wheel ? "wheel" : "direct")} recycling", () =>
        {
            var (doc, list) = Fixture(200, 0, 1000, 50);
            list.ScrollOffset = 500; doc.UpdateLayout();
            var row = (Button)list.Children[2];
            int changes = 0, cancellations = 0;
            string? cancelledText = null;
            list.SelectionChanged += _ => changes++;
            row.Event += e => { if (e.Kind == EventKind.PointerCancel) { cancellations++; cancelledText = row.Text; } };
            doc.Dispatch(new(EventKind.PointerDown) { Position = new(20, 75) });
            if (wheel) doc.Dispatch(new(EventKind.Scroll) { Position = new(20, 75), Delta = -1 });
            else list.ScrollOffset = 25000;
            // PointerUp performs pending layout too; no intervening frame is required.
            doc.Dispatch(new(EventKind.PointerUp) { Position = new(20, 75) });
            require(ReferenceEquals(row, list.Children[2]), "Regression must exercise a reused row object");
            require(list.SelectedIndex == -1 && changes == 0, $"Release must not select the replacement identity (selected {list.SelectedIndex}, events {changes})");
            require(cancellations == 1 && cancelledText == "Row 11", "Cancel must target the old identity before rebinding");
            Click(doc, 20, 75);
            require(list.SelectedIndex == (wheel ? 12 : 501) && changes == 1, "A fresh complete click must select the new identity");
        });

        test("virtual list retains a press across unchanged identity refresh", () =>
        {
            var (doc, list) = Fixture(200, 0, 1000, 50);
            list.ScrollOffset = 500; doc.UpdateLayout();
            doc.Dispatch(new(EventKind.PointerDown) { Position = new(20, 75) });
            list.Refresh(); doc.UpdateLayout();
            doc.Dispatch(new(EventKind.PointerUp) { Position = new(20, 75) });
            require(list.SelectedIndex == 11, "Refresh without identity change must preserve the click");
        });

        test("virtual list row removal and explicit pointer cancellation prevent selection", () =>
        {
            foreach (bool remove in new[] { false, true })
            {
                var (doc, list) = Fixture(200, 0, 1000, 50);
                list.ScrollOffset = 500; doc.UpdateLayout();
                var row = list.Children[2]; int cancellations = 0;
                row.Event += e => { if (e.Kind == EventKind.PointerCancel) cancellations++; };
                doc.Dispatch(new(EventKind.PointerDown) { Position = new(20, 75) });
                if (remove) { list.ItemCount = 0; doc.UpdateLayout(); }
                else doc.CancelPointer();
                doc.Dispatch(new(EventKind.PointerUp) { Position = new(20, 75) });
                require(cancellations == 1 && list.SelectedIndex == -1, "Removal and explicit cancel must clear the press");
                if (remove) { list.ItemCount = 1000; list.ScrollOffset = 500; doc.UpdateLayout(); }
                Click(doc, 20, 75);
                require(list.SelectedIndex == 11, "A fresh click must work after removal or cancellation");
            }
        });

        test("virtual list recycling does not cancel another control's capture", () =>
        {
            var root = new Element();
            var list = root.Add(new VirtualList { ItemCount = 1000, RowHeight = 50, Transform = RectTransform.Fixed(0, 0, 300, 200) });
            var button = root.Add(new Button("Other") { Transform = RectTransform.Fixed(350, 0, 100, 50) });
            var doc = new UiDocument(root, new TestMeasurer(), 640, 480);
            int clicks = 0; button.Clicked += () => clicks++;
            doc.Dispatch(new(EventKind.PointerDown) { Position = new(370, 20) });
            list.ScrollOffset = 500; doc.UpdateLayout();
            doc.Dispatch(new(EventKind.PointerUp) { Position = new(370, 20) });
            require(clicks == 1, "Recycling must only cancel capture belonging to the rebound row");
        });
    }

    private static (UiDocument Document, VirtualList List) Fixture(float height, float padding, int count, float rowHeight)
    {
        var root = new Element();
        var list = root.Add(new VirtualList { ItemCount = count, RowHeight = rowHeight, Padding = padding, ItemText = i => "Row " + i, Transform = RectTransform.Fixed(0, 0, 300, height) });
        var doc = new UiDocument(root, new TestMeasurer(), 640, 480);
        doc.UpdateLayout();
        return (doc, list);
    }

    private static void Click(UiDocument doc, float x, float y)
    {
        doc.Dispatch(new(EventKind.PointerDown) { Position = new Vector2(x, y) });
        doc.Dispatch(new(EventKind.PointerUp) { Position = new Vector2(x, y) });
    }
}
