using System.Globalization;
using System.Numerics;

namespace YoUI;

public sealed class Label : Element
{
    private string text = "";
    private float fontSize = 14;
    private Color? color;
    private bool bold;
    public Label(string value = "") { text = value; Role = "text"; }
    public string Text { get => text; set { if (Set(ref text, value ?? "", DirtyFlags.Layout | DirtyFlags.Semantics)) AccessibleName = text; } }
    public float FontSize { get => fontSize; set => Set(ref fontSize, NonNegative(value), DirtyFlags.Layout); }
    public Color? Color { get => color; set => Set(ref color, value, DirtyFlags.Paint); }
    public bool Bold { get => bold; set => Set(ref bold, value, DirtyFlags.Layout); }
    public override string SemanticValue => Text;
    public override Vector2 Measure(Vector2 available)
    {
        var measured = Document?.TextMeasurer.Measure(Text, FontSize, Math.Max(1, float.IsNaN(Width) ? available.X : Width), Bold) ?? (0f, FontSize * 1.35f);
        DesiredSize = new(float.IsNaN(Width) ? measured.Item1 : Width, float.IsNaN(Height) ? measured.Item2 : Height); return DesiredSize;
    }
    protected override void Paint(DrawList list, Rect bounds, float opacity, Theme theme) => list.Text(bounds, Text, (Color ?? theme.Text).Opacity(opacity), FontSize, Bold);
}

/// <summary>Interaction is composed with a child Label. No native widget dependency.</summary>
public class Button : Element
{
    private bool pressed, selected, primary;
    private Color? textColor;
    public Color? TextColor { get => textColor; set => Set(ref textColor, value, DirtyFlags.Paint); }
    public Label Caption { get; }
    public event Action? Clicked;
    public Button(string text, bool focusable = true)
    {
        Height = 42; Radius = 9; HitTestVisible = true; Focusable = focusable; Role = "button"; AccessibleName = text;
        Caption = Add(new Label(text) { FontSize = 14 });
    }
    public string Text { get => Caption.Text; set { Caption.Text = value; AccessibleName = value; } }
    public bool Selected { get => selected; set => Set(ref selected, value, DirtyFlags.Paint | DirtyFlags.Semantics); }
    public bool Primary { get => primary; set => Set(ref primary, value, DirtyFlags.Paint); }
    public override Vector2 Measure(Vector2 available) { var s = Caption.Measure(available); DesiredSize = new(float.IsNaN(Width) ? s.X + 32 : Width, float.IsNaN(Height) ? s.Y + 20 : Height); return DesiredSize; }
    protected override void ArrangeChildren(Rect content)
    {
        var s = Caption.Measure(new(Math.Max(1, content.Width - 28), content.Height));
        Caption.Arrange(new(content.X + 14, content.Y + Math.Max(0, (content.Height - s.Y) / 2), Math.Max(0, content.Width - 28), s.Y));
    }
    protected override void Paint(DrawList list, Rect bounds, float opacity, Theme theme)
    {
        Color fill = Background.A > 0 ? Background : Primary ? theme.Accent : Selected ? theme.Accent.Opacity(0.16f) : theme.SurfaceRaised;
        if (pressed) fill = theme.Accent.Opacity(0.5f); else if (IsHovered) fill = Primary ? theme.Accent.Opacity(0.85f) : theme.Border;
        if (IsFocused) list.Rectangle(new(bounds.X - 2, bounds.Y - 2, bounds.Width + 4, bounds.Height + 4), theme.Accent.Opacity(opacity), Radius + 2);
        list.Rectangle(bounds, fill.Opacity(opacity), Radius);
        Caption.Color = TextColor ?? (Primary ? theme.OnAccent : Selected ? theme.Accent : theme.Text);
    }
    protected override void OnEvent(UiEvent e)
    {
        switch (e.Kind)
        {
            case EventKind.PointerDown: pressed = true; e.CapturePointer = true; Invalidate(); break;
            case EventKind.PointerUp:
                bool activate = pressed && VisualBounds.Contains(e.Position); pressed = false; Invalidate(); if (activate) Clicked?.Invoke(); break;
            case EventKind.PointerCancel: case EventKind.Blur: pressed = false; Invalidate(); break;
            case EventKind.KeyDown when e.Key is 13 or 32: e.Handled = true; Clicked?.Invoke(); break;
        }
    }
}

