using System.Globalization;
using System.Numerics;
using YoUI.Assets;

namespace YoUI.Editor.App;

/// <summary>The editor is a client of YoUI controls; no alternate widget or web toolkit.</summary>
public sealed class EditorShell : Element
{
    public EditorSession Session { get; }
    public ExportCatalog Exporters { get; } = new();
    public EditorCanvas Canvas { get; }
    public TreeView Hierarchy { get; }
    public PropertyGrid Inspector { get; }
    public TextBox PropertySearch { get; }
    public TextBox ComponentSearch { get; }
    public ProjectBrowser Project { get; }
    public TreeView Resources { get; }
    public Button PreviewButton { get; }
    public Button SaveButton { get; }
    public Button UndoButton { get; }
    public Button RedoButton { get; }
    private readonly Label fileLabel, selectionLabel, statusLabel, typeLabel;
    private readonly Button colorsTab, fontsTab, commandsTab;
    private string resourceKind = "Colors";
    private string? projectFile;
    private readonly List<(Button Button, NodeKind Kind)> palette = [];
    private readonly Button newButton, openButton, exportButton, pluginButton, exporterButton, fitButton, minusButton, plusButton, snapButton, upButton, downButton, duplicateButton, deleteButton, themeButton;
    private string inspected = "";
    private int exporterIndex;
    private UiDocument? bound;
    public IUiExporter Exporter => Exporters.Exporters[exporterIndex];
    public Action? NewRequested, OpenRequested, SaveRequested, SaveAsRequested, ExportRequested, PluginRequested;
    public Action<string>? OpenFileRequested;
    public string Status { get; private set; } = "Ready";
    public EditorShell(EditorSession? session = null)
    {
        Session = session ?? new(); Background = Color.Hex(0x131A25);
        fileLabel = Add(new Label()); selectionLabel = Add(new Label { FontSize = 14, Bold = true });
        typeLabel = Add(new Label { FontSize = 11, Color = Theme.Dark.Muted });
        statusLabel = Add(new Label { FontSize = 11, Color = Theme.Dark.Muted });
        Canvas = Add(new EditorCanvas(Session)); Canvas.Status += SetStatus;
        Hierarchy = Add(new TreeView { AllowReparent = true });
        Hierarchy.SelectionChanged += Session.Select;
        Hierarchy.ReparentRequested += (id, parent) => Try(() => Session.Reparent(id, parent));
        Inspector = Add(new PropertyGrid()); Inspector.ValidationFailed += SetStatus;
        PropertySearch = Add(new TextBox { Placeholder = "Filter properties…" }); PropertySearch.Changed += value => Inspector.Filter = value;
        ComponentSearch = Add(new TextBox { Placeholder = "Find component…" }); ComponentSearch.Changed += _ => Invalidate(DirtyFlags.Layout);
        Project = Add(new ProjectBrowser(Environment.CurrentDirectory)); Project.OpenRequested += path => OpenFileRequested?.Invoke(path); Project.Failed += SetStatus;
        Resources = Add(new TreeView { RowHeight = 28 }); Resources.SelectionChanged += id => { if (id.StartsWith("node:")) Session.Select(id[5..]); };
        Button Make(string text, Action action, bool primary = false) { var b = Add(new Button(text) { Height = 30, Radius = 6, Primary = primary }); b.Caption.FontSize = 12; b.Clicked += () => Try(action); return b; }
        newButton = Make("New", () => NewRequested?.Invoke()); openButton = Make("Open", () => OpenRequested?.Invoke());
        SaveButton = Make("Save", () => SaveRequested?.Invoke()); exportButton = Make("Export", () => ExportRequested?.Invoke(), true);
        pluginButton = Make("Load plugin…", () => PluginRequested?.Invoke()); exporterButton = Make(Exporter.DisplayName, () => { exporterIndex = (exporterIndex + 1) % Exporters.Exporters.Count; UpdateExporters(); });
        UndoButton = Make("Undo", Session.Undo); RedoButton = Make("Redo", Session.Redo);
        fitButton = Make("Fit", Canvas.Fit); minusButton = Make("−", () => Canvas.SetZoom(Canvas.Zoom / 1.15f)); plusButton = Make("+", () => Canvas.SetZoom(Canvas.Zoom * 1.15f));
        snapButton = Make("Snap: 8", () => { Canvas.Snap = !Canvas.Snap; snapButton!.Text = Canvas.Snap ? "Snap: 8" : "Snap: off"; Canvas.Invalidate(); });
        PreviewButton = Make("▶ Preview", TogglePreview);
        colorsTab = Make("Colors", () => SetResources("Colors")); fontsTab = Make("Fonts", () => SetResources("Fonts")); commandsTab = Make("Commands", () => SetResources("Commands"));
        themeButton = Make("Theme", () => Session.Edit(a => a.DarkTheme = !a.DarkTheme));
        upButton = Make("↑", () => Session.Move(-1)); downButton = Make("↓", () => Session.Move(1)); duplicateButton = Make("Dup", Session.Duplicate); deleteButton = Make("Del", Session.Delete);
        foreach (var kind in Enum.GetValues<NodeKind>().Where(k => k != NodeKind.Canvas)) palette.Add((Make(kind switch { NodeKind.VerticalStack => "V Stack", NodeKind.HorizontalStack => "H Stack", NodeKind.TextBox => "Input", _ => kind.ToString() }, () => Session.Add(kind)), kind));
        Session.Changed += Refresh; Refresh(true); SetStatus("Ready · add components, edit properties, then preview or export");
    }
    public void Try(Action action)
    {
        try { action(); }
        catch (Exception e) when (e is not OutOfMemoryException and not StackOverflowException) { SetStatus("Error: " + e.GetBaseException().Message); }
    }
    public void SetStatus(string text) { Status = text; statusLabel.Text = text; Invalidate(); }
    public void UpdateExporters() { exporterButton.Text = Exporter.DisplayName; SetStatus("Export target: " + Exporter.DisplayName); }
    public void SelectExporter(string id) { int i = Exporters.Exporters.ToList().FindIndex(e => e.Id == id); if (i < 0) throw new InvalidDataException("Unknown exporter: " + id); exporterIndex = i; UpdateExporters(); }
    public void CommitInput() => Document?.Focus(null);
    public void TogglePreview()
    {
        CommitInput(); Canvas.SetPreview(!Canvas.Preview); PreviewButton.Text = Canvas.Preview ? "■ Edit" : "▶ Preview";
        foreach (var (button, _) in palette) button.Enabled = !Canvas.Preview;
        Hierarchy.Enabled = Inspector.Enabled = Resources.Enabled = upButton.Enabled = downButton.Enabled = duplicateButton.Enabled = deleteButton.Enabled = themeButton.Enabled = !Canvas.Preview;
        UndoButton.Enabled = !Canvas.Preview && Session.CanUndo; RedoButton.Enabled = !Canvas.Preview && Session.CanRedo;
        if (Canvas.Preview) Document?.Focus(Canvas);
    }
    private void Refresh(bool model)
    {
        fileLabel.Text = (Session.IsDirty ? "●  " : "") + (Session.FilePath == null ? Session.Asset.Name : Path.GetFileName(Session.FilePath));
        UndoButton.Enabled = !Canvas.Preview && Session.CanUndo; RedoButton.Enabled = !Canvas.Preview && Session.CanRedo;
        selectionLabel.Text = Session.Selected.Name;
        typeLabel.Text = Session.Selected.Kind + "  /  " + (Session.SelectedId == Session.Asset.Root.Id ? "Artboard" : "Component");
        TreeItem Tree(UiNode n) => new(n.Id, n.Name, n.Kind.ToString(), n.IsContainer, n.Children.Select(Tree).ToArray());
        if (model) Hierarchy.SetItems([Tree(Session.Asset.Root)]); Hierarchy.SelectedId = Session.SelectedId;
        // A different node/kind requires a new form. Property updates preserve active input and focus.
        string key = Session.SelectedId + ":" + Session.Selected.Kind + ":" + (Session.Selected.Anchor == AnchorMode.Stretch) + ":" + Session.ParentOf(Session.SelectedId)?.IsStack;
        if (inspected != key) { inspected = key; Inspector.SetEntries(Properties(Session.SelectedId)); }
        else Inspector.RefreshValues();
        if (model) SetResources(resourceKind);
        if (projectFile != Session.FilePath) { projectFile = Session.FilePath; if (projectFile != null) { Project.SetDirectory(Path.GetDirectoryName(projectFile)!); Project.Refresh(); } }
        Invalidate();
    }
    private void SetResources(string kind)
    {
        resourceKind = kind; colorsTab.Selected = kind == "Colors"; fontsTab.Selected = kind == "Fonts"; commandsTab.Selected = kind == "Commands";
        var nodes = Session.Asset.Root.DescendantsAndSelf();
        IEnumerable<(string Key, UiNode Node)> entries = kind switch
        {
            "Fonts" => nodes.Where(n => n.Kind is NodeKind.Text or NodeKind.Button or NodeKind.Toggle).Select(n => ($"{n.FontSize:0.#} px / {(n.Bold ? "Bold" : "Regular")}", n)),
            "Commands" => nodes.Where(n => n.Command.Length > 0).Select(n => (n.Command, n)),
            _ => nodes.SelectMany(n => new[] { n.Fill, n.Foreground }.Where(c => c.Length > 0 && c != "#00000000").Distinct().Select(c => (c.ToUpperInvariant(), n)))
        };
        Resources.SetItems(entries.GroupBy(e => e.Key).Select(g => new TreeItem("resource:" + g.Key, g.Key + "  ·  " + g.Count() + " uses", kind, true, g.Select(e => new TreeItem("node:" + e.Node.Id, e.Node.Name, e.Node.Kind.ToString(), false, [])).ToArray())).ToArray());
    }
    private IEnumerable<PropertyEntry> Properties(string id)
    {
        UiNode Node() => Session.Find(id);
        string group = "Node";
        PropertyEntry Text(string name, Func<UiNode, string> read, Action<UiNode, string> write, bool editable = true) => new(name, () => read(Node()), value => Session.Edit(a => write(a.Root.DescendantsAndSelf().First(n => n.Id == id), value)), editable) { Group = group };
        PropertyEntry Number(string name, Func<UiNode, float> read, Action<UiNode, float> write, bool editable = true) => Text(name, n => read(n).ToString("0.###", CultureInfo.InvariantCulture), (n, value) => write(n, float.Parse(value, CultureInfo.InvariantCulture)), editable);
        PropertyEntry Flag(string name, Func<UiNode, bool> read, Action<UiNode, bool> write) => Text(name, n => read(n) ? "true" : "false", (n, value) => write(n, bool.Parse(value))) with { Choices = ["false", "true"] };
        bool root = id == Session.Asset.Root.Id, stack = Session.ParentOf(id)?.IsStack == true;
        yield return Text("Name", n => n.Name, (n, v) => n.Name = v);
        yield return Text("Type", n => n.Kind.ToString(), (_, _) => { }, false);
        if (root) yield return new("Screen name", () => Session.Asset.Name, value => Session.Edit(a => a.Name = value)) { Group = group };
        group = "Content";
        if (Node().Kind is NodeKind.Text or NodeKind.Button or NodeKind.Toggle or NodeKind.TextBox) yield return Text("Text", n => n.Text, (n, v) => n.Text = v);
        group = "Layout";
        if (!root)
        {
            yield return new("Anchors & pivot · keep position", () => "", _ => { }, !stack) { Group = group, Hint = "Position anchor pivot presets", CreateEditor = () => new LayoutReferences(Node, mode => Try(() => Session.SetAnchor(id, mode, Canvas.ParentContentBounds(id))), point => Try(() => Session.SetPivot(id, point, Canvas.ParentContentBounds(id)))) };
            if (stack) yield return Text("Position", _ => "Parent stack", (_, _) => { }, false);
            yield return Number(Node().Anchor == AnchorMode.Stretch ? "Left inset" : "Position X", n => n.X, (n, v) => n.X = v, !stack);
            yield return Number(Node().Anchor == AnchorMode.Stretch ? "Top inset" : "Position Y", n => n.Y, (n, v) => n.Y = v, !stack);
        }
        yield return Number(Node().Anchor == AnchorMode.Stretch && !stack ? "Right inset" : "Width", n => n.Width, (n, v) => n.Width = v);
        yield return Number(Node().Anchor == AnchorMode.Stretch && !stack ? "Bottom inset" : "Height", n => n.Height, (n, v) => n.Height = v);
        if (stack) yield return Number("Grow", n => n.Grow, (n, v) => n.Grow = v);
        if (Node().IsContainer) yield return Number("Padding", n => n.Padding, (n, v) => n.Padding = v);
        if (Node().IsStack) yield return Number("Gap", n => n.Gap, (n, v) => n.Gap = v);
        group = "Appearance";
        if (Node().Kind is NodeKind.Canvas or NodeKind.Panel or NodeKind.Button or NodeKind.Toggle or NodeKind.VerticalStack or NodeKind.HorizontalStack)
        {
            yield return Text("Fill", n => n.Fill, (n, v) => n.Fill = v) with { Hint = "#RRGGBB / #RRGGBBAA" };
            yield return Number("Corner radius", n => n.Radius, (n, v) => n.Radius = v);
        }
        if (Node().Kind is NodeKind.Text or NodeKind.Button or NodeKind.Toggle)
        {
            group = "Typography";
            yield return Text("Text color", n => n.Foreground, (n, v) => n.Foreground = v) with { Hint = "Theme default" };
            yield return Number("Font size", n => n.FontSize, (n, v) => n.FontSize = v);
            yield return Flag("Bold", n => n.Bold, (n, v) => n.Bold = v);
        }
        if (Node().Kind is NodeKind.Button or NodeKind.Toggle)
        {
            group = "Behavior";
            yield return Flag("Primary", n => n.Primary, (n, v) => n.Primary = v);
            yield return Text("Command", n => n.Command, (n, v) => n.Command = v);
        }
        if (Node().Kind == NodeKind.Toggle) yield return Flag("Checked", n => n.Checked, (n, v) => n.Checked = v);
        if (Node().Kind == NodeKind.Slider) yield return Number("Value", n => n.Value, (n, v) => n.Value = v);
        group = "Visibility";
        yield return Number("Opacity", n => n.Opacity, (n, v) => n.Opacity = v);
        if (Node().IsContainer) yield return Flag("Clip children", n => n.Clip, (n, v) => n.Clip = v);
        if (!root) { yield return Flag("Visible", n => n.Visible, (n, v) => n.Visible = v); yield return Flag("Enabled", n => n.Enabled, (n, v) => n.Enabled = v); }
    }
    private void Shortcuts(UiEvent e)
    {
        if (e.Kind != EventKind.KeyDown) return;
        if (e.Control && e.Key is 83 or 79 or 78)
        {
            CommitInput(); if (e.Key == 83) { if (e.Shift) SaveAsRequested?.Invoke(); else SaveRequested?.Invoke(); } else if (e.Key == 79) OpenRequested?.Invoke(); else NewRequested?.Invoke(); e.Handled = true; return;
        }
        if (e.Key == 116 || (Canvas.Preview && e.Key == 27)) { TogglePreview(); e.Handled = true; return; }
        if (Canvas.Preview) { if (Document?.Focused == Canvas) Canvas.Forward(e); return; }
        if (Document?.Focused is TextBox) return;
        if (e.Key == 27) { Canvas.CancelDrag(); e.Handled = true; return; }
        if (e.Control && e.Key is 90 or 89 or 68)
        {
            if (Session.InTransaction) { Canvas.CancelDrag(); e.Handled = true; return; }
            Try(() => { if (e.Key == 68) Session.Duplicate(); else if (e.Key == 89 || e.Shift) Session.Redo(); else Session.Undo(); }); e.Handled = true;
        }
        if (e.Key == 46) { Session.Delete(); e.Handled = true; }
        if (Document?.Focused == Canvas && Canvas.CanManipulate && e.Key is >= 37 and <= 40)
        {
            float step = e.Shift ? 8 : 1; Try(() => Session.UpdateSelected(n => { n.X += e.Key == 37 ? -step : e.Key == 39 ? step : 0; n.Y += e.Key == 38 ? -step : e.Key == 40 ? step : 0; })); e.Handled = true;
        }
    }
    protected override void ArrangeChildren(Rect b)
    {
        if (Document != null && bound != Document) { if (bound != null) bound.PreviewInput -= Shortcuts; bound = Document; bound.PreviewInput += Shortcuts; }
        void Place(Element e, float x, float y, float w, float h) { e.Measure(new(w, h)); e.Arrange(new(b.X + x, b.Y + y, w, h)); }
        float left = 232, width = b.Width >= 1280 ? 340 : 310, right = b.Width - width, center = right - left;
        float componentY = b.Height * .40f, projectY = componentY + 226, resourcesY = b.Height - 208;
        Place(fileLabel, left + 16, 22, Math.Max(10, b.Width - left - 384), 24);
        Place(newButton, b.Width - 336, 17, 64, 32); Place(openButton, b.Width - 264, 17, 68, 32); Place(SaveButton, b.Width - 188, 17, 68, 32); Place(exportButton, b.Width - 108, 17, 88, 32);
        Place(UndoButton, left + 12, 76, 60, 28); Place(RedoButton, left + 78, 76, 60, 28);
        Place(fitButton, left + 154, 76, 46, 28); Place(minusButton, left + 206, 76, 34, 28); Place(plusButton, left + 246, 76, 34, 28); Place(snapButton, left + 292, 76, 92, 28); Place(PreviewButton, left + 396, 76, 112, 28);
        Place(Hierarchy, 6, 108, left - 12, componentY - 154);
        Place(upButton, 12, componentY - 38, 36, 28); Place(downButton, 54, componentY - 38, 36, 28); Place(duplicateButton, 98, componentY - 38, 54, 28); Place(deleteButton, 158, componentY - 38, 62, 28);
        Place(ComponentSearch, 12, componentY + 40, left - 24, 30);
        int index = 0;
        foreach (var (button, kind) in palette)
        {
            button.Visible = (button.Text + " " + kind).Contains(ComponentSearch.Text, StringComparison.OrdinalIgnoreCase);
            if (button.Visible) { Place(button, 12 + index % 2 * 110, componentY + 82 + index / 2 * 34, 102, 28); index++; }
        }
        Place(Project, 4, projectY + 38, left - 8, b.Height - projectY - 80);
        Place(Canvas, left + 1, 114, center - 2, resourcesY - 114);
        Place(colorsTab, left + 128, resourcesY + 9, 74, 26); Place(fontsTab, left + 208, resourcesY + 9, 70, 26); Place(commandsTab, left + 284, resourcesY + 9, 106, 26);
        Place(Resources, left + 8, resourcesY + 43, center - 16, 125);
        Place(selectionLabel, right + 16, 114, width - 112, 24); Place(themeButton, b.Width - 88, 112, 72, 28);
        Place(typeLabel, right + 16, 141, width - 32, 20);
        Place(PropertySearch, right + 12, 174, width - 24, 32);
        Place(Inspector, right + 4, 216, width - 8, b.Height - 334);
        Place(exporterButton, right + 12, b.Height - 108, width - 24, 30); Place(pluginButton, right + 12, b.Height - 70, width - 24, 28);
        Place(statusLabel, 16, b.Height - 23, b.Width - 32, 19);
    }
    protected override void Paint(DrawList list, Rect b, float opacity, Theme theme)
    {
        base.Paint(list, b, opacity, theme);
        list.Rectangle(new(b.X, b.Y, b.Width, 65), Color.Hex(0x171F2C));
        list.Rectangle(new(b.X + 18, b.Y + 20, 28, 28), theme.Accent, 7);
        list.Text(new(b.X + 24, b.Y + 25, 22, 20), "y", Color.Hex(0x10221B), 16, true);
        list.Text(new(b.X + 57, b.Y + 25, 172, 24), "YoUI  /  Editor", theme.Text, 16, true);
        float right = b.Width - (b.Width >= 1280 ? 340 : 310), componentY = b.Height * .40f, projectY = componentY + 226, resourcesY = b.Height - 208;
        void Heading(string title, float x, float y, float w)
        {
            list.Rectangle(new(b.X + x, b.Y + y, w, 34), Color.Hex(0x1B2534));
            list.Text(new(b.X + x + 14, b.Y + y + 10, w - 22, 18), title, theme.Text, 11, true);
        }
        Heading("SCENE  /  HIERARCHY", 0, 70, 232);
        Heading("COMPONENTS", 0, componentY, 232);
        Heading("PROJECT", 0, projectY, 232);
        Heading("INSPECTOR", right, 70, b.Width - right);
        list.Rectangle(new(b.X + 233, b.Y + resourcesY, right - 233, 1), theme.Border);
        list.Text(new(b.X + 248, b.Y + resourcesY + 16, 108, 18), "RESOURCES", theme.Text, 11, true);
        list.Rectangle(new(b.X + 232, b.Y + 65, 1, b.Height - 97), theme.Border);
        list.Rectangle(new(b.X + right, b.Y + 65, 1, b.Height - 97), theme.Border);
        list.Rectangle(new(b.X, b.Bottom - 32, b.Width, 1), theme.Border);
    }
}
