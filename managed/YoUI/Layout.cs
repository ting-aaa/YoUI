using System.Numerics;
using System.Runtime.CompilerServices;

namespace YoUI;

public enum SizeUnit { Auto, Fixed, Percent }
public readonly record struct UiLength(SizeUnit Unit, float Value = 0)
{
    public static UiLength Auto => new(SizeUnit.Auto);
    public static UiLength Fixed(float value) => new(SizeUnit.Fixed, value);
    public static UiLength Percent(float fraction) => new(SizeUnit.Percent, fraction);
    public float Resolve(float available, float desired)
    {
        if (!Enum.IsDefined(Unit) || !float.IsFinite(Value) || Value < 0 || Value > LayoutMath.Limit || !float.IsFinite(available) || available < 0 || !float.IsFinite(desired) || desired < 0)
            throw new ArgumentOutOfRangeException(nameof(available), "Lengths require finite nonnegative values and a valid unit.");
        return LayoutMath.Size(Unit switch { SizeUnit.Fixed => Value, SizeUnit.Percent => (double)available * Value, _ => desired });
    }
}
public enum Alignment { Stretch, Start, Center, End }
public readonly record struct Thickness(float Left, float Top, float Right, float Bottom)
{
    public Thickness(float uniform) : this(uniform, uniform, uniform, uniform) { }
    public float Horizontal => Left + Right;
    public float Vertical => Top + Bottom;
}

/// <summary>Immutable box constraints. Percent uses the parent's inner extent after margins.
/// Auto dimensions measure content and stretch only when the corresponding alignment requests it.</summary>
public sealed record BoxLayout
{
    public UiLength Width { get; init; } = UiLength.Auto;
    public UiLength Height { get; init; } = UiLength.Auto;
    public float MinWidth { get; init; }
    public float MinHeight { get; init; }
    public float MaxWidth { get; init; } = LayoutMath.Limit;
    public float MaxHeight { get; init; } = LayoutMath.Limit;
    public Thickness Margin { get; init; }
    public Alignment Horizontal { get; init; } = Alignment.Stretch;
    public Alignment Vertical { get; init; } = Alignment.Start;
    public void Validate()
    {
        static bool Valid(float n) => float.IsFinite(n) && n >= 0 && n <= LayoutMath.Limit;
        if (!Enum.IsDefined(Width.Unit) || !Enum.IsDefined(Height.Unit) || !Enum.IsDefined(Horizontal) || !Enum.IsDefined(Vertical)
            || !Valid(Width.Value) || !Valid(Height.Value)
            || !Valid(MinWidth) || !Valid(MinHeight) || !Valid(MaxWidth) || !Valid(MaxHeight)
            || !Valid(Margin.Left) || !Valid(Margin.Top) || !Valid(Margin.Right) || !Valid(Margin.Bottom)
            || MinWidth > MaxWidth || MinHeight > MaxHeight)
            throw new InvalidDataException("Box dimensions, margins and constraints must be finite, nonnegative, ordered and no greater than 32768.");
    }
    internal Vector2 MeasureConstraint(Vector2 available)
    {
        Validate(); LayoutMath.ValidateAvailable(available);
        return new(Math.Clamp(Width.Resolve(Math.Max(0, available.X - Margin.Horizontal), Math.Max(0, available.X - Margin.Horizontal)), MinWidth, MaxWidth),
            Math.Clamp(Height.Resolve(Math.Max(0, available.Y - Margin.Vertical), Math.Max(0, available.Y - Margin.Vertical)), MinHeight, MaxHeight));
    }
    public Vector2 Constrain(Vector2 available, Vector2 desired)
    {
        Validate(); LayoutMath.ValidateAvailable(available); LayoutMath.ValidateAvailable(desired);
        return new(Math.Clamp(Width.Resolve(Math.Max(0, available.X - Margin.Horizontal), desired.X), MinWidth, MaxWidth),
            Math.Clamp(Height.Resolve(Math.Max(0, available.Y - Margin.Vertical), desired.Y), MinHeight, MaxHeight));
    }
    public Rect Resolve(Rect parent, Vector2 desired)
    {
        LayoutMath.ValidateBounds(parent);
        float aw = LayoutMath.Size((double)parent.Width - Margin.Horizontal), ah = LayoutMath.Size((double)parent.Height - Margin.Vertical);
        var size = Constrain(new(parent.Width, parent.Height), desired);
        if (Width.Unit == SizeUnit.Auto && Horizontal == Alignment.Stretch) size.X = Math.Clamp(LayoutMath.Size(aw), MinWidth, MaxWidth);
        if (Height.Unit == SizeUnit.Auto && Vertical == Alignment.Stretch) size.Y = Math.Clamp(LayoutMath.Size(ah), MinHeight, MaxHeight);
        static float Offset(Alignment a, float remainder) => a == Alignment.Center ? remainder / 2 : a == Alignment.End ? remainder : 0;
        return new(parent.X + Margin.Left + Offset(Horizontal, aw - size.X), parent.Y + Margin.Top + Offset(Vertical, ah - size.Y), size.X, size.Y);
    }
}

