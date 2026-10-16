using System.Globalization;
using System.Numerics;
using System.Runtime.ExceptionServices;

namespace YoUI;

public sealed class CheckBox : Button
{
    private bool value;
    private string label;
    public CheckBox(string text = "Option", bool initial = false) : base("") { label = text ?? ""; value = initial; Role = "checkbox"; Clicked += () => Value = !Value; Refresh(); }
    public string Label { get => label; set { label = value ?? ""; Refresh(); } }
    public bool Value { get => value; set { if (this.value == value) return; this.value = value; Refresh(); Changed?.Invoke(value); } }
    public event Action<bool>? Changed;
    private void Refresh() { Text = (Value ? "☑  " : "☐  ") + label; AccessibleName = label; Selected = Value; }
    public override string SemanticValue => Value ? "checked" : "unchecked";
}

/// <summary>Explicitly owned radio membership. Selection commits exclusivity before callbacks.</summary>
public sealed class RadioGroup
{
    private readonly List<RadioButton> members = [];
    private long revision;
    public RadioButton? Selected { get; private set; }
    public event Action<RadioButton?>? SelectionChanged;
    internal void Add(RadioButton item) { members.Add(item); if (item.Value) Select(item); }
    internal void Remove(RadioButton item) { members.Remove(item); if (Selected == item) Select(null); }
    internal void Select(RadioButton? item)
    {
        if (item != null && !members.Contains(item)) throw new InvalidOperationException("Radio button does not belong to the group.");
        if (Selected == item && members.All(m => m.Value == (m == item))) return;
        Selected = item; long transition = ++revision;
        var changed = members.Where(m => m.Value != (m == item)).ToArray();
        foreach (var member in changed) member.Commit(member == item);
        Exception? failure = null;
        foreach (var member in changed)
        {
            if (revision != transition) break;
            try { member.Notify(); } catch (Exception error) { failure ??= error; }
        }
        if (revision == transition) try { SelectionChanged?.Invoke(Selected); } catch (Exception error) { failure ??= error; }
        if (failure != null) ExceptionDispatchInfo.Capture(failure).Throw();
    }
    internal void Move(RadioButton current, int direction)
    {
        var available = members.Where(m => m.Document == current.Document && m.Visible && m.Enabled && AncestorsActive(m)).ToArray();
        if (available.Length == 0) return;
        int index = Array.IndexOf(available, current);
        var next = available[(Math.Max(0, index) + direction + available.Length) % available.Length];
        Select(next); current.Document?.Focus(next);
    }
    private static bool AncestorsActive(Element element) { for (var p = element.Parent; p != null; p = p.Parent) if (!p.Visible || !p.Enabled) return false; return true; }
}

public sealed class RadioButton : Button
{
    private bool value;
    private string label;
    private RadioGroup? group;
    private bool transferringGroup;
    public RadioButton(string text = "Option", RadioGroup? group = null) : base("") { label = text ?? ""; Role = "radio"; Group = group; Clicked += () => Value = true; Refresh(); }
    public string Label { get => label; set { label = value ?? ""; Refresh(); } }
    public RadioGroup? Group
    {
        get => group;
        set
        {
            if (group == value) return;
            if (transferringGroup) throw new InvalidOperationException("Radio group transfers cannot be nested inside selection callbacks.");
            transferringGroup = true;
            try
            {
                var previous = group; group = null;
                // Removal commits before its callbacks. If it fails, this radio is
                // standalone and can be reassigned; never register two memberships.
                previous?.Remove(this);
                group = value;
                // Addition also commits before callbacks, so a failing notification
                // leaves the target membership consistent with the public Group.
                group?.Add(this);
            }
            finally { transferringGroup = false; }
        }
    }
    public bool Value
    {
        get => value;
        set
        {
            if (group != null) { if (value) group.Select(this); else if (this.value) group.Select(null); }
            else if (this.value != value) { Commit(value); Notify(); }
        }
    }
    public event Action<bool>? Changed;
    internal void Commit(bool next) { value = next; Refresh(); }
    internal void Notify() => Changed?.Invoke(value);
    private void Refresh() { Text = (Value ? "◉  " : "○  ") + label; AccessibleName = label; Selected = Value; }
    protected override void OnEvent(UiEvent e)
    {
        if (e.Kind == EventKind.KeyDown && e.Key is 37 or 38 or 39 or 40 && Group != null) { Group.Move(this, e.Key is 37 or 38 ? -1 : 1); e.Handled = true; return; }
        base.OnEvent(e);
    }
    public override string SemanticValue => Value ? "checked" : "unchecked";
}

