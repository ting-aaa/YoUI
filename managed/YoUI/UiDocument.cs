using System.Numerics;
using System.Runtime.ExceptionServices;

namespace YoUI;

public sealed record SemanticNode(string Name, string Role, string Value, Rect Bounds, bool Enabled, bool Focused);
public interface IClipboard { string GetText(); void SetText(string text); }

/// <summary>One UI thread; frozen dispatch paths permit safe tree mutation in handlers.</summary>
public sealed partial class UiDocument
{
    private DirtyFlags dirty = DirtyFlags.All;
    private Vector2 size;
    private Element? hovered, captured, modal, focusBeforeModal;
    private Theme theme = Theme.Dark;
    private readonly DrawList frame = new();
    private bool updating;
    private int dispatchDepth;
    private readonly List<UiEvent> pointerDispatches = [];
    private readonly int ownerThread = Environment.CurrentManagedThreadId;
    private readonly List<Element.DirtyState> consumedDirty = [];
    public Element Root { get; }
    public Element? Focused { get; private set; }
    public ITextMeasurer TextMeasurer { get; private set; }
    public IClipboard? Clipboard { get; set; }
    /// <summary>Host-level shortcuts and embedded documents can intercept input before focus routing.</summary>
    public event Action<UiEvent>? PreviewInput;
    public Theme Theme { get => theme; set { if (theme == value) return; theme = value; Invalidate(DirtyFlags.Paint); } }
    /// <summary>Unconsumed document stages. Layout also requests paint and semantics.</summary>
    public DirtyFlags PendingUpdates => dirty;
    /// <summary>Whether layout or paint is pending; semantic-only changes do not rebuild a frame.</summary>
    public bool NeedsRender => (dirty & (DirtyFlags.Layout | DirtyFlags.Paint)) != 0;
    public int LayoutPasses { get; private set; }
    public int PaintPasses { get; private set; }
    public UiDocument(Element root, ITextMeasurer textMeasurer, float width, float height)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(textMeasurer);
        if (root.Parent != null || root.Document != null) throw new InvalidOperationException("Root already belongs to a tree.");
        Root = root; TextMeasurer = textMeasurer; Resize(width, height);
        try { Root.Attach(this); }
        catch
        {
            // A failed constructor cannot return an owner for the associated tree.
            // Attempt every cleanup notification and preserve the original error.
            try { Detached(Root); } catch { }
            try { Root.Attach(null); } catch { }
            throw;
        }
    }
    /// <summary>Replace the host-owned text provider between updates on the creating thread.
    /// Discards cached commands and requests full layout, paint and semantics. Neither provider
    /// is disposed. Reupload/rebind image assets first; old image-bearing DrawLists remain stale.
    /// Calling with the same provider also invalidates changed provider configuration.</summary>
    public void RebindTextMeasurer(ITextMeasurer textMeasurer)
    {
        ArgumentNullException.ThrowIfNull(textMeasurer);
        if (Environment.CurrentManagedThreadId != ownerThread) throw new InvalidOperationException("Document rebind must stay on the creating thread.");
        if (updating || dispatchDepth != 0) throw new InvalidOperationException("Cannot rebind during a document update or input dispatch.");
        TextMeasurer = textMeasurer;
        frame.Clear();
        Root.Invalidate(DirtyFlags.All);
    }
    public void Resize(float width, float height)
    {
        if (!float.IsFinite(width) || !float.IsFinite(height) || width <= 0 || height <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        if (size == new Vector2(width, height)) return; size = new(width, height); Invalidate(DirtyFlags.All);
    }
    internal void Invalidate(DirtyFlags flags)
    {
        dirty |= Element.ExpandInvalidation(flags);
        PrunePopups();
        if (popups.Any(p => p.Anchor != p.Owner.VisualBounds)) dirty |= DirtyFlags.All;
    }
    private void BeginUpdate()
    {
        if (updating) throw new InvalidOperationException("Document updates cannot be nested inside layout, paint or semantic callbacks.");
        updating = true;
    }
    private List<Element.DirtyState> Consume(DirtyFlags flags)
    {
        var consumed = consumedDirty;
        consumed.Clear();
        Root.ConsumeDirty(flags, consumed);
        foreach (var popup in popups.ToArray()) if (popup.IsOpen) popup.Content.ConsumeDirty(flags, consumed);
        dirty &= ~flags;
        return consumed;
    }
    private void Restore(DirtyFlags flags, List<Element.DirtyState> consumed)
    {
        Invalidate(flags);
        // A callback may have detached or reparented a node. Restore through its current owner.
        foreach (var state in consumed) state.Node.Invalidate(state.Flags);
        Root.Invalidate(flags);
    }
    /// <summary>Run at most one layout pass. Callback invalidations remain pending for a later pass.</summary>
    public void EnsureLayout()
    {
        BeginUpdate();
        try { EnsureLayoutCore(); }
        finally { updating = false; }
    }
    /// <summary>Run up to maxPasses layout passes; false means layout is still pending. Never paints.</summary>
    public bool UpdateLayout(int maxPasses = 8)
    {
        if (maxPasses <= 0) throw new ArgumentOutOfRangeException(nameof(maxPasses));
        BeginUpdate();
        try
        {
            for (int pass = 0; pass < maxPasses && (dirty & DirtyFlags.Layout) != 0; pass++) EnsureLayoutCore();
            return (dirty & DirtyFlags.Layout) == 0;
        }
        finally { updating = false; }
    }
    private void EnsureLayoutCore()
    {
        if ((dirty & DirtyFlags.Layout) == 0) return;
        // Consume the entire stage before any callback, including Measure and custom Arrange overrides.
        var consumed = Consume(DirtyFlags.Layout);
        Invalidate(DirtyFlags.Paint | DirtyFlags.Semantics);
        try { Root.Measure(size); Root.Arrange(new(0, 0, size.X, size.Y)); ArrangePopups(); LayoutPasses++; }
        catch { Restore(DirtyFlags.Layout, consumed); throw; }
        finally { consumed.Clear(); }
    }
    public DrawList BuildFrame()
    {
        BeginUpdate();
        try
        {
            EnsureLayoutCore();
            if ((dirty & DirtyFlags.Paint) != 0)
            {
                var consumed = Consume(DirtyFlags.Paint);
                try
                {
                    frame.Clear(); frame.Rectangle(new(0, 0, size.X, size.Y), Theme.Background);
                    Root.PaintTree(frame, Vector2.Zero, 1, Theme);
                    if (popups.Count > 0)
                    {
                        frame.PushClip(new(0, 0, size.X, size.Y));
                        foreach (var popup in popups.ToArray()) if (popup.IsOpen) popup.Content.PaintTree(frame, Vector2.Zero, 1, Theme);
                        frame.PopClip();
                    }
                    PaintPasses++;
                }
                catch { Restore(DirtyFlags.Paint, consumed); throw; }
                finally { consumed.Clear(); }
            }
            return frame;
        }
        finally { updating = false; }
    }
    private static IEnumerable<Element> Traverse(Element root)
    {
        if (!root.Visible || !root.Enabled) yield break;
        yield return root; foreach (var child in root.Children) foreach (var n in Traverse(child)) yield return n;
    }
    private bool Active(Element node) => node.Document == this && (Traverse(modal ?? Root).Contains(node) || popups.Any(p => p.IsOpen && PopupOwnerActive(p.Owner) && Traverse(p.Content).Contains(node)));
    internal void Detached(Element root)
    {
        Exception? failure = null;
        void Cleanup(Action action) { try { action(); } catch (Exception error) { failure ??= error; } }
        foreach (var popup in popups.ToArray().Reverse())
            if (popup.Owner.IsDescendantOf(root) || popup.Content.IsDescendantOf(root)) Cleanup(popup.Dispose);
        if (Focused?.IsDescendantOf(root) == true) Cleanup(() => Focus(null));
        Cleanup(() => CancelPointer(root));
        if (hovered?.IsDescendantOf(root) == true) { hovered.IsHovered = false; hovered.Invalidate(); hovered = null; }
        if (modal?.IsDescendantOf(root) == true) Cleanup(() => SetModal(null));
        if (failure != null) ExceptionDispatchInfo.Capture(failure).Throw();
    }
    public void Focus(Element? element)
    {
        if (element != null && (!element.Focusable || !Active(element))) return;
        if (Focused == element) return;
        var old = Focused; Focused = element;
        // Commit the update request before callbacks, which can throw after focus has changed.
        old?.Invalidate(DirtyFlags.Paint | DirtyFlags.Semantics);
        element?.Invalidate(DirtyFlags.Paint | DirtyFlags.Semantics);
        if (old != null) old.Route(new(EventKind.Blur) { Target = old, Phase = EventPhase.Target });
        if (element != null) element.Route(new(EventKind.Focus) { Target = element, Phase = EventPhase.Target });
    }
    public void SetModal(Element? element)
    {
        if (element != null && (element.Document != this || !element.IsDescendantOf(Root))) throw new InvalidOperationException("Modal must belong to this document's root tree.");
        Exception? popupFailure = null;
        foreach (var popup in popups.ToArray().Reverse())
            try { popup.Dispose(); } catch (Exception error) { popupFailure ??= error; }
        if (popupFailure != null) ExceptionDispatchInfo.Capture(popupFailure).Throw();
        Invalidate(DirtyFlags.Semantics);
        if (element != null) { focusBeforeModal = Focused; modal = element; PrunePopups(); CancelPointer(); Focus(Traverse(element).FirstOrDefault(e => e.Focusable)); }
        else { var previous = focusBeforeModal; modal = null; focusBeforeModal = null; PrunePopups(); Focus(previous); }
    }
    // Recycled controls invalidate only gestures tied to their old subtree identity.
    internal void CancelPointer(Element? subtree)
    {
        HashSet<Element>? targets = null;
        bool Matches(Element target) => subtree == null || target.IsDescendantOf(subtree);
        if (captured != null && Matches(captured))
        {
            (targets ??= []).Add(captured);
            captured = null;
        }
        // Capture is committed only after bubbling. Invalidate in-flight targets too,
        // before callbacks, so nested dispatch and target handlers cannot revive them.
        foreach (var pending in pointerDispatches)
        {
            if (pending.PointerCancelled || pending.Target is not { } target || !Matches(target)) continue;
            pending.PointerCancelled = true;
            (targets ??= []).Add(target);
        }
        Exception? failure = null;
        if (targets != null)
            foreach (var target in targets)
                try { target.Route(new(EventKind.PointerCancel) { Target = target, Phase = EventPhase.Target }); }
                catch (Exception error) { failure ??= error; }
        if (failure != null) ExceptionDispatchInfo.Capture(failure).Throw();
    }
    public void CancelPointer() => CancelPointer(null);
    public void Dispatch(UiEvent e)
    {
        dispatchDepth++;
        try { DispatchCore(e); }
        finally { dispatchDepth--; }
    }
    private void DispatchCore(UiEvent e)
    {
        EnsureLayout();
        PreviewInput?.Invoke(e);
        if (e.Handled) return;
        if (popups.Count > 0)
        {
            var top = popups[^1];
            if (e.Kind == EventKind.KeyDown && e.Key == 27) { top.Dispose(); e.Handled = true; return; }
            if (e.Kind == EventKind.PointerDown && !top.Content.VisualBounds.Contains(e.Position))
            { top.Dispose(); e.Handled = true; return; }
        }
        if (Focused != null && !Active(Focused)) Focus(null);
        if (captured != null && !Active(captured)) CancelPointer();
        if (e.Kind == EventKind.KeyDown && e.Key == 9)
        {
            var all = Traverse(popups.Count > 0 ? popups[^1].Content : modal ?? Root).Where(n => n.Focusable).ToArray();
            if (all.Length > 0) { int index = Array.IndexOf(all, Focused); Focus(all[index < 0 ? (e.Shift ? all.Length - 1 : 0) : (index + (e.Shift ? all.Length - 1 : 1)) % all.Length]); }
            return;
        }
        bool pointer = e.Kind is EventKind.PointerDown or EventKind.PointerMove or EventKind.PointerUp or EventKind.Scroll;
        var hit = pointer ? HitTestPopups(e.Position) ?? (modal ?? Root).HitTest(e.Position, new(0, 0, size.X, size.Y), AncestorOffset(modal)) : null;
        if (e.Kind == EventKind.PointerMove && hovered != hit)
        {
            if (hovered != null) { hovered.IsHovered = false; hovered.Invalidate(); }
            hovered = hit;
            if (hovered != null) { hovered.IsHovered = true; hovered.Invalidate(); }
        }
        var target = pointer ? (e.Kind == EventKind.Scroll ? hit : captured ?? hit) : Focused;
        if (target == null) { if (e.Kind == EventKind.PointerDown) Focus(null); return; }
        e.Target = target;
        bool pointerGesture = pointer && e.Kind != EventKind.Scroll;
        if (pointerGesture) { e.PointerCancelled = false; pointerDispatches.Add(e); }
        try
        {
            if (e.Kind == EventKind.PointerDown) Focus(target.Focusable ? target : null);
            var path = new List<Element>(); for (var n = target; n != null; n = n.Parent) { path.Add(n); if (n == modal) break; }
            e.Phase = EventPhase.Capture;
            for (int i = path.Count - 1; i > 0 && !e.Handled; i--) path[i].Route(e);
            if (!e.Handled) { e.Phase = EventPhase.Target; target.Route(e); }
            e.Phase = EventPhase.Bubble;
            for (int i = 1; i < path.Count && !e.Handled; i++) path[i].Route(e);
            if (!e.PointerCancelled)
            {
                if (e.CapturePointer && Active(target)) captured = target;
                if (e.Kind is EventKind.PointerUp or EventKind.PointerCancel) captured = null;
            }
        }
        finally { if (pointerGesture) pointerDispatches.RemoveAt(pointerDispatches.Count - 1); }
    }
    private static Vector2 AncestorOffset(Element? node)
    {
        Vector2 result = Vector2.Zero; for (var p = node?.Parent; p != null; p = p.Parent) result += p.VisualOffset; return result;
    }
    public IReadOnlyList<SemanticNode> Semantics()
    {
        BeginUpdate();
        try
        {
            EnsureLayoutCore();
            var consumed = Consume(DirtyFlags.Semantics);
            try
            {
                return Traverse(modal ?? Root).Concat(popups.Where(p => p.IsOpen).SelectMany(p => Traverse(p.Content))).Where(e => e.Focusable || e.Role is "text" or "progressbar" or "combobox" or "spinbutton" or "tablist").Select(e => new SemanticNode(e.AccessibleName, e.Role, e.SemanticValue, e.Bounds.Translate(AncestorOffset(e) + e.VisualOffset), e.Enabled, e.IsFocused)).ToArray();
            }
            catch { Restore(DirtyFlags.Semantics, consumed); throw; }
            finally { consumed.Clear(); }
        }
        finally { updating = false; }
    }
}