internal static class LayoutMath
{
    internal const float Limit = 32768;
    internal static float Size(double value) => (float)Math.Clamp(value, 0, Limit);
    internal static void ValidateAvailable(Vector2 value)
    {
        if (!float.IsFinite(value.X) || !float.IsFinite(value.Y) || value.X < 0 || value.Y < 0) throw new ArgumentOutOfRangeException(nameof(value), "Layout requires finite nonnegative extents.");
    }
    internal static void ValidateBounds(Rect value)
    {
        ValidateAvailable(new(value.Width, value.Height));
        if (!float.IsFinite(value.X) || !float.IsFinite(value.Y)) throw new ArgumentOutOfRangeException(nameof(value), "Layout requires finite coordinates.");
    }
    internal static Vector2 Inner(Element owner, Vector2 available)
    {
        ValidateAvailable(available);
        return new(Size((float.IsNaN(owner.Width) ? available.X : owner.Width) - (double)owner.Padding * 2), Size((float.IsNaN(owner.Height) ? available.Y : owner.Height) - (double)owner.Padding * 2));
    }
}

public enum GridUnit { Fixed, Auto, Star }
public readonly record struct GridTrack(GridUnit Unit, float Value = 1)
{
    public static GridTrack Auto => new(GridUnit.Auto);
    public static GridTrack Star(float weight = 1) => new(GridUnit.Star, weight);
    public static GridTrack Fixed(float value) => new(GridUnit.Fixed, value);
}

