using System.Numerics;
using System.Runtime.CompilerServices;
using YoUI;
using YoUI.Components.Sample;

if (args.Length != 0)
{
    if (args.Length != 1 || args[0] != "--gpu") throw new ArgumentException("Supported argument: --gpu");
    using var renderer = new NativeRenderer(300, 100);
    var root = new Element();
    root.Add(new RatingControl(new RatingState { Value = 3 }) { Transform = RectTransform.Fixed(20, 30, 260, 40), ClipToBounds = true });
    var document = new UiDocument(root, renderer, 300, 100);
    var stats = renderer.Render(document.BuildFrame());
    if (stats.Instances < 3) throw new Exception("External control did not submit its background and fill.");
    Directory.CreateDirectory("artifacts"); renderer.SavePng("artifacts/custom-rating.png");
    root.Clear(); // End application-owned subscriptions before releasing the renderer.
    Console.WriteLine($"PASS external component GPU smoke: {renderer.Adapter}; {stats}");
    return;
}

int passed = 0;
void Require(bool value, string message = "contract failed") { if (!value) throw new Exception(message); }
void Test(string name, Action run) { run(); Console.WriteLine("PASS external component: " + name); passed++; }
UiDocument Doc(Element root) => new(root, new Measurer(), 300, 100);
void Clean(UiDocument doc) { Require(doc.UpdateLayout()); doc.BuildFrame(); doc.Semantics(); Require(doc.PendingUpdates == DirtyFlags.None); }

