using System.Globalization;
using System.Numerics;

namespace YoUI;

/// <summary>Which adjacent shaped cell owns a caret at a wrap or bidi boundary.</summary>
public enum TextAffinity { Upstream, Downstream }
/// <summary>A UTF-16 offset. Affinity selects the preceding or following logical cell.</summary>
public readonly record struct TextPosition(int Index, TextAffinity Affinity = TextAffinity.Downstream);
/// <summary>A grapheme advance rectangle from a shaped line, not its raster ink bounds.</summary>
public readonly record struct TextCell(int Start, int End, int Line, Rect Bounds, bool RightToLeft = false);
/// <summary>Optional precise layout capability. Measurement alone cannot determine caret positions.</summary>
public interface ITextLayoutProvider : ITextMeasurer
{
    TextLayout Layout(string text, float size, float width, bool bold = false, bool wrap = true);
}

/// <summary>Immutable, renderer-independent snapshot. All indices are UTF-16; coordinates are text-local.</summary>
public sealed class TextLayout
{
    private readonly TextCell[] cells;
    private readonly int[] boundaries;
    public string Text { get; }
    public IReadOnlyList<TextCell> Cells { get; }
    /// <summary>Authoritative provider-defined editing stops, as sorted UTF-16 offsets.</summary>
    public IReadOnlyList<int> CaretStops { get; }
    public TextLayout(string text, IEnumerable<TextCell> cells)
    {
        ArgumentNullException.ThrowIfNull(text); ArgumentNullException.ThrowIfNull(cells);
        TextBoundary.Validate(text);
        Text = text; this.cells = cells.ToArray();
        if (this.cells.Length == 0) throw new ArgumentException("Layout must include an empty-line cell for empty text.", nameof(cells));
        foreach (var cell in this.cells)
        {
            var b = cell.Bounds;
            if (cell.Start < 0 || cell.End < cell.Start || cell.End > text.Length || cell.Line < 0 ||
                !TextBoundary.IsScalarBoundary(text, cell.Start) || !TextBoundary.IsScalarBoundary(text, cell.End) ||
                !float.IsFinite(b.X) || !float.IsFinite(b.Y) || !float.IsFinite(b.Width) || !float.IsFinite(b.Height) ||
                b.Width < 0 || b.Height <= 0 || !float.IsFinite(b.Right) || !float.IsFinite(b.Bottom))
                throw new ArgumentException("Cells must have scalar-boundary indices and finite advance rectangles.", nameof(cells));
        }
        // The provider owns Unicode segmentation. Native and .NET Unicode tables
        // can differ (notably Indic conjuncts); never split a provider cell using StringInfo.
        boundaries = this.cells.SelectMany(c => new[] { c.Start, c.End }).Append(0).Append(text.Length).Distinct().Order().ToArray();
        Cells = Array.AsReadOnly(this.cells); CaretStops = Array.AsReadOnly(boundaries);
    }
    private static float StartX(TextCell cell) => cell.RightToLeft ? cell.Bounds.Right : cell.Bounds.X;
    private static float EndX(TextCell cell) => cell.RightToLeft ? cell.Bounds.X : cell.Bounds.Right;
    private static Rect Caret(TextCell cell, bool end) => new(end ? EndX(cell) : StartX(cell), cell.Bounds.Y, 1.5f, cell.Bounds.Height);
    /// <summary>Clamps outside the text; interior grapheme offsets snap in the requested affinity direction.</summary>
    public Rect GetCaret(TextPosition position)
    {
        if (!Enum.IsDefined(position.Affinity)) throw new ArgumentOutOfRangeException(nameof(position));
        bool upstream = position.Affinity == TextAffinity.Upstream;
        int index = TextBoundary.Snap(boundaries, position.Index, !upstream);
        // Prefer exact logical ownership. This matters at wraps and mixed-direction boundaries.
        foreach (var cell in cells)
            if (upstream ? cell.End == index : cell.Start == index) return Caret(cell, upstream);
        foreach (var cell in cells)
            if (upstream ? cell.Start == index : cell.End == index) return Caret(cell, !upstream);
        // A newline or an unrepresented format character has no advance cell of its own.
        var candidate = cells[0]; bool atEnd = false; long distance = long.MaxValue;
        foreach (var cell in cells)
        {
            long startDistance = Math.Abs((long)cell.Start - index), endDistance = Math.Abs((long)cell.End - index);
            if (startDistance < distance || (startDistance == distance && !upstream)) { candidate = cell; atEnd = false; distance = startDistance; }
            if (endDistance < distance || (endDistance == distance && upstream)) { candidate = cell; atEnd = true; distance = endDistance; }
        }
        return Caret(candidate, atEnd);
    }
    /// <summary>Nearest visual line and caret edge; outside coordinates clamp, nonfinite coordinates are rejected.</summary>
    public TextPosition HitTest(Vector2 point)
    {
        if (!float.IsFinite(point.X) || !float.IsFinite(point.Y)) throw new ArgumentOutOfRangeException(nameof(point));
        int line = cells[0].Line; double dy = double.PositiveInfinity;
        foreach (var cell in cells)
        {
            double distance = Math.Max((double)cell.Bounds.Y - point.Y, Math.Max(0, (double)point.Y - cell.Bounds.Bottom));
            if (distance < dy) { dy = distance; line = cell.Line; }
        }
        TextPosition result = new(cells[0].Start); double dx = double.PositiveInfinity;
        foreach (var cell in cells)
        {
            if (cell.Line != line) continue;
            double start = Math.Abs((double)point.X - StartX(cell)), end = Math.Abs((double)point.X - EndX(cell));
            if (start < dx) { dx = start; result = new(cell.Start, TextAffinity.Downstream); }
            // Ties choose the logical trailing edge (also deterministic for zero-width cells).
            if (end <= dx) { dx = end; result = new(cell.End, TextAffinity.Upstream); }
        }
        return result;
    }
    /// <summary>Advance rectangles for a logical range; expands partial graphemes. Newline-only ranges have no ink box.</summary>
    public IReadOnlyList<Rect> GetSelection(int start, int end)
    {
        if (start == end) return Array.Empty<Rect>();
        int lo = TextBoundary.Snap(boundaries, Math.Min(start, end), false), hi = TextBoundary.Snap(boundaries, Math.Max(start, end), true);
        var result = new List<Rect>();
        foreach (var cell in cells.Where(c => c.End > lo && c.Start < hi && c.Start != c.End && c.Bounds.Width > 0).OrderBy(c => c.Line).ThenBy(c => c.Bounds.X))
        {
            var b = cell.Bounds;
            if (result.Count > 0 && result[^1].Y == b.Y && result[^1].Height == b.Height && b.X <= result[^1].Right + .01f)
            {
                var previous = result[^1]; result[^1] = new(previous.X, previous.Y, Math.Max(previous.Right, b.Right) - previous.X, previous.Height);
            }
            else result.Add(b);
        }
        return result.AsReadOnly();
    }
}

