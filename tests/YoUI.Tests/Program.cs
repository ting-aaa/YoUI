using System.Numerics;
using System.Runtime.InteropServices;
using YoUI;

int passed = 0;
void Test(string name, Action run) { run(); Console.WriteLine("PASS " + name); passed++; }
void Require(bool condition, string message = "assertion failed") { if (!condition) throw new Exception(message); }
UiDocument Doc(Element root, float w = 640, float h = 480) => new(root, new TestMeasurer(), w, h);
void Click(UiDocument doc, float x, float y) { doc.Dispatch(new(EventKind.PointerDown) { Position = new(x, y) }); doc.Dispatch(new(EventKind.PointerUp) { Position = new(x, y) }); }

Test("ABI sizes and offsets", () => { Require(Marshal.SizeOf<DrawCommand>() == 80); Require(Marshal.OffsetOf<DrawCommand>(nameof(DrawCommand.Rect)).ToInt32() == 16); Require(Marshal.SizeOf<FrameStats>() == 48); });
Test("stretched and centered RectTransform", () =>
{
    Require(RectTransform.Stretch(10, 20, 30, 40).Resolve(new(0, 0, 200, 100)) == new Rect(10, 20, 160, 40));
    Require(new RectTransform(new(.5f), new(.5f), new(.5f), Vector2.Zero, new(40, 20)).Resolve(new(10, 20, 200, 100)) == new Rect(90, 60, 40, 20));
});
Test("paint changes and visual offset do not relayout", () =>
{
    var root = new Element(); var child = root.Add(new Button("A") { Transform = RectTransform.Fixed(10, 10, 100, 40) }); var doc = Doc(root); doc.BuildFrame(); int passes = doc.LayoutPasses;
    child.Background = Color.Hex(0x123456); child.VisualOffset = new(20, 0); doc.BuildFrame(); Require(doc.LayoutPasses == passes);
    int clicks = 0; child.Clicked += () => clicks++; Click(doc, 15, 15); Require(clicks == 0); Click(doc, 45, 20); Require(clicks == 1);
});
Test("idle document reuses command arena without repaint", () => { var doc = Doc(new Element()); var first = doc.BuildFrame(); var second = doc.BuildFrame(); Require(ReferenceEquals(first, second) && doc.PaintPasses == 1 && !doc.NeedsRender); });
Test("clipped frontmost hit target and capture cancellation", () =>
{
    var root = new Element(); var back = root.Add(new Button("Back") { Transform = RectTransform.Fixed(0, 0, 200, 100) });
    var panel = root.Add(new Element { ClipToBounds = true, Transform = RectTransform.Fixed(50, 0, 50, 80) }); var front = panel.Add(new Button("Front") { Transform = RectTransform.Fixed(-30, 0, 150, 80) });
    var doc = Doc(root); int a = 0, b = 0; back.Clicked += () => a++; front.Clicked += () => b++;
    Click(doc, 30, 20); Click(doc, 60, 20); Require(a == 1 && b == 1);
    doc.Dispatch(new(EventKind.PointerDown) { Position = new(60, 20) }); doc.CancelPointer(); doc.Dispatch(new(EventKind.PointerUp) { Position = new(60, 20) }); Require(b == 1);
});
Test("capture-target-bubble path remains stable during detach", () =>
{
    var root = new Element(); var panel = root.Add(new Element()); var button = panel.Add(new Button("X")); var doc = Doc(root);
    var trace = new List<string>();
    root.Event += e => { if (e.Kind == EventKind.PointerDown) trace.Add("root-" + e.Phase); };
    panel.Event += e => { if (e.Kind == EventKind.PointerDown) { trace.Add("panel-" + e.Phase); if (e.Phase == EventPhase.Capture) panel.Remove(button); } };
    button.Event += e => { if (e.Kind == EventKind.PointerDown) trace.Add("button-" + e.Phase); };
    doc.Dispatch(new(EventKind.PointerDown) { Position = new(20, 20) });
    Require(string.Join(",", trace) == "root-Capture,panel-Capture,button-Target,panel-Bubble,root-Bubble"); Require(button.Document == null);
});
Test("keyboard focus skips disabled controls and cycles", () =>
{
    var root = new Element(); var first = root.Add(new Button("One")); root.Add(new Button("Disabled") { Enabled = false }); var last = root.Add(new Button("Last")); var doc = Doc(root);
    doc.Dispatch(new(EventKind.KeyDown) { Key = 9 }); Require(doc.Focused == first);
    doc.Dispatch(new(EventKind.KeyDown) { Key = 9 }); Require(doc.Focused == last);
    doc.Dispatch(new(EventKind.KeyDown) { Key = 9, Shift = true }); Require(doc.Focused == first);
    doc.Focus(null); doc.Dispatch(new(EventKind.KeyDown) { Key = 9, Shift = true }); Require(doc.Focused == last);
});
Test("modal blocks background and restores previous focus", () =>
{
    var root = new Element(); var background = root.Add(new Button("Background") { Transform = RectTransform.Fixed(0, 0, 100, 40) }); var modal = root.Add(new Element { HitTestVisible = true }); var confirm = modal.Add(new Button("Confirm") { Transform = RectTransform.Fixed(100, 100, 100, 40) }); var doc = Doc(root); int clicked = 0; background.Clicked += () => clicked++;
    doc.Focus(background); doc.SetModal(modal); Click(doc, 10, 10); Require(clicked == 0); doc.Dispatch(new(EventKind.KeyDown) { Key = 9 }); Require(doc.Focused == confirm); doc.SetModal(null); Require(doc.Focused == background);
});
Test("text editing removes complete Unicode graphemes", () =>
{
    var root = new Element(); var input = root.Add(new TextBox()); var doc = Doc(root); doc.Focus(input);
    doc.Dispatch(new(EventKind.TextInput) { Text = "A👨‍👩‍👧‍👦é" }); doc.Dispatch(new(EventKind.KeyDown) { Key = 8 }); Require(input.Text == "A👨‍👩‍👧‍👦"); doc.Dispatch(new(EventKind.KeyDown) { Key = 8 }); Require(input.Text == "A");
    doc.Dispatch(new(EventKind.Composition) { Text = "中文" }); Require(input.Text == "A"); doc.Dispatch(new(EventKind.TextInput) { Text = "中文" }); Require(input.Text == "A中文");
});
Test("virtual list realizes viewport only and rebinds IDs", () =>
{
    var root = new Element(); var list = root.Add(new VirtualList { ItemCount = 100000, RowHeight = 50, ItemText = i => "Row " + i, Transform = RectTransform.Fixed(0, 0, 300, 200) }); var doc = Doc(root); doc.BuildFrame();
    Require(list.RealizedCount == 7); var row = list.Children[2]; list.ScrollOffset = 25000; doc.BuildFrame(); Require(list.RealizedCount == 7 && list.FirstVisibleIndex == 500 && ReferenceEquals(row, list.Children[2]));
    Click(doc, 20, 75); Require(list.SelectedIndex == 501); list.ItemCount = 2; doc.BuildFrame(); Require(list.RealizedCount == 2 && list.ScrollOffset == 0);
});
Test("clipboard selection replacement and undo redo", () =>
{
    var root = new Element(); var input = root.Add(new TextBox { Text = "Hello" }); var doc = Doc(root); var clipboard = new TestClipboard { Value = "Aurora" }; doc.Clipboard = clipboard; doc.Focus(input);
    doc.Dispatch(new(EventKind.KeyDown) { Key = 65, Control = true }); Require(input.SelectedText == "Hello");
    doc.Dispatch(new(EventKind.KeyDown) { Key = 86, Control = true }); Require(input.Text == "Aurora");
    doc.Dispatch(new(EventKind.KeyDown) { Key = 90, Control = true }); Require(input.Text == "Hello");
    doc.Dispatch(new(EventKind.KeyDown) { Key = 89, Control = true }); Require(input.Text == "Aurora");
    doc.Dispatch(new(EventKind.KeyDown) { Key = 65, Control = true }); doc.Dispatch(new(EventKind.KeyDown) { Key = 88, Control = true }); Require(input.Text == "" && clipboard.Value == "Aurora");
});
Test("layout controller ownership is enforced", () =>
{
    var root = new StackPanel(); root.Add(new Button("Conflict") { Transform = RectTransform.Fixed(1, 1, 20, 20) }); var doc = Doc(root);
    bool rejected = false; try { doc.BuildFrame(); } catch (InvalidOperationException) { rejected = true; } Require(rejected);
});
Test("stack measures wrapping after width assignment", () =>
{
    var root = new StackPanel { Gap = 10, Padding = 10 }; var label = root.Add(new Label(new string('x', 40))); var button = root.Add(new Button("Next")); var doc = Doc(root, 100, 200); doc.BuildFrame(); Require(label.Bounds.Width == 80 && button.Bounds.Y >= label.Bounds.Bottom + 10);
});
Test("cycle and double attachment rejected", () =>
{
    var root = new Element(); var child = root.Add(new Element()); bool rejected = false; try { child.Add(root); } catch (InvalidOperationException) { rejected = true; } Require(rejected);
    rejected = false; try { root.Add(child); } catch (InvalidOperationException) { rejected = true; } Require(rejected);
});
Console.WriteLine($"{passed} managed tests passed");

// Logic-test fixture only. Production measurement always comes from COSMIC Text.
sealed class TestMeasurer : ITextMeasurer
{
    public (float Width, float Height) Measure(string text, float size, float width, bool bold = false) => (Math.Min(width, text.Length * size * 0.5f), Math.Max(1, MathF.Ceiling(text.Length * size * 0.5f / Math.Max(1, width))) * size * 1.35f);
}
sealed class TestClipboard : IClipboard { public string Value = ""; public string GetText() => Value; public void SetText(string text) => Value = text; }