Test("real assembly boundary without friend access", () =>
{
    Require(typeof(RatingControl).Assembly != typeof(Element).Assembly);
    Require(typeof(RatingControl).Assembly != typeof(Program).Assembly);
    Require(!typeof(Element).Assembly.GetCustomAttributes(typeof(InternalsVisibleToAttribute), false).Any());
});
Test("descriptor normalization, notifications and phase invalidation", () =>
{
    var state = new RatingState(); var root = new Element(); var rating = root.Add(new RatingControl(state)); var doc = Doc(root);
    Clean(doc); int count = 0; rating.Changed += _ => count++;
    rating.Value = 99;
    Require(rating.Value == 5 && state.Value == 5 && count == 1);
    Require(doc.PendingUpdates == (DirtyFlags.Paint | DirtyFlags.Semantics));
    int layouts = doc.LayoutPasses; Clean(doc); rating.Value = 6;
    Require(count == 1 && doc.PendingUpdates == DirtyFlags.None && doc.LayoutPasses == layouts);
    rating.Padding = 3; Require(doc.PendingUpdates == DirtyFlags.All); Clean(doc);
});
Test("layout convention, inherited coordinates, opacity and ordered IR", () =>
{
    var state = new RatingState { Value = 3 }; var root = new Element { VisualOffset = new(7, 2), InheritedOpacity = .5f };
    var rating = root.Add(new RatingControl(state) { Transform = RectTransform.Fixed(10, 10, 100, 30), VisualOffset = new(3, 4), Padding = 2, InheritedOpacity = .5f, ClipToBounds = true, CompositedOpacity = .8f });
    var doc = Doc(root); var frame = doc.BuildFrame();
    Require(rating.Bounds == new Rect(10, 10, 100, 30));
    Require(rating.VisualBounds == new Rect(20, 16, 100, 30));
    Require(rating.Measure(new(20, 20)) == new Vector2(164, 36));
    var commands = frame.Commands.ToArray();
    Require(commands.Select(c => c.Kind).SequenceEqual(new uint[] { 0, 2, 4, 0, 0, 5, 3 }));
    Require(commands[1].Rect == rating.VisualBounds && commands[3].Rect == rating.VisualBounds.Inset(2));
    Require(commands[4].Rect.Width == 57.6f && commands[4].Color.A == .25f);
    var semantics = doc.Semantics().Single();
    Require(semantics.Role == "slider" && semantics.Value == "3 of 5" && semantics.Bounds == rating.VisualBounds);
});
Test("focus, target events, capture outside bounds, cancellation and interception", () =>
{
    var root = new Element(); var rating = root.Add(new RatingControl(new()) { Transform = RectTransform.Fixed(10, 10, 100, 30) }); var doc = Doc(root);
    doc.Dispatch(new(EventKind.KeyDown) { Key = 9 }); Require(doc.Focused == rating);
    doc.Dispatch(new(EventKind.KeyDown) { Key = 39 }); Require(rating.Value == 1);
    doc.Dispatch(new(EventKind.PointerDown) { Position = new(60, 20) });
    doc.Dispatch(new(EventKind.PointerMove) { Position = new(280, 90) }); Require(rating.Value == 5);
    doc.CancelPointer(); doc.Dispatch(new(EventKind.PointerMove) { Position = new(10, 20) }); Require(rating.Value == 5);
    rating.Event += e => { if (e.Kind == EventKind.KeyDown) e.Handled = true; };
    doc.Dispatch(new(EventKind.KeyDown) { Key = 36 }); Require(rating.Value == 5);
    rating.Enabled = false; doc.Dispatch(new(EventKind.PointerDown) { Position = new(10, 20) });
    Require(doc.Focused == null && doc.Semantics().Count == 0);
});
Test("detachment and repeated cross-document attachment release subscriptions", () =>
{
    var state = new RatingState { Value = 2 }; var rating = new RatingControl(state); var a = new Element(); var b = new Element(); var da = Doc(a); var db = Doc(b);
    a.Add(rating); Require(rating.Value == 2); int notifications = 0; rating.Changed += _ => notifications++;
    for (int i = 0; i < 3; i++)
    {
        da.Focus(rating); da.Dispatch(new(EventKind.PointerDown) { Position = new(20, 20) });
        Require(a.Remove(rating) && rating.Document == null && rating.Parent == null && da.Focused == null);
        Clean(da); state.Value = 4; rating.Value = 1; Require(state.Value == 4 && da.PendingUpdates == DirtyFlags.None);
        b.Add(rating); Require(rating.Value == 4 && rating.Document == db); state.Value = 3; Require(rating.Value == 3);
        b.Clear(); a.Add(rating); Require(rating.Value == 3); Clean(da); Clean(db);
        int before = notifications; state.Value = 2; Require(notifications == before + 1 && db.PendingUpdates == DirtyFlags.None);
    }
    a.Clear(); Clean(da); state.Value = 5; Require(rating.Value == 2 && da.PendingUpdates == DirtyFlags.None);
});
Test("synchronous model and control corrections converge on initial and repeat attachment", () =>
{
    var state = new RatingState { Value = 3 };
    state.Changed += value => { if (value == 4) state.Value = 2; };
    var rating = new RatingControl(state);
    rating.Changed += value => { if (value == 3) rating.Value = 1; };
    var root = new Element(); var doc = Doc(root);
    for (int i = 0; i < 3; i++)
    {
        state.Value = 3; root.Add(rating); Require(state.Value == 1 && rating.Value == 1);
        state.Value = 4; Require(state.Value == 2 && rating.Value == 2);
        rating.Value = 3; Require(state.Value == 1 && rating.Value == 1);
        root.Remove(rating); rating.Value = 0;
    }
});
Test("detachment during initial synchronization leaves no subscriptions", () =>
{
    var state = new RatingState { Value = 3 }; var rating = new RatingControl(state);
    var root = new Element(); _ = Doc(root);
    rating.Changed += _ => root.Remove(rating);
    root.Add(rating); Require(rating.Document == null && rating.Parent == null);
    state.Value = 4; Require(rating.Value == 3);
    rating.Value = 2; Require(state.Value == 4);
});
Test("detached view can be collected while application model survives", () =>
{
    var state = new RatingState(); var reference = DetachedReference(state);
    for (int i = 0; i < 3 && reference.IsAlive; i++) { GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect(); }
    Require(!reference.IsAlive); GC.KeepAlive(state);
});
Test("explicit sizing, stack ownership, hidden state and cycle rejection", () =>
{
    var root = new StackPanel { Padding = 5, Gap = 3 };
    var first = root.Add(new RatingControl(new()) { Width = 80, Height = 24 });
    var second = root.Add(new RatingControl(new())); var doc = Doc(root); Clean(doc);
    Require(first.DesiredSize == new Vector2(80, 24) && first.Bounds == new Rect(5, 5, 80, 24));
    Require(second.Bounds.Y == 32 && second.Bounds.Width == 290);
    second.Visible = false; Clean(doc); Require(doc.Semantics().Count == 1);
    second.Value = 3; Clean(doc); second.Visible = true; Clean(doc); Require(doc.Semantics().Count == 2);
    bool rejected = false; try { first.Add(root); } catch (InvalidOperationException) { rejected = true; }
    Require(rejected && root.Parent == null && first.Parent == root);
    rejected = false; try { new Element().Add(first); } catch (InvalidOperationException) { rejected = true; }
    Require(rejected && first.Parent == root);
    first.Transform = RectTransform.Fixed(0, 0, 20, 20);
    rejected = false; try { doc.UpdateLayout(); } catch (InvalidOperationException) { rejected = true; }
    Require(rejected); first.Transform = null; Clean(doc);
});
Test("hover state is cleared on detach before reuse", () =>
{
    var root = new Element(); var rating = root.Add(new RatingControl(new())); var doc = Doc(root);
    doc.Dispatch(new(EventKind.PointerMove) { Position = new(20, 20) }); Require(rating.IsHovered);
    root.Remove(rating); Require(!rating.IsHovered); root.Add(rating); Require(!rating.IsHovered);
});
Console.WriteLine($"{passed} external component contracts passed; no native renderer or window required.");

[MethodImpl(MethodImplOptions.NoInlining)]
static WeakReference DetachedReference(RatingState state)
{
    var root = new Element(); var rating = root.Add(new RatingControl(state)); _ = new UiDocument(root, new Measurer(), 300, 100);
    root.Clear(); return new WeakReference(rating);
}
sealed class Measurer : ITextMeasurer
{
    public (float Width, float Height) Measure(string text, float size, float width, bool bold = false) => (Math.Min(width, text.Length * size / 2), size);
}
