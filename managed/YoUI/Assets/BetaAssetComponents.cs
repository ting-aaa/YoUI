using System.Globalization;

namespace YoUI.Assets;

/// <summary>Version-2 declarative component settings; no delegates or runtime resources.</summary>
public sealed class BetaNodeOptions
{
    public List<string> Items { get; set; } = ["First", "Second", "Third"];
    public int SelectedIndex { get; set; }
    public string Group { get; set; } = "default";
    public float Number { get; set; }
    public float Minimum { get; set; }
    public float Maximum { get; set; } = 100;
    public float Step { get; set; } = 1;
    public List<string> GridRows { get; set; } = ["*"];
    public List<string> GridColumns { get; set; } = ["*", "*"];
    public int Row { get; set; }
    public int Column { get; set; }
    public int RowSpan { get; set; } = 1;
    public int ColumnSpan { get; set; } = 1;
    public List<string> TableColumns { get; set; } = ["Name", "Status"];
    public List<List<string>> TableRows { get; set; } = [["Aurora", "Ready"], ["Boreal", "Running"]];
}

/// <summary>Shared definition contract for the loader, authoring and generated C#.</summary>
public static class BetaAssetComponents
{
    public static BetaNodeOptions Options(UiNode node) => node.Beta ?? new();
    public static GridTrack[] Tracks(IEnumerable<string> definitions) => definitions.Select(value =>
    {
        if (value == "Auto") return GridTrack.Auto;
        if (value != null && value.EndsWith('*'))
        {
            float weight = value.Length == 1 ? 1 : float.Parse(value[..^1], CultureInfo.InvariantCulture);
            if (!float.IsFinite(weight) || weight <= 0 || weight > 32768) throw new InvalidDataException("Invalid star weight.");
            return GridTrack.Star(weight);
        }
        if (!float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float size) || !float.IsFinite(size) || size < 0 || size > 32768) throw new InvalidDataException("Grid tracks require Auto, *, positive weighted *, or a finite size.");
        return GridTrack.Fixed(size);
    }).ToArray();

    internal static void Validate(UiNode n, int version)
    {
        if (version == 1 && (n.Kind > NodeKind.HorizontalStack || n.Beta != null || n.Layout != null || n.Kind == NodeKind.Button && n.Children?.Count > 0)) throw new InvalidDataException("Beta components and layout require asset version 2.");
        if (n.Layout != null) { try { n.Layout.Validate(); } catch (ArgumentException error) { throw new InvalidDataException("Invalid box layout.", error); } }
        if (n.Kind is NodeKind.Content or NodeKind.Button && n.Children?.Count > 1) throw new InvalidDataException("Content permits one child.");
        var o = Options(n);
        if (o.Items == null || o.Items.Count > 10000 || o.Items.Any(s => s == null || s.Length > 16384 || s.Contains('\0'))) throw new InvalidDataException("Invalid item collection.");
        if (o.Group == null || o.Group.Length > 128 || o.Group.Contains('\0')) throw new InvalidDataException("Invalid radio group.");
        if (new[] { o.Number, o.Minimum, o.Maximum, o.Step }.Any(v => !float.IsFinite(v) || Math.Abs(v) > 32768) || o.Minimum > o.Maximum || o.Step <= 0) throw new InvalidDataException("Invalid numeric range.");
        if (o.GridRows == null || o.GridColumns == null || o.GridRows.Count is < 1 or > 256 || o.GridColumns.Count is < 1 or > 256) throw new InvalidDataException("Grid requires 1-256 tracks.");
        try { _ = Tracks(o.GridRows); _ = Tracks(o.GridColumns); } catch (FormatException error) { throw new InvalidDataException("Invalid grid track.", error); }
        if (o.Row < 0 || o.Column < 0 || o.RowSpan < 1 || o.ColumnSpan < 1 || o.Row > 255 || o.Column > 255 || o.RowSpan > 256 || o.ColumnSpan > 256) throw new InvalidDataException("Invalid grid cell.");
        if (n.Kind == NodeKind.ComboBox && (o.SelectedIndex < -1 || o.SelectedIndex >= o.Items.Count)) throw new InvalidDataException("Selected option is outside the items.");
        if (n.Kind == NodeKind.TabControl && ((n.Children?.Count ?? 0) > 0 ? o.SelectedIndex < 0 || o.SelectedIndex >= n.Children!.Count : o.SelectedIndex is < -1 or > 0)) throw new InvalidDataException("Selected tab is outside the pages.");
        if (o.TableColumns == null || o.TableColumns.Count is < 1 or > 64 || o.TableColumns.Any(s => s == null || s.Length > 1024 || s.Contains('\0')) || o.TableRows == null || o.TableRows.Count > 10000 || o.TableRows.Any(r => r == null || r.Count != o.TableColumns.Count || r.Any(s => s == null || s.Length > 16384 || s.Contains('\0')))) throw new InvalidDataException("Invalid table shape or limits.");
    }

    internal static void ValidateChild(UiNode parent, UiNode child)
    {
        if (parent.Kind != NodeKind.Grid) return;
        var p = Options(parent); var c = Options(child);
        if ((long)c.Row + c.RowSpan > p.GridRows.Count || (long)c.Column + c.ColumnSpan > p.GridColumns.Count) throw new InvalidDataException("Grid child extends beyond its tracks.");
    }

    public static Element Create(UiNode n) => n.Kind switch
    {
        NodeKind.CheckBox => new CheckBox(n.Text, n.Checked) { Name = n.Name },
        NodeKind.RadioButton => new RadioButton(n.Text) { Name = n.Name },
        NodeKind.ComboBox => new ComboBox { Name = n.Name },
        NodeKind.NumericUpDown => new NumericUpDown { Name = n.Name },
        NodeKind.ProgressBar => new ProgressBar { Name = n.Name },
        NodeKind.TabControl => new TabControl { Name = n.Name },
        NodeKind.Separator => new Separator { Name = n.Name },
        NodeKind.DataGrid => new DataGrid { Name = n.Name },
        NodeKind.Content => new ContentControl { Name = n.Name },
        NodeKind.Grid => new Grid { Name = n.Name },
        NodeKind.Wrap => new WrapPanel { Name = n.Name },
        NodeKind.VirtualList => new VirtualList { Name = n.Name },
        NodeKind.TreeView => new TreeView { Name = n.Name },
        NodeKind.ScrollView => new ScrollView(new StackPanel { Gap = n.Gap }) { Name = n.Name },
        _ => new Element { Name = n.Name }
    };

    public static void Configure(Element element, UiNode n, IDictionary<string, RadioGroup> groups)
    {
        var o = Options(n);
        switch (element)
        {
            case RadioButton radio:
                if (!groups.TryGetValue(o.Group, out var group)) groups.Add(o.Group, group = new());
                radio.Group = group; radio.Value = n.Checked; break;
            case ComboBox combo: combo.SetItems(o.Items); combo.SelectedIndex = o.SelectedIndex; break;
            case NumericUpDown number:
                number.Minimum = Math.Min(0, o.Minimum); number.Maximum = o.Maximum; number.Minimum = o.Minimum; number.Step = o.Step; number.Value = o.Number; break;
            case ProgressBar progress: progress.Value = n.Value; break;
            case DataGrid table: table.SetData(o.TableColumns.Select(s => new DataColumn(s)).ToArray(), o.TableRows.Select((r, i) => (i.ToString(CultureInfo.InvariantCulture), r.ToArray()))); break;
            case Grid grid: grid.Rows = Tracks(o.GridRows); grid.Columns = Tracks(o.GridColumns); grid.Gap = n.Gap; break;
            case WrapPanel wrap: wrap.Gap = n.Gap; break;
            case VirtualList list: list.ItemText = i => o.Items[i]; list.ItemCount = o.Items.Count; break;
            case TreeView tree: tree.SetItems(o.Items.Select((s, i) => new TreeItem(n.Id + ":" + i, s, "", false, [])).ToArray()); break;
        }
    }

    public static void AttachChild(Element parent, Element child, UiNode definition)
    {
        if (parent is TabControl tabs) tabs.AddPage(definition.Name, child);
        else if (parent is Button button) button.Content = child;
        else if (parent is ContentControl content) content.Content = child;
        else if (parent is ScrollView scroll) scroll.Content.Add(child);
        else
        {
            parent.Add(child);
            if (parent is Grid grid) { var o = Options(definition); grid.SetCell(child, o.Row, o.Column, o.RowSpan, o.ColumnSpan); }
        }
    }

    public static void Complete(Element element, UiNode n)
    {
        if (element is TabControl tabs && n.Children.Count > 0) tabs.SelectedIndex = Options(n).SelectedIndex;
    }
}