public sealed class ComboBox : Element
{
    private readonly Button header;
    private readonly StackPanel choices;
    private readonly ScrollView choicePopup;
    private PopupHandle? popup;
    private string[] items = [];
    private IReadOnlyList<string> readOnlyItems = Array.Empty<string>();
    private int selected = -1;
    public ComboBox()
    {
        Height = 36; Width = 200; Role = "combobox"; Focusable = HitTestVisible = true;
        header = Add(new Button("Select…  ▾", focusable: false) { Height = 36 });
        choices = new StackPanel { Gap = 2, Padding = 4 };
        choicePopup = new ScrollView(choices);
        header.Clicked += () => SetOpen(!IsOpen);
        header.Event += e =>
        {
            if (e.Phase == EventPhase.Target && e.Kind == EventKind.PointerDown) Document?.Focus(this);
        };
        choices.Event += e =>
        {
            if (e.Phase != EventPhase.Bubble || e.Kind != EventKind.KeyDown || e.Key is not (38 or 40 or 36 or 35) || choices.Children.Count == 0) return;
            int current = choices.Children.ToList().IndexOf(Document?.Focused!);
            int index = e.Key == 36 ? 0 : e.Key == 35 ? choices.Children.Count - 1 : Math.Clamp(current + (e.Key == 38 ? -1 : 1), 0, choices.Children.Count - 1);
            Document?.Focus(choices.Children[index]); choicePopup.ScrollIntoView(choices.Children[index]); e.Handled = true;
        };
    }
    public IReadOnlyList<string> Items => readOnlyItems;
    public int SelectedIndex
    {
        get => selected;
        set
        {
            if (value < -1 || value >= items.Length) throw new ArgumentOutOfRangeException(nameof(value));
            if (value == selected) return; selected = value; Refresh(); SelectionChanged?.Invoke(value);
        }
    }
    public event Action<int>? SelectionChanged;
    private void Refresh() { header.Text = (selected >= 0 ? items[selected] : "Select…") + "  ▾"; Invalidate(DirtyFlags.Paint | DirtyFlags.Semantics); }
    public void SetItems(IEnumerable<string> source)
    {
        ArgumentNullException.ThrowIfNull(source);
        var next = source.Select(s => s ?? "").ToArray();
        if (items.SequenceEqual(next)) return;
        SetOpen(false); items = next; readOnlyItems = Array.AsReadOnly(items); choices.Clear();
        for (int i = 0; i < items.Length; i++)
        {
            int index = i;
            var option = choices.Add(new Button(items[i]) { Height = 32 });
            option.Clicked += () => { SelectedIndex = index; SetOpen(false); };
        }
        int previous = selected; selected = items.Length > 0 ? Math.Clamp(selected, 0, items.Length - 1) : -1; Refresh();
        Invalidate(DirtyFlags.Layout); if (selected != previous) SelectionChanged?.Invoke(selected);
    }
    public bool IsOpen => popup?.IsOpen == true;
    public void SetOpen(bool value)
    {
        if (!value) { var current = popup; popup = null; current?.Dispose(); return; }
        if (IsOpen || Document == null || items.Length == 0 || !Visible || !Enabled) return;
        choices.Background = choicePopup.Background = Document.Theme.Surface;
        popup = Document.ShowPopup(this, choicePopup, new(Math.Max(120, Bounds.Width), Math.Min(280, items.Length * 34f + 8)), () => { popup = null; Invalidate(DirtyFlags.Paint | DirtyFlags.Semantics); });
        if (popup.IsOpen && selected >= 0) { Document.Focus(choices.Children[selected]); choicePopup.ScrollIntoView(choices.Children[selected]); }
    }
    public override Vector2 Measure(Vector2 available) { DesiredSize = new(float.IsNaN(Width) ? 200 : Width, float.IsNaN(Height) ? 36 : Height); return DesiredSize; }
    protected override void ArrangeChildren(Rect b) { header.Measure(new(b.Width, b.Height)); header.Arrange(b); }
    protected override void OnEvent(UiEvent e)
    {
        if (e.Kind != EventKind.KeyDown) return;
        if (e.Key is 38 or 40 && items.Length > 0) { SelectedIndex = Math.Clamp(selected + (e.Key == 38 ? -1 : 1), 0, items.Length - 1); e.Handled = true; }
        else if (e.Key is 13 or 32) { SetOpen(!IsOpen); e.Handled = true; }
    }
    public override string SemanticValue => selected >= 0 ? items[selected] : "";
}

