using System.Numerics;

namespace YoUI;

public sealed record SemanticNode(string Name, string Role, string Value, Rect Bounds, bool Enabled, bool Focused);
public interface IClipboard { string GetText(); void SetText(string text); }

/// <summary>One UI thread; frozen dispatch paths permit safe tree mutation in handlers.</summary>
public sealed class UiDocument
{
    private DirtyFlags dirty = DirtyFlags.All;
    private Vector2 size;
    private Element? hovered, captured, modal, focusBeforeModal;
    private Theme theme = Theme.Dark;
    private readonly DrawList frame = new();
    public Element Root { get; }
    public Element? Focused { get; private set; }
    public ITextMeasurer TextMeasurer { get; }
    public IClipboard? Clipboard { get; set; }
    /// <summary>Host-level shortcuts and embedded documents can intercept input before focus routing.</summary>
    public event Action<UiEvent>? PreviewInput;
    public Theme Theme { get => theme; set { theme = value; Invalidate(DirtyFlags.Paint); } }
    public bool NeedsRender => dirty != DirtyFlags.None;
    public int LayoutPasses { get; private set; }
    public int PaintPasses { get; private set; }
    public UiDocument(Element root, ITextMeasurer textMeasurer, float width, float height)
    {
        if (root.Parent != null || root.Document != null) throw new InvalidOperationException("Root already belongs to a tree.");
        Root = root; TextMeasurer = textMeasurer; Root.Attach(this); Resize(width, height);
    }
    public void Resize(float width, float height)
    {
        if (!float.IsFinite(width) || !float.IsFinite(height) || width <= 0 || height <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        if (size == new Vector2(width, height)) return; size = new(width, height); Invalidate(DirtyFlags.All);
    }
    internal void Invalidate(DirtyFlags flags) => dirty |= flags;
    public void EnsureLayout()
    {
        if ((dirty & DirtyFlags.Layout) == 0) return;
        Root.Measure(size); Root.Arrange(new(0, 0, size.X, size.Y)); LayoutPasses++; dirty &= ~DirtyFlags.Layout;
    }
    public DrawList BuildFrame()
    {
        EnsureLayout();
        if ((dirty & DirtyFlags.Paint) != 0)
        {
            frame.Clear(); frame.Rectangle(new(0, 0, size.X, size.Y), Theme.Background);
            Root.PaintTree(frame, Vector2.Zero, 1, Theme); PaintPasses++;
        }
        dirty = DirtyFlags.None; return frame;
    }
    private static IEnumerable<Element> Traverse(Element root)
    {
        if (!root.Visible || !root.Enabled) yield break;
        yield return root; foreach (var child in root.Children) foreach (var n in Traverse(child)) yield return n;
    }
    private bool Active(Element node) => node.Document == this && Traverse(modal ?? Root).Contains(node);
    internal void Detached(Element root)
    {
        if (Focused?.IsDescendantOf(root) == true) Focus(null);
        if (captured?.IsDescendantOf(root) == true) CancelPointer();
        if (hovered?.IsDescendantOf(root) == true) hovered = null;
        if (modal?.IsDescendantOf(root) == true) SetModal(null);
    }
    public void Focus(Element? element)
    {
        if (element != null && (!element.Focusable || !Active(element))) return;
        if (Focused == element) return;
        var old = Focused; Focused = element;
        if (old != null) { old.Route(new(EventKind.Blur) { Target = old, Phase = EventPhase.Target }); old.Invalidate(); }
        if (element != null) { element.Route(new(EventKind.Focus) { Target = element, Phase = EventPhase.Target }); element.Invalidate(); }
    }
    public void SetModal(Element? element)
    {
        if (element != null && element.Document != this) throw new InvalidOperationException("Modal must be attached to this document.");
        if (element != null) { focusBeforeModal = Focused; modal = element; CancelPointer(); Focus(Traverse(element).FirstOrDefault(e => e.Focusable)); }
        else { modal = null; Focus(focusBeforeModal); focusBeforeModal = null; }
    }
    public void CancelPointer()
    {
        var old = captured; captured = null;
        if (old != null) old.Route(new(EventKind.PointerCancel) { Target = old, Phase = EventPhase.Target });
    }
    public void Dispatch(UiEvent e)
    {
        EnsureLayout();
        PreviewInput?.Invoke(e);
        if (e.Handled) return;
        if (Focused != null && !Active(Focused)) Focus(null);
        if (captured != null && !Active(captured)) CancelPointer();
        if (e.Kind == EventKind.KeyDown && e.Key == 9)
        {
            var all = Traverse(modal ?? Root).Where(n => n.Focusable).ToArray();
            if (all.Length > 0) { int index = Array.IndexOf(all, Focused); Focus(all[index < 0 ? (e.Shift ? all.Length - 1 : 0) : (index + (e.Shift ? all.Length - 1 : 1)) % all.Length]); }
            return;
        }
        bool pointer = e.Kind is EventKind.PointerDown or EventKind.PointerMove or EventKind.PointerUp or EventKind.Scroll;
        var hit = pointer ? (modal ?? Root).HitTest(e.Position, new(0, 0, size.X, size.Y), AncestorOffset(modal)) : null;
        if (e.Kind == EventKind.PointerMove && hovered != hit)
        {
            if (hovered != null) { hovered.IsHovered = false; hovered.Invalidate(); }
            hovered = hit;
            if (hovered != null) { hovered.IsHovered = true; hovered.Invalidate(); }
        }
        var target = pointer ? (e.Kind == EventKind.Scroll ? hit : captured ?? hit) : Focused;
        if (target == null) { if (e.Kind == EventKind.PointerDown) Focus(null); return; }
        if (e.Kind == EventKind.PointerDown) Focus(target.Focusable ? target : null);
        e.Target = target;
        var path = new List<Element>(); for (var n = target; n != null; n = n.Parent) { path.Add(n); if (n == modal) break; }
        e.Phase = EventPhase.Capture;
        for (int i = path.Count - 1; i > 0 && !e.Handled; i--) path[i].Route(e);
        if (!e.Handled) { e.Phase = EventPhase.Target; target.Route(e); }
        e.Phase = EventPhase.Bubble;
        for (int i = 1; i < path.Count && !e.Handled; i++) path[i].Route(e);
        if (e.CapturePointer && Active(target)) captured = target;
        if (e.Kind is EventKind.PointerUp or EventKind.PointerCancel) captured = null;
    }
    private static Vector2 AncestorOffset(Element? node)
    {
        Vector2 result = Vector2.Zero; for (var p = node?.Parent; p != null; p = p.Parent) result += p.VisualOffset; return result;
    }
    public IReadOnlyList<SemanticNode> Semantics()
    {
        EnsureLayout();
        return Traverse(modal ?? Root).Where(e => e.Focusable || e.Role == "text").Select(e => new SemanticNode(e.AccessibleName, e.Role, e.SemanticValue, e.Bounds.Translate(AncestorOffset(e) + e.VisualOffset), e.Enabled, e.IsFocused)).ToArray();
    }
}