/// <summary>Fixed tracks keep their size; Auto tracks reserve content demand, including spans;
/// Star tracks divide the remaining finite extent by weight. Overflow never creates negative tracks.</summary>
public sealed class Grid : Element
{
    private sealed record Cell(int Row, int Column, int RowSpan, int ColumnSpan);
    private readonly ConditionalWeakTable<Element, Cell> cells = new();
    private IReadOnlyList<GridTrack> rows = Array.AsReadOnly(new[] { GridTrack.Star() }), columns = Array.AsReadOnly(new[] { GridTrack.Star() });
    private float gap;
    public IReadOnlyList<GridTrack> Rows { get => rows; set { var next = ValidateTracks(value); ValidateCells(next.Count, columns.Count); rows = next; Invalidate(DirtyFlags.Layout); } }
    public IReadOnlyList<GridTrack> Columns { get => columns; set { var next = ValidateTracks(value); ValidateCells(rows.Count, next.Count); columns = next; Invalidate(DirtyFlags.Layout); } }
    public float Gap { get => gap; set { if (value > LayoutMath.Limit) throw new ArgumentOutOfRangeException(nameof(value)); Set(ref gap, NonNegative(value), DirtyFlags.Layout); } }
    private static IReadOnlyList<GridTrack> ValidateTracks(IReadOnlyList<GridTrack> tracks)
    {
        ArgumentNullException.ThrowIfNull(tracks);
        var copy = tracks.ToArray();
        if (copy.Length is < 1 or > 256 || copy.Any(t => !Enum.IsDefined(t.Unit) || !float.IsFinite(t.Value) || t.Value < 0 || t.Value > LayoutMath.Limit || (t.Unit == GridUnit.Star && t.Value == 0)))
            throw new ArgumentException("A grid requires 1 to 256 finite nonnegative tracks; Star weights must be positive.", nameof(tracks));
        return Array.AsReadOnly(copy);
    }
    private Cell GetCell(Element child) => cells.TryGetValue(child, out var cell) ? cell : new(0, 0, 1, 1);
    private void ValidateCells(int rowCount, int columnCount)
    {
        if (Children.Any(child => { var c = GetCell(child); return c.Row + c.RowSpan > rowCount || c.Column + c.ColumnSpan > columnCount; }))
            throw new ArgumentException("Track changes cannot remove an occupied cell or span.");
    }
    public void SetCell(Element child, int row, int column, int rowSpan = 1, int columnSpan = 1)
    {
        ArgumentNullException.ThrowIfNull(child);
        if (child.Parent != this) throw new ArgumentException("The cell must belong to this grid.", nameof(child));
        if (row < 0 || column < 0 || rowSpan < 1 || columnSpan < 1 || (long)row + rowSpan > Rows.Count || (long)column + columnSpan > Columns.Count)
            throw new ArgumentOutOfRangeException(nameof(row), "Cell and span must stay within the track collection.");
        cells.Remove(child); cells.Add(child, new(row, column, rowSpan, columnSpan)); Invalidate(DirtyFlags.Layout);
    }
    private Element[] Items()
    {
        var items = Children.Where(c => c.Visible).ToArray();
        if (items.Any(c => c.Transform != null)) throw new InvalidOperationException("Grid owns child placement; remove the child's RectTransform.");
        return items;
    }
    private float[] ResolveTracks(IReadOnlyList<GridTrack> tracks, float available, bool vertical, Element[] items)
    {
        var result = tracks.Select(t => t.Unit == GridUnit.Fixed ? t.Value : 0).ToArray();
        // Process short spans first, so a spanning demand does not double count a narrower demand.
        foreach (var child in items.OrderBy(c => vertical ? GetCell(c).RowSpan : GetCell(c).ColumnSpan))
        {
            var cell = GetCell(child); int start = vertical ? cell.Row : cell.Column, span = vertical ? cell.RowSpan : cell.ColumnSpan;
            var auto = Enumerable.Range(start, span).Where(i => tracks[i].Unit == GridUnit.Auto).ToArray();
            if (auto.Length == 0) continue;
            double occupied = (span - 1) * (double)Gap;
            for (int i = start; i < start + span; i++) occupied += result[i];
            double desired = vertical ? child.DesiredSizeWithMargin.Y : child.DesiredSizeWithMargin.X;
            float share = LayoutMath.Size(Math.Max(0, desired - occupied) / auto.Length);
            foreach (int index in auto) result[index] = LayoutMath.Size((double)result[index] + share);
        }
        double used = result.Sum(v => (double)v) + (tracks.Count - 1) * (double)Gap;
        double remaining = Math.Max(0, available - used), weight = tracks.Where(t => t.Unit == GridUnit.Star).Sum(t => (double)t.Value);
        for (int i = 0; i < result.Length; i++) if (tracks[i].Unit == GridUnit.Star) result[i] = LayoutMath.Size(remaining * tracks[i].Value / weight);
        return result;
    }
    private float SpanSize(float[] values, int start, int span) => (float)(values.Skip(start).Take(span).Sum(v => (double)v) + (span - 1) * (double)Gap);
    private float Offset(float[] values, int index) => (float)(values.Take(index).Sum(v => (double)v) + index * (double)Gap);
    private (float[] Widths, float[] Heights) Calculate(Vector2 available, Element[] items)
    {
        foreach (var child in items) child.MeasureForLayout(available);
        var widths = ResolveTracks(Columns, available.X, false, items);
        foreach (var child in items)
        {
            var c = GetCell(child);
            child.MeasureForLayout(new(SpanSize(widths, c.Column, c.ColumnSpan), available.Y));
        }
        return (widths, ResolveTracks(Rows, available.Y, true, items));
    }
    public override Vector2 Measure(Vector2 available)
    {
        var inner = LayoutMath.Inner(this, available); var (w, h) = Calculate(inner, Items());
        DesiredSize = new(float.IsNaN(Width) ? LayoutMath.Size(w.Sum(v => (double)v) + (w.Length - 1) * (double)Gap + Padding * 2d) : Width,
            float.IsNaN(Height) ? LayoutMath.Size(h.Sum(v => (double)v) + (h.Length - 1) * (double)Gap + Padding * 2d) : Height);
        return DesiredSize;
    }
    protected override void ArrangeChildren(Rect content)
    {
        LayoutMath.ValidateBounds(content);
        var items = Items(); var (w, h) = Calculate(new(content.Width, content.Height), items);
        foreach (var child in items)
        {
            var c = GetCell(child);
            var area = new Rect(content.X + Offset(w, c.Column), content.Y + Offset(h, c.Row), SpanSize(w, c.Column, c.ColumnSpan), SpanSize(h, c.Row, c.RowSpan));
            child.MeasureForLayout(new(area.Width, area.Height));
            child.Arrange(child.Layout?.Resolve(area, child.DesiredSize) ?? area);
        }
    }
}