public sealed class NumericUpDown : Element
{
    public TextBox Input { get; }
    private readonly Button minus, plus;
    private float value, minimum, maximum = 100, step = 1;
    public NumericUpDown()
    {
        Height = 36; Width = 180; Role = "spinbutton";
        Input = Add(new TextBox { Text = "0", Height = 36 }); minus = Add(new SpinButton("−") { AccessibleName = "Decrease value" }); plus = Add(new SpinButton("+") { AccessibleName = "Increase value" });
        minus.Clicked += () => Adjust(-1); plus.Clicked += () => Adjust(1);
        Input.Event += e =>
        {
            if (e.Phase != EventPhase.Target) return;
            if (e.Kind == EventKind.Blur || e.Kind == EventKind.KeyDown && e.Key == 13) { CommitText(); if (e.Kind == EventKind.KeyDown) e.Handled = true; }
            else if (e.Kind == EventKind.KeyDown && e.Key is 38 or 40) { CommitText(); Adjust(e.Key == 38 ? 1 : -1); e.Handled = true; }
        };
    }
    public float Minimum { get => minimum; set { if (!float.IsFinite(value) || value > maximum) throw new ArgumentOutOfRangeException(nameof(value)); if (minimum == value) return; minimum = value; Value = this.value; Invalidate(DirtyFlags.Semantics); } }
    public float Maximum { get => maximum; set { if (!float.IsFinite(value) || value < minimum) throw new ArgumentOutOfRangeException(nameof(value)); if (maximum == value) return; maximum = value; Value = this.value; Invalidate(DirtyFlags.Semantics); } }
    public float Step { get => step; set { if (!float.IsFinite(value) || value <= 0) throw new ArgumentOutOfRangeException(nameof(value)); step = value; } }
    public float Value
    {
        get => value;
        set
        {
            if (!float.IsFinite(value)) throw new ArgumentOutOfRangeException(nameof(value));
            float next = Math.Clamp(value, Minimum, Maximum); if (next == this.value) return;
            this.value = next; Refresh(); Invalidate(DirtyFlags.Semantics); ValueChanged?.Invoke(next);
        }
    }
    public event Action<float>? ValueChanged;
    private void Refresh() => Input.Text = value.ToString("G9", CultureInfo.InvariantCulture);
    private void CommitText() { if (float.TryParse(Input.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out float parsed) && float.IsFinite(parsed)) Value = parsed; Refresh(); }
    private void Adjust(int direction) => Value = (float)Math.Clamp((double)Value + (double)Step * direction, Minimum, Maximum);
    public override Vector2 Measure(Vector2 available) { DesiredSize = new(float.IsNaN(Width) ? 180 : Width, float.IsNaN(Height) ? 36 : Height); return DesiredSize; }
    protected override void ArrangeChildren(Rect b)
    {
        float button = Math.Min(32, b.Width / 3), gap = Math.Min(4, b.Width / 12), inputWidth = Math.Max(0, b.Width - 2 * button - 2 * gap);
        Input.Measure(new(inputWidth, b.Height)); Input.Arrange(new(b.X, b.Y, inputWidth, b.Height));
        minus.Measure(new(button, b.Height)); minus.Arrange(new(b.X + inputWidth + gap, b.Y, button, b.Height));
        plus.Measure(new(button, b.Height)); plus.Arrange(new(b.Right - button, b.Y, button, b.Height));
    }
    public override string SemanticValue => Value.ToString(CultureInfo.InvariantCulture);
    private sealed class SpinButton : Button
    {
        public SpinButton(string text) : base(text) { Caption.FontSize = 18; Radius = 7; }
        protected override void ArrangeChildren(Rect b)
        {
            var size = Caption.Measure(new(Math.Max(0, b.Width - 4), b.Height));
            float width = Math.Min(Math.Max(0, b.Width - 4), size.X), height = Math.Min(b.Height, size.Y);
            Caption.Arrange(new(b.X + (b.Width - width) / 2, b.Y + (b.Height - height) / 2, width, height));
        }
    }
}

