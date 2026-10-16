using System.Collections.ObjectModel;
using System.Numerics;
using System.Runtime.ExceptionServices;

namespace YoUI;

/// <summary>A replaceable content slot. Content uses the normal retained-tree lifecycle.</summary>
public class ContentControl : Element
{
    private Element? content;
    private bool replacingContent;
    public Element? Content
    {
        get => content;
        set
        {
            if (ReferenceEquals(content, value)) return;
            if (replacingContent) throw new InvalidOperationException("Content changes cannot be nested inside content lifecycle callbacks.");
            if (value != null && (value.Parent != null || value.Document != null || ReferenceEquals(value, this))) throw new InvalidOperationException("Detach content before assigning it.");
            var previous = content; content = null;
            replacingContent = true;
            try
            {
                if (previous != null) Remove(previous);
                if (value != null)
                {
                    try { Add(value); }
                    finally { if (value.Parent == this) content = value; }
                }
            }
            finally { replacingContent = false; }
        }
    }
}

public sealed class ContentPresenter : ContentControl { }

/// <summary>Keyed, non-virtualized item composition. Reordering retains views and keyboard focus.
/// Template and binding callbacks run before structural edits; callback-owned state is not rolled back.</summary>
public class ItemsControl<T> : Element
{
    private readonly Dictionary<object, Element> views = [];
    private readonly IReadOnlyDictionary<object, Element> readOnlyViews;
    private bool changing;
    private float gap;
    public ItemsControl() => readOnlyViews = new ReadOnlyDictionary<object, Element>(views);
    public Func<T, object> Key { get; init; } = item => item!;
    public Func<T, Element> ItemTemplate { get; init; } = _ => new Label();
    public Action<Element, T>? BindItem { get; init; }
    public Action<Element>? ReleaseItem { get; init; }
    public IReadOnlyDictionary<object, Element> ItemViews => readOnlyViews;
    public bool Horizontal { get; init; }
    public float Gap { get => gap; set => Set(ref gap, NonNegative(value), DirtyFlags.Layout); }
    public void SetItems(IEnumerable<T> source)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (changing) throw new InvalidOperationException("Item updates cannot be nested inside item callbacks.");
        changing = true;
        try
        {
            var next = source.Select(item => (Key: Key(item), Item: item)).ToArray();
            if (next.Any(p => p.Key == null) || next.Select(p => p.Key).Distinct().Count() != next.Length) throw new ArgumentException("Item keys must be non-null and unique.", nameof(source));
            var candidates = new Dictionary<object, Element>();
            var created = new List<Element>();
            try
            {
                foreach (var (key, item) in next)
                {
                    var view = views.TryGetValue(key, out var retained) ? retained : ItemTemplate(item) ?? throw new InvalidOperationException("Item template returned null.");
                    if (retained == null && (view.Parent != null || view.Document != null || ReferenceEquals(view, this))) throw new InvalidOperationException("Item template must return detached views.");
                    if (candidates.Values.Contains(view)) throw new InvalidOperationException("Each item needs a distinct view.");
                    if (retained == null) created.Add(view);
                    candidates.Add(key, view); BindItem?.Invoke(view, item);
                    if (view.Parent != (retained == null ? null : this)) throw new InvalidOperationException("Item callbacks must not change item ownership.");
                }
            }
            catch { foreach (var view in created) try { ReleaseItem?.Invoke(view); } catch { } throw; }
            // Commit removals even when an application cleanup callback fails.
            Exception? failure = null;
            foreach (var key in views.Keys.Where(k => !candidates.ContainsKey(k)).ToArray())
            {
                var view = views[key]; views.Remove(key);
                try { Remove(view); } catch (Exception error) { failure ??= error; }
                try { ReleaseItem?.Invoke(view); } catch (Exception error) { failure ??= error; }
            }
            foreach (var (key, view) in candidates)
            {
                if (views.ContainsKey(key)) continue;
                try { Add(view); }
                catch (Exception error) { failure ??= error; }
                finally { if (view.Parent == this) views[key] = view; }
            }
            ReorderChildren(next.Where(p => views.ContainsKey(p.Key)).Select(p => views[p.Key]));
            if (failure != null) ExceptionDispatchInfo.Capture(failure).Throw();
        }
        finally { changing = false; }
    }
    public override Vector2 Measure(Vector2 available)
    {
        float main = 0, cross = 0; int count = 0;
        foreach (var child in Children.Where(c => c.Visible))
        {
            if (child.Transform != null) throw new InvalidOperationException("ItemsControl owns item layout.");
            child.MeasureForLayout(Vector2.Max(Vector2.Zero, available - new Vector2(Padding * 2))); var measured = child.DesiredSizeWithMargin;
            main += Horizontal ? measured.X : measured.Y; cross = Math.Max(cross, Horizontal ? measured.Y : measured.X); count++;
        }
        main += Math.Max(0, count - 1) * Gap;
        var desired = (Horizontal ? new Vector2(main, cross) : new Vector2(cross, main)) + new Vector2(Padding * 2);
        DesiredSize = new(float.IsNaN(Width) ? desired.X : Width, float.IsNaN(Height) ? desired.Y : Height); return DesiredSize;
    }
    protected override void ArrangeChildren(Rect b)
    {
        float position = 0;
        foreach (var child in Children.Where(c => c.Visible))
        {
            child.MeasureForLayout(new(b.Width, b.Height)); var measured = child.DesiredSizeWithMargin;
            var slot = Horizontal ? new Rect(b.X + position, b.Y, measured.X, b.Height) : new Rect(b.X, b.Y + position, b.Width, measured.Y);
            var box = child.Layout;
            if (box != null && Horizontal && box.Width.Unit == SizeUnit.Percent) box = box with { Width = UiLength.Fixed(child.DesiredSize.X) };
            if (box != null && !Horizontal && box.Height.Unit == SizeUnit.Percent) box = box with { Height = UiLength.Fixed(child.DesiredSize.Y) };
            var rect = box?.Resolve(slot, child.DesiredSize) ?? (Horizontal ? new Rect(slot.X, slot.Y, measured.X, float.IsNaN(child.Height) ? b.Height : child.Height) : new Rect(slot.X, slot.Y, float.IsNaN(child.Width) ? b.Width : child.Width, measured.Y));
            child.Arrange(rect); position += (Horizontal ? slot.Width : slot.Height) + Gap;
        }
    }
}

public sealed class Separator : Element
{
    private bool vertical;
    public bool Vertical { get => vertical; set { if (vertical == value) return; vertical = value; Width = value ? 1 : float.NaN; Height = value ? float.NaN : 1; } }
    public Separator() { Height = 1; Role = "separator"; }
    protected override void Paint(DrawList list, Rect bounds, float opacity, Theme theme) => list.Rectangle(bounds, (Background.A > 0 ? Background : theme.Border).Opacity(opacity));
}
