using System.Numerics;

namespace YoUI;

public sealed record PropertyEntry(string Name, Func<string> Read, Action<string> Write, bool Editable = true, IReadOnlyList<string>? Choices = null)
{
    public string Group { get; init; } = "Properties";
    public string Hint { get; init; } = "";
    public Func<Element>? CreateEditor { get; init; }
}

public interface IPropertyEditor { void RefreshValue(); }

/// <summary>Searchable, collapsible property sections. Edits commit on Enter or blur.</summary>
public sealed class PropertyGrid : ScrollView
{
    private readonly StackPanel stack;
    private readonly List<(PropertyEntry Entry, TextBox Input)> fields = [];
    private readonly List<IPropertyEditor> editors = [];
    private readonly List<(string Name, Button Header, StackPanel Body, List<(PropertyEntry Entry, Element Row)> Rows)> groups = [];
    private readonly HashSet<string> collapsed = ["Node"];
    private string filter = "";
    private bool rebinding;
    public event Action<string>? ValidationFailed;
    public string Filter { get => filter; set { filter = value.Trim(); ApplyFilter(); ScrollTo(0); } }
    public PropertyGrid() : this(new StackPanel { Gap = 6, Padding = 8 }) { }
    private PropertyGrid(StackPanel content) : base(content) => stack = content;
    public void SetEntries(IEnumerable<PropertyEntry> entries)
    {
        rebinding = true;
        try { stack.Clear(); } finally { rebinding = false; }
        fields.Clear(); editors.Clear(); groups.Clear();
        string[] order = ["Layout", "Content", "Appearance", "Typography", "Behavior", "Visibility", "Node"];
        foreach (var group in entries.GroupBy(e => e.Group).OrderBy(g => { int i = Array.IndexOf(order, g.Key); return i < 0 ? order.Length : i; }))
        {
            var header = stack.Add(new Button(group.Key) { Height = 30, Radius = 3 }); header.Caption.FontSize = 12; header.Caption.Bold = true;
            var body = stack.Add(new StackPanel { Gap = 3 });
            var rows = new List<(PropertyEntry Entry, Element Row)>(); groups.Add((group.Key, header, body, rows));
            header.Clicked += () => { if (!collapsed.Add(group.Key)) collapsed.Remove(group.Key); ApplyFilter(); };
            header.Event += e => { if (e.Kind == EventKind.Focus) ScrollIntoView(header); };
            foreach (var entry in group)
            {
                Element editor;
                if (entry.CreateEditor != null) editor = entry.CreateEditor();
                else if (entry.Choices is { Count: > 0 } options)
                    editor = new PropertyChoice(entry.Read, v => Write(entry, v), options);
                else
                {
                    var input = new TextBox { Text = entry.Read(), Height = 30, AccessibleName = entry.Name, Placeholder = entry.Hint };
                    fields.Add((entry, input)); editor = input;
                    void Commit()
                    {
                        if (rebinding || !entry.Editable || input.Text == entry.Read()) return;
                        Write(entry, input.Text); input.Text = entry.Read();
                    }
                    input.Event += e => { if (e.Phase == EventPhase.Target && (e.Kind == EventKind.Blur || (e.Kind == EventKind.KeyDown && e.Key == 13))) { Commit(); if (e.Kind == EventKind.KeyDown) e.Handled = true; } };
                }
                editor.Enabled = entry.Editable; editor.AccessibleName = entry.Name;
                if (editor is IPropertyEditor custom) editors.Add(custom);
                var row = body.Add(new PropertyRow(entry, editor)); rows.Add((entry, row));
                editor.Event += e => { if (e.Kind == EventKind.Focus) ScrollIntoView(row); };
            }
        }
        ApplyFilter();
    }
    private void Write(PropertyEntry entry, string value)
    {
        try { entry.Write(value); }
        catch (Exception error) when (error is ArgumentException or InvalidDataException or FormatException or OverflowException)
        { ValidationFailed?.Invoke($"{entry.Name}: {error.Message}"); }
        RefreshValues();
    }
    private void ApplyFilter()
    {
        foreach (var (name, header, body, rows) in groups)
        {
            bool searching = filter.Length != 0;
            foreach (var (entry, row) in rows) row.Visible = !searching || (name + " " + entry.Name + " " + entry.Hint).Contains(filter, StringComparison.OrdinalIgnoreCase);
            bool any = rows.Any(r => r.Row.Visible), open = searching || !collapsed.Contains(name);
            header.Visible = any; header.Text = (open ? "▾  " : "▸  ") + name;
            body.Visible = any && open;
        }
        Invalidate(DirtyFlags.Layout);
    }
    public void RefreshValues()
    {
        foreach (var (entry, input) in fields) if (!input.IsFocused) input.Text = entry.Read();
        foreach (var editor in editors) editor.RefreshValue();
    }
    private sealed class PropertyRow : Element
    {
        private readonly Label label;
        private readonly Element editor;
        private readonly bool full;
        public PropertyRow(PropertyEntry entry, Element editor)
        {
            full = entry.CreateEditor != null; this.editor = Add(editor);
            label = Add(new Label(entry.Name) { FontSize = 12, Color = Theme.Dark.Muted });
        }
        public override Vector2 Measure(Vector2 available)
        {
            var size = editor.Measure(new(full ? available.X : available.X * .57f, available.Y));
            DesiredSize = new(available.X, full ? size.Y + 24 : Math.Max(32, size.Y)); return DesiredSize;
        }
        protected override void ArrangeChildren(Rect b)
        {
            float split = MathF.Floor(b.Width * .40f);
            label.Arrange(new(b.X + 4, b.Y + (full ? 2 : 8), full ? b.Width - 8 : split - 8, 18));
            editor.Arrange(full ? new(b.X + 4, b.Y + 24, b.Width - 8, b.Height - 24) : new(b.X + split, b.Y + 1, b.Width - split, b.Height - 2));
        }
    }
}