public sealed class Toggle : Button
{
    private bool value;
    private readonly string label;
    public Toggle(string label, bool initial = false) : base(label)
    {
        this.label = label; Role = "switch"; value = initial; UpdateCaption();
        Clicked += () => Value = !Value;
    }
    public event Action<bool>? Changed;
    public bool Value { get => value; set { if (!Set(ref this.value, value, DirtyFlags.Paint | DirtyFlags.Semantics)) return; UpdateCaption(); Changed?.Invoke(value); } }
    private void UpdateCaption() { Text = $"{label}   {(value ? "ON" : "OFF")}"; Selected = value; }
    public override string SemanticValue => Value ? "on" : "off";
}

public sealed class Slider : Element
{
    private float value = 0.5f;
    private bool dragging;
    public Slider() { Height = 28; HitTestVisible = Focusable = true; Role = "slider"; }
    public event Action<float>? Changed;
    public float Value { get => value; set { var v = Math.Clamp(value, 0, 1); if (Set(ref this.value, v, DirtyFlags.Paint | DirtyFlags.Semantics)) Changed?.Invoke(v); } }
    public override string SemanticValue => Value.ToString("P0", CultureInfo.InvariantCulture);
    private void SetPosition(float x) => Value = (x - VisualBounds.X - 8) / Math.Max(1, Bounds.Width - 16);
    protected override void OnEvent(UiEvent e)
    {
        if (e.Kind == EventKind.PointerDown) { dragging = true; e.CapturePointer = true; SetPosition(e.Position.X); }
        if (e.Kind == EventKind.PointerMove && dragging) SetPosition(e.Position.X);
        if (e.Kind is EventKind.PointerUp or EventKind.PointerCancel or EventKind.Blur) dragging = false;
        if (e.Kind == EventKind.KeyDown && e.Key is 37 or 39) { Value += e.Key == 37 ? -0.05f : 0.05f; e.Handled = true; }
    }
    protected override void Paint(DrawList list, Rect b, float opacity, Theme theme)
    {
        list.Rectangle(new(b.X + 8, b.Y + b.Height / 2 - 3, Math.Max(0, b.Width - 16), 6), theme.Border.Opacity(opacity), 3);
        list.Rectangle(new(b.X + 8, b.Y + b.Height / 2 - 3, Math.Max(0, b.Width - 16) * Value, 6), theme.Accent.Opacity(opacity), 3);
        list.Rectangle(new(b.X + Math.Max(0, b.Width - 16) * Value, b.Y + b.Height / 2 - 8, 16, 16), (IsFocused ? theme.Text : theme.Accent).Opacity(opacity), 8);
    }
}

