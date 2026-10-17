using System.Numerics;
using System.Runtime.ExceptionServices;

namespace YoUI;

[Flags] public enum DirtyFlags { None = 0, Paint = 1, Layout = 2, Semantics = 4, All = 7 }
public enum EventKind { PointerMove, PointerDown, PointerUp, PointerCancel, Scroll, KeyDown, TextInput, Composition, Focus, Blur }
public enum EventPhase { Capture, Target, Bubble }
public sealed class UiEvent(EventKind kind)
{
    public EventKind Kind { get; } = kind;
    public Vector2 Position { get; init; }
    public float Delta { get; init; }
    public int Key { get; init; }
    public bool Shift { get; init; }
    public bool Control { get; init; }
    public string Text { get; init; } = "";
    public EventPhase Phase { get; internal set; }
    public Element? Target { get; internal set; }
    public bool Handled { get; set; }
    public bool CapturePointer { get; set; }
    internal bool PointerCancelled { get; set; }
}

/// <summary>Retained node. Behaviors route events; Paint emits independent render IR.</summary>
public class Element
{
    private readonly List<Element> children = [];
    private readonly IReadOnlyList<Element> readOnlyChildren;
    private bool visible = true, enabled = true, clip;
    private RectTransform? transform;
    private BoxLayout? layout;
    private float width = float.NaN, height = float.NaN, grow, padding, radius, opacity = 1, groupOpacity = 1;
    private Color background;
    private Vector2 visualOffset;
    private string accessibleName = "";
    private long documentRevision;
    public Element() => readOnlyChildren = children.AsReadOnly();
    public string Name { get; init; } = "";
    public Element? Parent { get; private set; }
    public UiDocument? Document { get; internal set; }
    public IReadOnlyList<Element> Children => readOnlyChildren;
    public Rect Bounds { get; private set; }
    public Vector2 DesiredSize { get; protected set; }
    /// <summary>Pending work from this node or its descendants; consumed at document stage boundaries.</summary>
    public DirtyFlags Dirty { get; private set; } = DirtyFlags.All;
    public long LayoutRevision { get; private set; }
    public bool Focusable { get; protected set; }
    public bool HitTestVisible { get; set; }
    public string Role { get; protected set; } = "group";
    public string AccessibleName { get => accessibleName; set => Set(ref accessibleName, value, DirtyFlags.Semantics); }
    public bool IsHovered { get; internal set; }
    public bool IsFocused => Document?.Focused == this;
    public bool Visible { get => visible; set => Set(ref visible, value, DirtyFlags.All); }
    public bool Enabled { get => enabled; set => Set(ref enabled, value, DirtyFlags.Paint | DirtyFlags.Semantics); }
    public bool ClipToBounds { get => clip; set => Set(ref clip, value, DirtyFlags.Paint); }
    public RectTransform? Transform
    {
        get => transform;
        set { if (value != null && layout != null) throw new InvalidOperationException("RectTransform and BoxLayout cannot own the same element."); Set(ref transform, value, DirtyFlags.Layout); }
    }
    public BoxLayout? Layout
    {
        get => layout;
        set { value?.Validate(); if (value != null && transform != null) throw new InvalidOperationException("RectTransform and BoxLayout cannot own the same element."); Set(ref layout, value, DirtyFlags.Layout); }
    }
    public float Width { get => width; set => Set(ref width, Dimension(value), DirtyFlags.Layout); }
    public float Height { get => height; set => Set(ref height, Dimension(value), DirtyFlags.Layout); }
    public float Grow { get => grow; set => Set(ref grow, NonNegative(value), DirtyFlags.Layout); }
    public float Padding { get => padding; set => Set(ref padding, NonNegative(value), DirtyFlags.Layout); }
    public float Radius { get => radius; set => Set(ref radius, NonNegative(value), DirtyFlags.Paint); }
    public Color Background { get => background; set => Set(ref background, value, DirtyFlags.Paint); }
    public float InheritedOpacity { get => opacity; set => Set(ref opacity, Unit(value), DirtyFlags.Paint); }
    public float CompositedOpacity { get => groupOpacity; set => Set(ref groupOpacity, Unit(value), DirtyFlags.Paint); }
    public Vector2 VisualOffset { get => visualOffset; set => Set(ref visualOffset, value, DirtyFlags.Paint | DirtyFlags.Semantics); }
    public Rect VisualBounds { get { var offset = Vector2.Zero; for (Element? p = this; p != null; p = p.Parent) offset += p.VisualOffset; return Bounds.Translate(offset); } }
    public event Action<UiEvent>? Event;
    protected static float NonNegative(float value) => float.IsFinite(value) && value >= 0 ? value : throw new ArgumentOutOfRangeException(nameof(value));
    private static float Dimension(float value) => float.IsNaN(value) ? value : NonNegative(value);
    private static float Unit(float value) => float.IsFinite(value) ? Math.Clamp(value, 0, 1) : throw new ArgumentOutOfRangeException(nameof(value));
    protected bool Set<T>(ref T field, T value, DirtyFlags flags)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value; Invalidate(flags); return true;
    }
    /// <summary>Assign a field-backed typed property; no notification for an unchanged normalized value.</summary>
    protected static bool SetProperty<TOwner, TValue>(TOwner owner, ref TValue field, TValue value,
        UiProperty<TOwner, TValue> property) where TOwner : Element
    {
        value = property.Normalize(value);
        if (EqualityComparer<TValue>.Default.Equals(field, value)) return false;
        var previous = field;
        field = value;
        owner.Invalidate(property.Invalidation);
        property.Notify(owner, previous, value);
        return true;
    }
    public void Invalidate(DirtyFlags flags = DirtyFlags.Paint)
    {
        flags = ExpandInvalidation(flags);
        // Notify the owning document once; ancestors aggregate pending work without recursion.
        for (Element? node = this; node != null; node = node.Parent) node.Dirty |= flags;
        Document?.Invalidate(flags);
    }
    internal static DirtyFlags ExpandInvalidation(DirtyFlags flags) =>
        (flags & DirtyFlags.Layout) != 0 ? flags | DirtyFlags.Paint | DirtyFlags.Semantics : flags;
    internal readonly record struct DirtyState(Element Node, DirtyFlags Flags);
    internal void ConsumeDirty(DirtyFlags flags, List<DirtyState> consumed)
    {
        var pending = Dirty & flags;
        if (pending != DirtyFlags.None) { consumed.Add(new(this, pending)); Dirty &= ~flags; }
        // Hidden nodes are acknowledged too. Showing or reattaching them requests fresh layout.
        foreach (var child in children) child.ConsumeDirty(flags, consumed);
    }
    public T Add<T>(T child) where T : Element
    {
        for (Element? p = this; p != null; p = p.Parent) if (p == child) throw new InvalidOperationException("A node cannot be its own ancestor.");
        if (child.Parent != null || child.Document != null) throw new InvalidOperationException("Detach a node before reparenting it.");
        children.Add(child); child.Parent = this;
        try { child.Attach(Document); }
        finally { Invalidate(DirtyFlags.All); }
        return child;
    }
    public bool Remove(Element child)
    {
        if (!children.Remove(child)) return false;
        Exception? failure = null;
        try { Document?.Detached(child); } catch (Exception error) { failure = error; }
        child.Parent = null;
        try { child.Attach(null); } catch (Exception error) { failure ??= error; }
        Invalidate(DirtyFlags.All);
        if (failure != null) ExceptionDispatchInfo.Capture(failure).Throw();
        return true;
    }
    public void Clear() { foreach (var child in children.ToArray()) Remove(child); }
    /// <summary>Change draw, hit-test and traversal order without detaching retained children.</summary>
    public void ReorderChildren(IEnumerable<Element> order)
    {
        ArgumentNullException.ThrowIfNull(order);
        var next = order.ToArray();
        if (next.Length != children.Count || next.Distinct(ReferenceEqualityComparer.Instance).Count() != next.Length || next.Any(c => c == null || c.Parent != this))
            throw new ArgumentException("The order must contain every current child exactly once.", nameof(order));
        if (children.SequenceEqual(next)) return;
        children.Clear(); children.AddRange(next); Invalidate(DirtyFlags.All);
    }
    /// <summary>Convert window coordinates to this element's local origin, including ancestor visual offsets.</summary>
    public Vector2 WindowToLocal(Vector2 position) => position - new Vector2(VisualBounds.X, VisualBounds.Y);
    internal void Attach(UiDocument? document)
    {
        var notifications = new List<(Element Node, UiDocument? Previous, long Revision)>();
        void Associate(Element node)
        {
            if (node.Document == document) return;
            var previous = node.Document; node.Document = document; node.documentRevision++;
            foreach (var child in node.children) Associate(child);
            notifications.Add((node, previous, node.documentRevision));
        }
        // Commit the entire subtree association before invoking user notifications.
        // A failing notification must not leave a sibling in the former document.
        Associate(this);
        Exception? failure = null;
        foreach (var (node, previous, revision) in notifications)
        {
            // An earlier notification may detach or reattach this subtree. Its
            // newer transition owns notifications; never replay the stale one.
            if (node.documentRevision != revision || node.Document != document) continue;
            try { node.OnDocumentChanged(previous, document); } catch (Exception error) { failure ??= error; }
        }
        if (failure != null) ExceptionDispatchInfo.Capture(failure).Throw();
    }
    /// <summary>Pair external subscriptions with document attachment; detach releases them.</summary>
    protected virtual void OnDocumentChanged(UiDocument? previous, UiDocument? current) { }
    internal bool IsDescendantOf(Element root) { for (Element? n = this; n != null; n = n.Parent) if (n == root) return true; return false; }
    internal Thickness LayoutMargin => Layout?.Margin ?? default;
    internal Vector2 DesiredSizeWithMargin => DesiredSize + new Vector2(LayoutMargin.Horizontal, LayoutMargin.Vertical);
    // Parent layout controllers measure border sizes; margins are accounted for once by the parent.
    internal Vector2 MeasureForLayout(Vector2 available)
    {
        if (Layout == null) return Measure(available);
        var constraint = Layout.MeasureConstraint(available);
        var measured = Measure(constraint);
        DesiredSize = Layout.Constrain(available, measured);
        return DesiredSize;
    }
    public virtual Vector2 Measure(Vector2 available)
    {
        float w = 0, h = 0;
        foreach (var child in children.Where(c => c.Visible)) { child.MeasureForLayout(Vector2.Max(Vector2.Zero, available - new Vector2(Padding * 2))); var size = child.DesiredSizeWithMargin; w = Math.Max(w, size.X); h = Math.Max(h, size.Y); }
        DesiredSize = new(float.IsNaN(Width) ? w + Padding * 2 : Width, float.IsNaN(Height) ? h + Padding * 2 : Height); return DesiredSize;
    }
    public virtual void Arrange(Rect bounds)
    {
        Bounds = bounds; LayoutRevision++; ArrangeChildren(bounds.Inset(Padding));
    }
    protected virtual void ArrangeChildren(Rect content)
    {
        foreach (var child in children.Where(c => c.Visible))
        {
            if (child.Layout != null) child.MeasureForLayout(new(content.Width, content.Height));
            var rect = child.Transform?.Resolve(content) ?? child.Layout?.Resolve(content, child.DesiredSize) ?? new Rect(content.X, content.Y, float.IsNaN(child.Width) ? content.Width : child.Width, float.IsNaN(child.Height) ? content.Height : child.Height);
            if (child.Layout == null) child.Measure(new(rect.Width, rect.Height));
            child.Arrange(rect);
        }
    }
    internal void PaintTree(DrawList list, Vector2 offset, float inherited, Theme theme)
    {
        if (!Visible) return;
        offset += VisualOffset; float opacity = inherited * InheritedOpacity * (Enabled ? 1 : 0.45f);
        var bounds = Bounds.Translate(offset);
        if (ClipToBounds) list.PushClip(bounds);
        if (CompositedOpacity < 1) list.BeginLayer(CompositedOpacity);
        Paint(list, bounds, opacity, theme);
        foreach (var child in children) child.PaintTree(list, offset, opacity, theme);
        if (CompositedOpacity < 1) list.EndLayer();
        if (ClipToBounds) list.PopClip();
    }
    protected virtual void Paint(DrawList list, Rect bounds, float opacity, Theme theme)
    {
        if (Background.A > 0) list.Rectangle(bounds, Background.Opacity(opacity), Radius);
    }
    internal Element? HitTest(Vector2 position, Rect clipRect, Vector2 offset)
    {
        if (!Visible || !Enabled) return null;
        offset += VisualOffset; var bounds = Bounds.Translate(offset);
        if (ClipToBounds) clipRect = clipRect.Intersect(bounds);
        if (!clipRect.Contains(position)) return null;
        for (int i = children.Count - 1; i >= 0; i--) { var hit = children[i].HitTest(position, clipRect, offset); if (hit != null) return hit; }
        return HitTestVisible && bounds.Contains(position) ? this : null;
    }
    // This target's termination cleanup precedes its observers: interception or
    // a failing observer must not retain its old gesture. Nested cancellations
    // involving multiple targets do not provide a transaction.
    internal void Route(UiEvent e)
    {
        if (e.Phase == EventPhase.Target && e.Kind is EventKind.PointerCancel or EventKind.Blur)
        {
            Exception? failure = null;
            try { OnEvent(e); } catch (Exception error) { failure = error; }
            try { Event?.Invoke(e); } catch (Exception error) { failure ??= error; }
            if (failure != null) ExceptionDispatchInfo.Capture(failure).Throw();
            return;
        }
        // Keep the frozen observer path, but never start or finish a cancelled target's gesture.
        Event?.Invoke(e);
        if (!e.Handled && !e.PointerCancelled && e.Phase == EventPhase.Target) OnEvent(e);
    }
    protected virtual void OnEvent(UiEvent e) { }
    public virtual string SemanticValue => "";
}