/// <summary>Explicit choices, with checkbox presentation for booleans; never cycles enum values.</summary>
public sealed class PropertyChoice : StackPanelBase, IPropertyEditor
{
    private readonly Func<string> read;
    private readonly Button current;
    private readonly List<Button> options = [];
    private bool open;
    private readonly bool boolean;
    public PropertyChoice(Func<string> read, Action<string> write, IReadOnlyList<string> values)
    {
        this.read = read; boolean = values.SequenceEqual(new[] { "false", "true" });
        current = Add(new Button("") { Height = 30, Radius = 3 }); current.Caption.FontSize = 12;
        current.Clicked += () => { if (boolean) { write(read() == "true" ? "false" : "true"); RefreshValue(); } else { open = !open; Update(); } };
        if (!boolean) foreach (var value in values)
        {
            var button = Add(new Button(value) { Height = 28, Radius = 3 }); button.Caption.FontSize = 12; options.Add(button);
            button.Clicked += () => { open = false; write(value); RefreshValue(); Update(); };
        }
        RefreshValue(); Update();
    }
    public void RefreshValue() { current.Text = boolean ? (read() == "true" ? "☑  On" : "☐  Off") : read() + "  ▾"; current.Selected = boolean && read() == "true"; foreach (var option in options) option.Selected = option.Text == read(); }
    private void Update() { foreach (var option in options) option.Visible = open; Invalidate(DirtyFlags.Layout); }
}

/// <summary>Small vertical layout used by compound property controls.</summary>
public class StackPanelBase : Element
{
    public override Vector2 Measure(Vector2 available)
    {
        float height = 0; foreach (var child in Children.Where(c => c.Visible)) height += child.Measure(available).Y + 2;
        DesiredSize = new(available.X, height); return DesiredSize;
    }
    protected override void ArrangeChildren(Rect b)
    {
        float y = b.Y; foreach (var child in Children.Where(c => c.Visible)) { child.Arrange(new(b.X, y, b.Width, child.DesiredSize.Y)); y += child.DesiredSize.Y + 2; }
    }
}

/// <summary>Direct spatial selection of nine reference points, optionally with a stretch preset.</summary>
public sealed class ReferencePicker : Element, IPropertyEditor
{
    private readonly Func<int> read;
    private readonly List<Button> buttons = [];
    private readonly Label value;
    public IReadOnlyList<Button> Presets => buttons;
    public ReferencePicker(Func<int> read, Action<int> select, bool stretch = false)
    {
        this.read = read; Height = stretch ? 160 : 126;
        string[] names = ["Top left", "Top center", "Top right", "Center left", "Center", "Center right", "Bottom left", "Bottom center", "Bottom right", "Stretch"];
        for (int i = 0; i < (stretch ? 10 : 9); i++)
        {
            int index = i; var button = Add(new ReferenceButton(i) { AccessibleName = names[i], Height = 30 }); buttons.Add(button);
            button.Clicked += () => { select(index); RefreshValue(); };
            button.Event += e => { if (e.Kind == EventKind.Focus) for (var parent = Parent; parent != null; parent = parent.Parent) if (parent is ScrollView scroll) { scroll.ScrollIntoView(button); break; } };
        }
        value = Add(new Label { FontSize = 11, Color = Theme.Dark.Muted }); RefreshValue();
    }
    public void RefreshValue() { int selected = read(); for (int i = 0; i < buttons.Count; i++) buttons[i].Selected = i == selected; value.Text = selected >= 0 && selected < buttons.Count ? buttons[selected].AccessibleName : "Custom"; }
    public override Vector2 Measure(Vector2 available) { DesiredSize = new(available.X, Height); return DesiredSize; }
    protected override void ArrangeChildren(Rect b)
    {
        for (int i = 0; i < buttons.Count; i++) buttons[i].Arrange(i < 9 ? new(b.X + i % 3 * 38, b.Y + i / 3 * 34, 34, 30) : new(b.X, b.Y + 104, 110, 28));
        value.Arrange(new(b.X, b.Y + (buttons.Count == 10 ? 139 : 106), b.Width, 18));
    }
    private sealed class ReferenceButton(int index) : Button("")
    {
        protected override void Paint(DrawList list, Rect b, float opacity, Theme theme)
        {
            base.Paint(list, b, opacity, theme);
            var c = (Selected ? theme.Accent : theme.Muted).Opacity(opacity);
            if (index == 9) { list.Text(new(b.X + 10, b.Y + 7, b.Width - 12, 18), "Stretch", c, 11); return; }
            list.Rectangle(new(b.X + 7, b.Y + 5, b.Width - 14, b.Height - 10), theme.Border.Opacity(opacity), 2);
            float x = b.X + 8 + index % 3 * (b.Width - 16) / 2, y = b.Y + 6 + index / 3 * (b.Height - 12) / 2;
            list.Rectangle(new(x - 3, y - 3, 6, 6), c, 2);
        }
    }
}