/// <summary>Single-line grapheme editing, keyboard selection, clipboard, undo and IME.</summary>
public sealed class TextBox : Element
{
    private string text = "", composition = "";
    private int cursor, anchor;
    private readonly List<(string Text, int Cursor, int Anchor)> undo = [], redo = [];
    public TextBox() { Height = 44; HitTestVisible = Focusable = true; Role = "textbox"; ClipToBounds = true; }
    public string Placeholder { get; init; } = "Search…";
    public event Action<string>? Changed;
    public string Text { get => text; set { value ??= ""; if (Set(ref text, value, DirtyFlags.Paint | DirtyFlags.Semantics)) { cursor = anchor = text.Length; composition = ""; undo.Clear(); redo.Clear(); Changed?.Invoke(text); } } }
    public override string SemanticValue => Text;
    public string SelectedText => text[Math.Min(cursor, anchor)..Math.Max(cursor, anchor)];
    private int Previous() => StringInfo.ParseCombiningCharacters(text).LastOrDefault(i => i < cursor);
    private int Next() => StringInfo.ParseCombiningCharacters(text).FirstOrDefault(i => i > cursor, text.Length);
    private void Remember() { undo.Add((text, cursor, anchor)); if (undo.Count > 100) undo.RemoveAt(0); redo.Clear(); }
    private void ReplaceSelection(string inserted)
    {
        Remember(); int begin = Math.Min(cursor, anchor), end = Math.Max(cursor, anchor);
        text = text.Remove(begin, end - begin).Insert(begin, inserted); cursor = anchor = begin + inserted.Length; composition = ""; Invalidate(DirtyFlags.Paint | DirtyFlags.Semantics); Changed?.Invoke(text);
    }
    private void Restore(List<(string Text, int Cursor, int Anchor)> from, List<(string Text, int Cursor, int Anchor)> to)
    {
        if (from.Count == 0) return; to.Add((text, cursor, anchor)); (text, cursor, anchor) = from[^1]; from.RemoveAt(from.Count - 1); composition = ""; Invalidate(DirtyFlags.Paint | DirtyFlags.Semantics); Changed?.Invoke(text);
    }
    protected override void OnEvent(UiEvent e)
    {
        switch (e.Kind)
        {
            case EventKind.PointerDown: cursor = anchor = text.Length; Invalidate(); break;
            case EventKind.Blur: composition = ""; Invalidate(); break;
            case EventKind.Composition: composition = e.Text; Invalidate(); break;
            case EventKind.TextInput:
                if (e.Text.Length == 0 || e.Text.Any(char.IsControl)) break;
                ReplaceSelection(e.Text); break;
            case EventKind.KeyDown:
                if (composition.Length > 0) break;
                if (e.Control)
                {
                    switch (e.Key)
                    {
                        case 65: anchor = 0; cursor = text.Length; break;
                        case 67: if (cursor != anchor) Document?.Clipboard?.SetText(SelectedText); break;
                        case 88: if (cursor != anchor && Document?.Clipboard != null) { Document.Clipboard.SetText(SelectedText); ReplaceSelection(""); } break;
                        case 86: if (Document?.Clipboard != null) ReplaceSelection(new string(Document.Clipboard.GetText().Where(c => !char.IsControl(c)).ToArray())); break;
                        case 90: if (e.Shift) Restore(redo, undo); else Restore(undo, redo); break;
                        case 89: Restore(redo, undo); break;
                        default: return;
                    }
                    e.Handled = true; Invalidate(); break;
                }
                if (e.Key == 8 && (cursor > 0 || cursor != anchor)) { if (anchor == cursor) anchor = Previous(); ReplaceSelection(""); }
                else if (e.Key == 46 && (cursor < text.Length || cursor != anchor)) { if (anchor == cursor) anchor = Next(); ReplaceSelection(""); }
                else if (e.Key is 37 or 39 or 36 or 35)
                {
                    cursor = e.Key switch { 37 => !e.Shift && cursor != anchor ? Math.Min(cursor, anchor) : Previous(), 39 => !e.Shift && cursor != anchor ? Math.Max(cursor, anchor) : Next(), 36 => 0, _ => text.Length };
                    if (!e.Shift) anchor = cursor;
                }
                else break;
                e.Handled = true; Invalidate(); break;
        }
    }
    protected override void Paint(DrawList list, Rect b, float opacity, Theme theme)
    {
        list.Rectangle(b, (IsFocused ? theme.Accent : theme.Border).Opacity(opacity), 9);
        list.Rectangle(b.Inset(1), theme.Surface.Opacity(opacity), 8);
        string value = text.Insert(cursor, composition);
        float caretX = Document!.TextMeasurer.Measure(text[..cursor] + composition, 14, 100000).Width;
        float scroll = Math.Max(0, caretX - Math.Max(1, b.Width - 32));
        float textY = b.Y + Math.Max(2, (b.Height - 20) / 2);
        list.PushClip(new(b.X + 12, b.Y + 2, Math.Max(0, b.Width - 24), Math.Max(0, b.Height - 4)));
        if (cursor != anchor && IsFocused)
        {
            float x0 = Document.TextMeasurer.Measure(text[..Math.Min(cursor, anchor)], 14, 100000).Width;
            float x1 = Document.TextMeasurer.Measure(text[..Math.Max(cursor, anchor)], 14, 100000).Width;
            list.Rectangle(new(b.X + 14 + x0 - scroll, textY, x1 - x0, 20), theme.Accent.Opacity(0.28f * opacity));
        }
        list.Text(new(b.X + 14 - scroll, textY, Math.Max(b.Width, caretX + 200), 24), value.Length == 0 ? Placeholder : value, (value.Length == 0 ? theme.Muted : theme.Text).Opacity(opacity), 14);
        if (IsFocused) list.Rectangle(new(b.X + 14 + caretX - scroll, textY, 1.5f, 19), theme.Accent.Opacity(opacity));
        if (composition.Length > 0) list.Rectangle(new(b.X + 14 - scroll, textY + 19, Math.Min(caretX, b.Width - 28), 1), theme.Accent.Opacity(opacity));
        list.PopClip();
    }
}