internal static class TextBoundary
{
    internal static void Validate(string text)
    {
        for (int i = 0; i < text.Length; i++)
        {
            if (char.IsHighSurrogate(text[i]))
            {
                if (i + 1 >= text.Length || !char.IsLowSurrogate(text[++i])) throw new ArgumentException("Text contains an unpaired UTF-16 surrogate.", nameof(text));
            }
            else if (char.IsLowSurrogate(text[i])) throw new ArgumentException("Text contains an unpaired UTF-16 surrogate.", nameof(text));
        }
    }
    internal static bool IsScalarBoundary(string text, int index) =>
        index == 0 || index == text.Length || !(char.IsHighSurrogate(text[index - 1]) && char.IsLowSurrogate(text[index]));
    internal static int[] Stops(string text) => [.. StringInfo.ParseCombiningCharacters(text), text.Length];
    internal static int Snap(int[] stops, int index, bool forward)
    {
        index = Math.Clamp(index, 0, stops[^1]);
        int found = Array.BinarySearch(stops, index);
        return found >= 0 ? stops[found] : stops[forward ? ~found : ~found - 1];
    }
    internal static string SingleLine(string? value)
    {
        value ??= ""; Validate(value);
        return new string(value.Where(c => !char.IsControl(c) && c is not '\u2028' and not '\u2029').ToArray());
    }
}