/// <summary>Horizontal flow with content-sized lines. Hidden items occupy no space.</summary>
public sealed class WrapPanel : Element
{
    private float gap = 8;
    public float Gap { get => gap; set { if (value > LayoutMath.Limit) throw new ArgumentOutOfRangeException(nameof(value)); Set(ref gap, NonNegative(value), DirtyFlags.Layout); } }
    private Vector2 Flow(Rect area, bool arrange)
    {
        LayoutMath.ValidateBounds(area);
        double x = 0, y = 0, line = 0, width = 0; bool any = false;
        foreach (var child in Children.Where(c => c.Visible))
        {
            if (child.Transform != null) throw new InvalidOperationException("WrapPanel owns child placement; remove the child's RectTransform.");
            child.MeasureForLayout(new(area.Width, area.Height)); var outer = child.DesiredSizeWithMargin;
            if (any && x + outer.X > area.Width) { x = 0; y += line + Gap; line = 0; any = false; }
            if (arrange)
            {
                var slot = new Rect(area.X + (float)x, area.Y + (float)y, outer.X, outer.Y);
                // Percent was resolved against the panel; use that measured border size in its flow slot.
                var layout = child.Layout;
                if (layout != null) layout = layout with { Width = UiLength.Fixed(child.DesiredSize.X), Height = UiLength.Fixed(child.DesiredSize.Y) };
                child.Arrange(layout?.Resolve(slot, child.DesiredSize) ?? slot);
            }
            width = Math.Max(width, x + outer.X); x += outer.X + Gap; line = Math.Max(line, outer.Y); any = true;
        }
        return new(LayoutMath.Size(width), LayoutMath.Size(y + line));
    }
    public override Vector2 Measure(Vector2 available)
    {
        var inner = LayoutMath.Inner(this, available); var size = Flow(new(0, 0, inner.X, inner.Y), false);
        DesiredSize = new(float.IsNaN(Width) ? LayoutMath.Size((double)size.X + Padding * 2d) : Width, float.IsNaN(Height) ? LayoutMath.Size((double)size.Y + Padding * 2d) : Height); return DesiredSize;
    }
    protected override void ArrangeChildren(Rect content) => Flow(content, true);
}