/// <summary>Fixed-height virtualization: only viewport rows plus two overscan rows exist.</summary>
public sealed class VirtualList : Element
{
    private readonly List<Button> pool = [];
    private readonly Dictionary<Button, int> indices = [];
    private int count, selected = -1;
    private float offset;
    public VirtualList()
    {
        ClipToBounds = HitTestVisible = Focusable = true; Role = "listbox";
        Event += e =>
        {
            if (e.Kind == EventKind.Scroll && e.Phase != EventPhase.Capture) { ScrollOffset -= e.Delta * RowHeight; e.Handled = true; }
        };
    }
    public float RowHeight { get; init; } = 54;
    public Func<int, string> ItemText { get; set; } = i => $"Item {i}";
    public int ItemCount { get => count; set { if (value < 0) throw new ArgumentOutOfRangeException(nameof(value)); count = value; selected = Math.Min(selected, count - 1); ScrollOffset = offset; Invalidate(DirtyFlags.Layout); } }
    public int RealizedCount => pool.Count;
    public int FirstVisibleIndex => (int)(offset / RowHeight);
    public float ScrollOffset { get => offset; set { if (!float.IsFinite(value)) throw new ArgumentOutOfRangeException(nameof(value)); Set(ref offset, Math.Clamp(value, 0, Math.Max(0, count * RowHeight - Bounds.Height)), DirtyFlags.Layout); } }
    public int SelectedIndex { get => selected; set { int next = Math.Clamp(value, -1, count - 1); if (Set(ref selected, next, DirtyFlags.Layout | DirtyFlags.Semantics)) SelectionChanged?.Invoke(next); } }
    public event Action<int>? SelectionChanged;
    public void Refresh() => Invalidate(DirtyFlags.Layout);
    protected override void ArrangeChildren(Rect content)
    {
        if (!float.IsFinite(RowHeight) || RowHeight < 1) throw new InvalidOperationException("RowHeight must be finite and at least 1.");
        offset = Math.Clamp(offset, 0, Math.Max(0, count * RowHeight - content.Height));
        int first = Math.Max(0, (int)(offset / RowHeight) - 1);
        int needed = Math.Min(count - first, (int)Math.Ceiling(content.Height / RowHeight) + 3);
        while (pool.Count < needed)
        {
            var row = Add(new Button("", false) { Height = RowHeight - 6, Radius = 8 }); pool.Add(row);
            row.Clicked += () => { SelectedIndex = indices[row]; Document?.Focus(this); };
        }
        while (pool.Count > needed) { var row = pool[^1]; pool.RemoveAt(pool.Count - 1); indices.Remove(row); Remove(row); }
        for (int n = 0; n < pool.Count; n++)
        {
            var row = pool[n]; int index = first + n; indices[row] = index;
            row.Text = ItemText(index); row.Selected = index == selected;
            row.Arrange(new(content.X, content.Y + index * RowHeight - offset, Math.Max(0, content.Width - 10), RowHeight - 6));
        }
    }
    protected override void OnEvent(UiEvent e)
    {
        if (e.Kind == EventKind.KeyDown && e.Key is 38 or 40 or 36 or 35)
        {
            SelectedIndex = e.Key switch { 38 => selected - 1, 40 => selected + 1, 36 => 0, _ => count - 1 };
            if (selected >= 0) ScrollOffset = Math.Clamp(offset, Math.Max(0, (selected + 1) * RowHeight - Bounds.Height), selected * RowHeight);
            e.Handled = true;
        }
    }
    protected override void Paint(DrawList list, Rect b, float opacity, Theme theme)
    {
        if (count * RowHeight <= b.Height) return;
        float thumb = Math.Max(24, b.Height * b.Height / (count * RowHeight));
        list.Rectangle(new(b.Right - 4, b.Y + offset / (count * RowHeight - b.Height) * (b.Height - thumb), 3, thumb), theme.Border.Opacity(opacity), 1.5f);
    }
    public override string SemanticValue => $"{selected + 1} of {count}";
}