public sealed class ProgressBar : Element
{
    private float value;
    public ProgressBar() { Height = 12; Role = "progressbar"; }
    public float Value { get => value; set { if (!float.IsFinite(value)) throw new ArgumentOutOfRangeException(nameof(value)); Set(ref this.value, Math.Clamp(value, 0, 1), DirtyFlags.Paint | DirtyFlags.Semantics); } }
    protected override void Paint(DrawList list, Rect b, float opacity, Theme theme) { list.Rectangle(b, theme.Border.Opacity(opacity), b.Height / 2); list.Rectangle(b with { Width = b.Width * Value }, theme.Accent.Opacity(opacity), b.Height / 2); }
    public override string SemanticValue => Value.ToString("P0", CultureInfo.InvariantCulture);
}

/// <summary>Retains every page while only the selected page participates in paint, input and layout.</summary>
public sealed class TabControl : Element
{
    private readonly StackPanel headers;
    private readonly Element pagesHost;
    private readonly List<(Element Content, Button Button)> pages = [];
    private int selected = -1;
    private bool selecting;
    private bool addingPage;
    public TabControl() { headers = Add(new StackPanel { Horizontal = true, Gap = 4 }); pagesHost = Add(new Element()); Role = "tablist"; }
    public int PageCount => pages.Count;
    public int SelectedIndex
    {
        get => selected;
        set
        {
            if (value < 0 || value >= pages.Count) throw new ArgumentOutOfRangeException(nameof(value));
            if (value == selected) return;
            if (selecting) throw new InvalidOperationException("Tab selection cannot be nested inside lifecycle callbacks.");
            selecting = true;
            Exception? failure = null;
            try
            {
                bool restore = selected >= 0 && Document?.Focused?.IsDescendantOf(pages[selected].Content) == true;
                selected = value;
                for (int i = 0; i < pages.Count; i++)
                {
                    try { pages[i].Content.Visible = i == value; } catch (Exception error) { failure ??= error; }
                    try { pages[i].Button.Selected = i == value; } catch (Exception error) { failure ??= error; }
                }
                if (restore) try { Document?.Focus(pages[value].Button); } catch (Exception error) { failure ??= error; }
                try { Invalidate(DirtyFlags.Layout | DirtyFlags.Semantics); } catch (Exception error) { failure ??= error; }
            }
            finally { selecting = false; }
            if (failure != null) ExceptionDispatchInfo.Capture(failure).Throw();
            SelectionChanged?.Invoke(value);
        }
    }
    public event Action<int>? SelectionChanged;
    public void AddPage(string title, Element content)
    {
        ArgumentNullException.ThrowIfNull(content);
        if (addingPage || selecting) throw new InvalidOperationException("Pages cannot be added inside page lifecycle callbacks.");
        if (content.Parent != null || content.Document != null || ReferenceEquals(content, this)) throw new ArgumentException("Tab content is already attached.", nameof(content));
        addingPage = true;
        try
        {
            int index = pages.Count;
            var button = headers.Add(new Button(title) { Height = 34 });
            content.Visible = false;
            try { pagesHost.Add(content); }
            catch { headers.Remove(button); if (content.Parent == pagesHost) pagesHost.Remove(content); throw; }
            pages.Add((content, button)); button.Clicked += () => SelectedIndex = index;
            button.Event += e =>
            {
                if (e.Phase != EventPhase.Target || e.Kind != EventKind.KeyDown || e.Key is not (37 or 39 or 36 or 35)) return;
                int next = e.Key == 36 ? 0 : e.Key == 35 ? pages.Count - 1 : (index + (e.Key == 37 ? pages.Count - 1 : 1)) % pages.Count;
                SelectedIndex = next; Document?.Focus(pages[next].Button); e.Handled = true;
            };
            if (selected < 0) SelectedIndex = 0; Invalidate(DirtyFlags.Layout);
        }
        finally { addingPage = false; }
    }
    public override Vector2 Measure(Vector2 available)
    {
        var h = headers.Measure(new(available.X, 38));
        var page = selected < 0 ? Vector2.Zero : pages[selected].Content.Measure(new(available.X, Math.Max(0, available.Y - 44)));
        DesiredSize = new(float.IsNaN(Width) ? Math.Max(h.X, page.X) : Width, float.IsNaN(Height) ? page.Y + 44 : Height); return DesiredSize;
    }
    protected override void ArrangeChildren(Rect b)
    {
        float headerHeight = Math.Min(38, b.Height), top = Math.Min(44, b.Height);
        headers.Measure(new(b.Width, headerHeight)); headers.Arrange(new(b.X, b.Y, b.Width, headerHeight));
        pagesHost.Measure(new(b.Width, Math.Max(0, b.Height - top))); pagesHost.Arrange(new(b.X, b.Y + top, b.Width, Math.Max(0, b.Height - top)));
    }
    public override string SemanticValue => selected < 0 ? "" : $"{selected + 1} of {pages.Count}";
}

