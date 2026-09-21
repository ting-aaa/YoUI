using System.Numerics;

namespace YoUI;

/// <summary>Reusable vertical viewport. Content keeps logical layout; scrolling is a paint transform.</summary>
public class ScrollView : Element
{
    public Element Content { get; }
    private float scroll, extent;
    public float ScrollOffset => scroll;
    public ScrollView(Element content)
    {
        Content = Add(content); HitTestVisible = ClipToBounds = true;
        Event += e => { if (e.Kind == EventKind.Scroll && e.Phase != EventPhase.Capture && !e.Handled) { ScrollTo(scroll - e.Delta * 48); e.Handled = true; } };
    }
    public void ScrollTo(float value)
    {
        scroll = Math.Clamp(float.IsFinite(value) ? value : 0, 0, Math.Max(0, extent - Bounds.Height));
        Content.VisualOffset = new(0, -scroll); Invalidate();
    }
    public void ScrollIntoView(Element element)
    {
        float top = element.Bounds.Y - Content.Bounds.Y, bottom = top + element.Bounds.Height;
        if (top < scroll) ScrollTo(top); else if (bottom > scroll + Bounds.Height) ScrollTo(bottom - Bounds.Height);
    }
    public override Vector2 Measure(Vector2 available) { DesiredSize = available; return available; }
    protected override void ArrangeChildren(Rect content)
    {
        var size = Content.Measure(new(Math.Max(1, content.Width - 10), 100000));
        extent = size.Y; Content.Arrange(new(content.X, content.Y, Math.Max(0, content.Width - 10), Math.Max(content.Height, extent))); ScrollTo(scroll);
    }
    protected override void Paint(DrawList list, Rect b, float opacity, Theme theme)
    {
        base.Paint(list, b, opacity, theme);
        if (extent <= b.Height) return;
        float h = Math.Max(24, b.Height * b.Height / extent);
        list.Rectangle(new(b.Right - 5, b.Y + (b.Height - h) * scroll / (extent - b.Height), 3, h), theme.Border.Opacity(opacity), 2);
    }
}
public sealed record TreeItem(string Id, string Label, string Detail, bool Container, IReadOnlyList<TreeItem> Children);

/// <summary>Virtualized tree with selection, expansion, keyboard navigation and container drops.</summary>
public sealed class TreeView : Element
{
    private IReadOnlyList<TreeItem> items = [];
    private readonly HashSet<string> collapsed = [];
    private readonly List<(TreeItem Item, int Depth)> rows = [];
    private string? selected, dragging, drop;
    private Vector2 down;
    private float scroll;
    public float RowHeight { get; init; } = 32;
    public bool AllowReparent { get; set; }
    public string? SelectedId { get => selected; set { selected = value; Invalidate(); } }
    public event Action<string>? SelectionChanged;
    public event Action<string, string>? ReparentRequested;
    public TreeView() { Focusable = HitTestVisible = ClipToBounds = true; Role = "tree"; }
    public void SetItems(IReadOnlyList<TreeItem> value) { items = value; Rebuild(); }
    private void Rebuild()
    {
        rows.Clear(); void Visit(TreeItem item, int depth) { rows.Add((item, depth)); if (!collapsed.Contains(item.Id)) foreach (var child in item.Children) Visit(child, depth + 1); }
        foreach (var item in items) Visit(item, 0); ClampScroll(); Invalidate();
    }
    private void ClampScroll() => scroll = Math.Clamp(scroll, 0, Math.Max(0, rows.Count * RowHeight - Bounds.Height));
    private int RowAt(Vector2 point) => Bounds.Contains(point) ? (int)((point.Y - Bounds.Y + scroll) / RowHeight) : -1;
    private void Select(int index) { if (index < 0 || index >= rows.Count) return; selected = rows[index].Item.Id; SelectionChanged?.Invoke(selected); Invalidate(); }
    protected override void OnEvent(UiEvent e)
    {
        int row = RowAt(e.Position);
        if (e.Kind == EventKind.Scroll) { scroll -= e.Delta * RowHeight * 3; ClampScroll(); e.Handled = true; Invalidate(); }
        if (e.Kind == EventKind.PointerDown && row >= 0 && row < rows.Count)
        {
            var (item, depth) = rows[row];
            if (e.Position.X < Bounds.X + 26 + depth * 16 && item.Children.Count > 0) { if (!collapsed.Add(item.Id)) collapsed.Remove(item.Id); Rebuild(); }
            else { Select(row); down = e.Position; dragging = item.Id; e.CapturePointer = true; }
        }
        if (e.Kind == EventKind.PointerMove && dragging != null && AllowReparent && Vector2.Distance(down, e.Position) > 6)
        {
            drop = row >= 0 && row < rows.Count && rows[row].Item.Container && rows[row].Item.Id != dragging ? rows[row].Item.Id : null; Invalidate();
        }
        if (e.Kind == EventKind.PointerUp) { var source = dragging; var target = drop; dragging = drop = null; if (source != null && target != null) ReparentRequested?.Invoke(source, target); Invalidate(); }
        if (e.Kind is EventKind.PointerCancel or EventKind.Blur) { dragging = drop = null; Invalidate(); }
        if (e.Kind == EventKind.KeyDown)
        {
            int index = rows.FindIndex(r => r.Item.Id == selected);
            if (e.Key is 38 or 40)
            {
                index = Math.Clamp(index + (e.Key == 38 ? -1 : 1), 0, Math.Max(0, rows.Count - 1)); Select(index);
                if (index * RowHeight < scroll) scroll = index * RowHeight; else if ((index + 1) * RowHeight > scroll + Bounds.Height) scroll = (index + 1) * RowHeight - Bounds.Height;
                ClampScroll(); e.Handled = true;
            }
            if (index >= 0 && e.Key is 37 or 39) { if (e.Key == 37) collapsed.Add(rows[index].Item.Id); else collapsed.Remove(rows[index].Item.Id); Rebuild(); e.Handled = true; }
        }
    }
    protected override void Paint(DrawList list, Rect b, float opacity, Theme theme)
    {
        base.Paint(list, b, opacity, theme); ClampScroll();
        int first = (int)(scroll / RowHeight), end = Math.Min(rows.Count, first + (int)(b.Height / RowHeight) + 2);
        for (int i = first; i < end; i++)
        {
            var (item, depth) = rows[i]; float y = b.Y + i * RowHeight - scroll, x = b.X + 12 + depth * 16;
            if (item.Id == selected || item.Id == drop) list.Rectangle(new(b.X + 4, y + 1, b.Width - 8, RowHeight - 2), theme.Accent.Opacity(item.Id == drop ? .32f : .14f), 5);
            list.Text(new(x, y + 7, 14, 20), item.Children.Count == 0 ? "·" : collapsed.Contains(item.Id) ? "▸" : "▾", theme.Muted.Opacity(opacity), 12);
            list.Text(new(x + 18, y + 7, Math.Max(1, b.Right - x - 26), 20), item.Label, (item.Id == selected ? theme.Accent : theme.Text).Opacity(opacity), 13);
        }
    }
}