public sealed class StackPanel : Element
{
    private float gap;
    public bool Horizontal { get; init; }
    public float Gap { get => gap; set => Set(ref gap, NonNegative(value), DirtyFlags.Layout); }
    public override Vector2 Measure(Vector2 available)
    {
        float main = 0, cross = 0; int count = 0;
        foreach (var child in Children.Where(c => c.Visible))
        {
            if (child.Transform != null) throw new InvalidOperationException("StackPanel owns child position and size; remove the child's RectTransform.");
            child.MeasureForLayout(Vector2.Max(Vector2.Zero, available - new Vector2(Padding * 2)));
            var size = child.DesiredSizeWithMargin;
            main += Horizontal ? size.X : size.Y; cross = Math.Max(cross, Horizontal ? size.Y : size.X); count++;
        }
        main += Math.Max(0, count - 1) * Gap;
        var desired = (Horizontal ? new Vector2(main, cross) : new Vector2(cross, main)) + new Vector2(Padding * 2);
        DesiredSize = new(float.IsNaN(Width) ? desired.X : Width, float.IsNaN(Height) ? desired.Y : Height); return DesiredSize;
    }
    protected override void ArrangeChildren(Rect content)
    {
        var items = Children.Where(c => c.Visible).ToArray(); double total = 0, grow = 0;
        foreach (var child in items)
        {
            if (child.Transform != null) throw new InvalidOperationException("StackPanel owns child position and size; remove the child's RectTransform.");
            // A containing layout may assign a narrower extent than Measure received.
            // Resolve percentages and wrapping against that extent before allocating slots.
            if (child.Layout != null) child.MeasureForLayout(new(content.Width, content.Height));
            total += Horizontal ? child.DesiredSizeWithMargin.X : child.DesiredSizeWithMargin.Y; grow += child.Grow;
        }
        double free = Math.Max(0, (Horizontal ? content.Width : content.Height) - total - Math.Max(0, items.Length - 1) * (double)Gap); float position = 0;
        foreach (var child in items)
        {
            if (child.Layout != null)
            {
                float allocated = (float)((Horizontal ? child.DesiredSizeWithMargin.X : child.DesiredSizeWithMargin.Y) + (grow > 0 ? free * child.Grow / grow : 0));
                var slot = Horizontal ? new Rect(content.X + position, content.Y, allocated, content.Height) : new Rect(content.X, content.Y + position, content.Width, allocated);
                var box = child.Layout;
                if (Horizontal && box.Width.Unit == SizeUnit.Percent) box = box with { Width = UiLength.Fixed(child.DesiredSize.X) };
                if (!Horizontal && box.Height.Unit == SizeUnit.Percent) box = box with { Height = UiLength.Fixed(child.DesiredSize.Y) };
                child.Arrange(box.Resolve(slot, child.DesiredSize)); position += allocated + Gap; continue;
            }
            float main = (float)((Horizontal ? child.DesiredSize.X : child.DesiredSize.Y) + (grow > 0 ? free * child.Grow / grow : 0));
            var size = Horizontal ? new Vector2(main, float.IsNaN(child.Height) ? content.Height : child.Height) : new Vector2(float.IsNaN(child.Width) ? content.Width : child.Width, main);
            // Text is remeasured after actual width is known, then height is assigned.
            var measured = child.Measure(size);
            if (!Horizontal && float.IsNaN(child.Height) && child.Grow == 0) size.Y = measured.Y;
            child.Arrange(new(content.X + (Horizontal ? position : 0), content.Y + (Horizontal ? 0 : position), size.X, size.Y));
            position += (Horizontal ? size.X : size.Y) + Gap;
        }
    }
}