public sealed record DataColumn(string Header, float Width = 140, IComparer<string>? Comparer = null);

/// <summary>Read-only string table: fixed widths, vertical viewport, single keyed selection,
/// stable sorting and case-insensitive filtering. No cell editors, resizing or horizontal scroll.</summary>
public sealed class DataGrid : Element
{
    private const float HeaderHeight = 34, RowHeight = 30;
    private IReadOnlyList<DataColumn> columns = Array.Empty<DataColumn>();
    private List<(string Key, string[] Cells)> source = [], rows = [];
    private float offset;
    private int sortColumn = -1;
    private bool descending;
    private string filter = "", selectedKey = "";
    public DataGrid() { ClipToBounds = Focusable = HitTestVisible = true; Role = "grid"; }
    public IReadOnlyList<DataColumn> Columns => columns;
    public int RowCount => rows.Count;
    public string GetRowKey(int index) => rows[index].Key;
    public string SelectedKey => selectedKey;
    public float ScrollOffset => offset;
    public event Action<string>? SelectionChanged;
    public string Filter { get => filter; set { string next = value ?? ""; if (filter == next) return; var rebuilt = BuildRows(source, columns, next, sortColumn, descending); filter = next; rows = rebuilt; FinishRebuild(); } }
    public int VisibleRowCount => Math.Min(Math.Max(0, rows.Count - (int)(offset / RowHeight)), Math.Max(0, (int)Math.Ceiling(Math.Max(0, Bounds.Height - HeaderHeight) / RowHeight) + 1));
    public void SetData(IReadOnlyList<DataColumn> headings, IEnumerable<(string Key, string[] Cells)> data)
    {
        ArgumentNullException.ThrowIfNull(headings); ArgumentNullException.ThrowIfNull(data);
        if (headings.Count == 0 || headings.Any(c => c == null || !float.IsFinite(c.Width) || c.Width < 24 || c.Header == null)) throw new ArgumentException("Table needs headings with finite widths of at least 24.", nameof(headings));
        var nextColumns = Array.AsReadOnly(headings.ToArray());
        var next = data.Select(r => (r.Key, Cells: r.Cells?.ToArray() ?? throw new ArgumentException("Row cells cannot be null.", nameof(data)))).ToList();
        if (next.Any(r => string.IsNullOrEmpty(r.Key) || r.Cells.Length != headings.Count || r.Cells.Any(c => c == null)) || next.Select(r => r.Key).Distinct(StringComparer.Ordinal).Count() != next.Count) throw new ArgumentException("Rows need unique keys and one non-null cell per column.", nameof(data));
        int nextSort = sortColumn < headings.Count ? sortColumn : -1;
        var rebuilt = BuildRows(next, nextColumns, filter, nextSort, descending);
        columns = nextColumns; source = next; rows = rebuilt; sortColumn = nextSort; FinishRebuild();
    }
    public void Sort(int column, bool reverse = false)
    {
        if (column < 0 || column >= columns.Count) throw new ArgumentOutOfRangeException(nameof(column));
        var rebuilt = BuildRows(source, columns, filter, column, reverse); sortColumn = column; descending = reverse; rows = rebuilt; FinishRebuild();
    }
    private static List<(string Key, string[] Cells)> BuildRows(List<(string Key, string[] Cells)> data, IReadOnlyList<DataColumn> headings, string query, int column, bool reverse)
    {
        IEnumerable<(string Key, string[] Cells)> view = data.Where(r => r.Cells.Any(c => c.Contains(query, StringComparison.OrdinalIgnoreCase)));
        if (column >= 0) { var comparer = headings[column].Comparer ?? StringComparer.OrdinalIgnoreCase; view = reverse ? view.OrderByDescending(r => r.Cells[column], comparer) : view.OrderBy(r => r.Cells[column], comparer); }
        return view.ToList();
    }
    private void FinishRebuild()
    {
        bool clear = selectedKey.Length > 0 && rows.All(r => r.Key != selectedKey);
        if (clear) selectedKey = "";
        ClampOffset(); Invalidate(DirtyFlags.Paint | DirtyFlags.Semantics); if (clear) SelectionChanged?.Invoke("");
    }
    private void ClampOffset() => offset = Math.Clamp(offset, 0, Math.Max(0, rows.Count * RowHeight - Math.Max(0, Bounds.Height - HeaderHeight)));
    private void Select(int index)
    {
        if (index < 0 || index >= rows.Count || selectedKey == rows[index].Key) return;
        selectedKey = rows[index].Key; Invalidate(DirtyFlags.Paint | DirtyFlags.Semantics); SelectionChanged?.Invoke(selectedKey);
    }
    protected override void ArrangeChildren(Rect content) => ClampOffset();
    protected override void OnEvent(UiEvent e)
    {
        if (e.Kind == EventKind.Scroll && float.IsFinite(e.Delta)) { float before = offset; offset = (float)Math.Clamp((double)offset - (double)e.Delta * 90, 0, float.MaxValue); ClampOffset(); e.Handled = before != offset; Invalidate(); }
        if (e.Kind == EventKind.PointerDown)
        {
            var b = VisualBounds; var p = e.Position - new Vector2(b.X, b.Y);
            if (p.Y < HeaderHeight) { float x = 0; for (int i = 0; i < columns.Count; i++) { if (p.X >= x && p.X < x + columns[i].Width) { Sort(i, sortColumn == i && !descending); break; } x += columns[i].Width; } }
            else Select((int)((p.Y - HeaderHeight + offset) / RowHeight));
            e.Handled = true;
        }
        if (e.Kind == EventKind.KeyDown && e.Key is 38 or 40 or 36 or 35 && rows.Count > 0)
        {
            int previous = rows.FindIndex(r => r.Key == selectedKey);
            int i = e.Key == 36 ? 0 : e.Key == 35 ? rows.Count - 1 : Math.Clamp(previous + (e.Key == 38 ? -1 : 1), 0, rows.Count - 1);
            // Finish scrolling before notifying, as application handlers may replace the data.
            float viewport = Math.Max(0, Bounds.Height - HeaderHeight);
            if (i * RowHeight < offset) offset = i * RowHeight; else if ((i + 1) * RowHeight > offset + viewport) offset = (i + 1) * RowHeight - viewport;
            ClampOffset(); e.Handled = true; Invalidate(); Select(i);
        }
    }
    protected override void Paint(DrawList list, Rect b, float opacity, Theme theme)
    {
        list.Rectangle(b, theme.Surface.Opacity(opacity));
        int start = (int)(offset / RowHeight), end = Math.Min(rows.Count, start + VisibleRowCount);
        list.PushClip(new(b.X, b.Y + Math.Min(HeaderHeight, b.Height), b.Width, Math.Max(0, b.Height - HeaderHeight)));
        for (int i = start; i < end; i++)
        {
            float y = b.Y + HeaderHeight + i * RowHeight - offset, x = b.X;
            if (rows[i].Key == selectedKey || i % 2 == 1) list.Rectangle(new(x, y, b.Width, RowHeight), (rows[i].Key == selectedKey ? theme.Accent.Opacity(.2f) : theme.SurfaceRaised).Opacity(opacity));
            for (int c = 0; c < columns.Count; c++) { list.PushClip(new(x, y, columns[c].Width, RowHeight)); list.Text(new(x + 8, y + 5, columns[c].Width - 16, 22), rows[i].Cells[c], theme.Text.Opacity(opacity), 13); list.PopClip(); x += columns[c].Width; }
        }
        list.PopClip(); list.Rectangle(new(b.X, b.Y, b.Width, Math.Min(HeaderHeight, b.Height)), theme.SurfaceRaised.Opacity(opacity)); float columnX = b.X;
        for (int c = 0; c < columns.Count; c++) { list.PushClip(new(columnX, b.Y, columns[c].Width, HeaderHeight)); list.Text(new(columnX + 8, b.Y + 7, columns[c].Width - 16, 22), columns[c].Header + (sortColumn == c ? descending ? " ↓" : " ↑" : ""), theme.Text.Opacity(opacity), 13, true); list.PopClip(); columnX += columns[c].Width; }
    }
    public override string SemanticValue => $"{rows.Count} rows; selected {selectedKey}";
}
