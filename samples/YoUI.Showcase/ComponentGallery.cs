using System.Globalization;
using System.Numerics;
using YoUI.Components.Sample;

namespace YoUI.Showcase;

/// <summary>A small application composed entirely from existing public controls.</summary>
public sealed class ComponentGallery : Element
{
    private readonly Element host, modal, dialog;
    private readonly Element[] pages;
    private readonly Label ratingValue, selection, status;
    private readonly string[] records = Enumerable.Range(0, 240).Select(i => $"{i + 1:000} / {new[] { "Aurora", "Orbit", "Lumen", "Tide" }[i % 4]}").ToArray();
    private int[] filtered = Enumerable.Range(0, 240).ToArray();
    private int selectedRecord = -1;
    public RatingState State { get; }
    public RatingControl Rating { get; }
    public Button[] Navigation { get; }
    public Button ThemeButton { get; }
    public Button ResetButton { get; }
    public Button CancelButton { get; }
    public Button ConfirmButton { get; }
    public Button DisabledButton { get; }
    public Slider Opacity { get; }
    public Toggle ShowPreview { get; }
    public Element Preview { get; }
    public TextBox Search { get; }
    public VirtualList Items { get; }
    public PropertyGrid Properties { get; }
    public StackPanel LayoutPreview { get; }
    public RenderingGallery Rendering { get; }
    public Toggle RenderingClip { get; }
    public Slider RenderingOpacity { get; }
    public int Page { get; private set; } = -1;
    public bool ModalOpen => modal.Visible;
    public int SelectedRecord => selectedRecord;
    public string SelectionText => selection.Text;
    public string Status => status.Text;

    public ComponentGallery(RatingState? state = null)
    {
        State = state ?? new RatingState { Value = 3 };
        ClipToBounds = true;
        Add(new Label("YoUI / Component gallery") { Bold = true, FontSize = 25, Transform = RectTransform.Fixed(24, 18, 560, 40) });
        Add(new Label("Live controls. Shared input, layout and model contracts.") { FontSize = 13, Transform = RectTransform.Fixed(24, 62, 560, 28) });
        Navigation = [new("Controls"), new("Collection"), new("Layout"), new("Rendering")];
        for (int i = 0; i < Navigation.Length; i++)
        {
            int page = i; Add(Navigation[i]); Navigation[i].Clicked += () => SelectPage(page);
        }
        ThemeButton = Add(new Button("Light theme"));
        ThemeButton.Clicked += () =>
        {
            if (Document == null) return;
            Document.Theme = Document.Theme == Theme.Dark ? Theme.Light : Theme.Dark;
            ThemeButton.Text = Document.Theme == Theme.Dark ? "Light theme" : "Dark theme";
        };
        ResetButton = Add(new Button("Reset")); ResetButton.Clicked += OpenReset;
        host = Add(new Element { ClipToBounds = true });
        status = Add(new Label("Tab / Shift+Tab to navigate; Enter or Space to activate.") { FontSize = 12 });

        var controls = new StackPanel { Gap = 14, Padding = 16 };
        controls.Add(new Label("01 / Controls and states") { Bold = true, FontSize = 22 });
        controls.Add(new Label("Try pointer, Tab and arrow keys. Disabled controls skip focus.") { FontSize = 13 });
        var buttons = controls.Add(new StackPanel { Horizontal = true, Gap = 12, Height = 42 });
        var normal = buttons.Add(new Button("Normal") { Width = 132 });
        normal.Clicked += () => status.Text = "Normal button activated.";
        var primary = buttons.Add(new Button("Primary") { Width = 132, Primary = true });
        primary.Clicked += OpenReset;
        DisabledButton = buttons.Add(new Button("Disabled") { Width = 132, Enabled = false });
        ShowPreview = controls.Add(new Toggle("Preview visible", true) { Primary = true });
        controls.Add(new Label("Slider / composed opacity (0–100%)") { FontSize = 13 });
        Opacity = controls.Add(new Slider { Value = .8f, AccessibleName = "Preview opacity" });
        Preview = controls.Add(new Element { Height = 68, Background = Color.Hex(0x397E73), Radius = 12, CompositedOpacity = .8f, ClipToBounds = true });
        Preview.Add(new Label("A clipped, composited surface") { FontSize = 18, Color = Color.Hex(0xFFFFFF), Transform = RectTransform.Stretch(16, 16, 16, 10) });
        ShowPreview.Changed += _ => { Preview.Visible = ShowPreview.Value; ShowPreview.Primary = ShowPreview.Value; };
        Opacity.Changed += _ => { Preview.CompositedOpacity = Opacity.Value; status.Text = $"Preview opacity {Opacity.Value:P0}"; };
        controls.Add(new Label("External component / two-way application model") { FontSize = 13 });
        Rating = controls.Add(new RatingControl(State) { Height = 36, AccessibleName = "Demo rating" });
        ratingValue = controls.Add(new Label { FontSize = 13 });
        var setModel = controls.Add(new Button("Set model to 5") { Width = 180 });
        setModel.Clicked += () => State.Value = 5;
        controls.Add(new Label("Rating: Left / Right / Home / End. Drag outside, then cancel safely.") { FontSize = 12 });

        var collection = new StackPanel { Gap = 12, Padding = 16 };
        collection.Add(new Label("02 / Search and selection") { Bold = true, FontSize = 22 });
        collection.Add(new Label("240 deterministic records; selection follows record identity after filtering.") { FontSize = 13 });
        Search = collection.Add(new TextBox { Placeholder = "Filter Aurora, Orbit, Lumen or Tide…", AccessibleName = "Filter gallery records" });
        selection = collection.Add(new Label("No record selected.") { FontSize = 13 });
        Items = collection.Add(new VirtualList { ItemCount = records.Length, ItemText = i => records[filtered[i]], Height = 272, AccessibleName = "Gallery records" });
        Search.Changed += _ => FilterRecords();
        Items.SelectionChanged += _ => UpdateSelection();
        collection.Add(new Label("Up / Down / Home / End select and scroll. Empty results clear selection.") { FontSize = 12 });

        var layout = new StackPanel { Gap = 12, Padding = 16 };
        layout.Add(new Label("03 / Layout and property editing") { Bold = true, FontSize = 22 });
        layout.Add(new Label("StackPanel owns positions. RectTransform belongs to the free-layout surface below.") { FontSize = 13 });
        LayoutPreview = layout.Add(new StackPanel { Horizontal = true, Gap = 12, Padding = 12, Height = 82, Background = Color.Hex(0x243C50), ClipToBounds = true });
        LayoutPreview.Add(new Button("Fixed 120") { Width = 120, Height = 42 });
        LayoutPreview.Add(new Button("Grow = 1") { Width = 80, Grow = 1, Height = 42, Primary = true });
        var anchored = layout.Add(new Element { Height = 72, Background = Color.Hex(0x243C50), ClipToBounds = true });
        anchored.Add(new Button("Centered anchor") { Transform = new(new(.5f), new(.5f), new(.5f), Vector2.Zero, new(210, 42)) });
        var propertyViewport = layout.Add(new Element { Height = 230 });
        Properties = propertyViewport.Add(new PropertyGrid { AccessibleName = "Layout properties" });
        Properties.SetEntries([
            new PropertyEntry("Gap", () => LayoutPreview.Gap.ToString(CultureInfo.InvariantCulture), value => LayoutPreview.Gap = ParseSpacing(value)) { Group = "Layout", Hint = "0..32 DIP" },
            new PropertyEntry("Padding", () => LayoutPreview.Padding.ToString(CultureInfo.InvariantCulture), value => LayoutPreview.Padding = ParseSpacing(value)) { Group = "Layout", Hint = "0..32 DIP" }
        ]);
        Properties.ValidationFailed += message => status.Text = message;
        layout.Add(new Label("Edit 0..32 DIP; Enter or blur commits. Invalid values leave the preview unchanged.") { FontSize = 12 });
        var rendering = new StackPanel { Gap = 12, Padding = 16 };
        rendering.Add(new Label("04 / Rendering foundations") { Bold = true, FontSize = 22 });
        rendering.Add(new Label("Generated textures. Ordered triangles. Nested clips and isolated groups.") { FontSize = 13 });
        RenderingClip = rendering.Add(new Toggle("Clip the overlapping group", true) { Primary = true });
        rendering.Add(new Label("Group opacity / arrows or drag (0–100%)") { FontSize = 13 });
        RenderingOpacity = rendering.Add(new Slider { Value = .65f, AccessibleName = "Rendering group opacity" });
        Rendering = rendering.Add(new RenderingGallery { Height = 460 });
        RenderingClip.Changed += _ => { Rendering.Clipped = RenderingClip.Value; RenderingClip.Primary = RenderingClip.Value; };
        RenderingOpacity.Changed += _ => Rendering.GroupOpacity = RenderingOpacity.Value;
        rendering.Add(new Label("The host owns GPU images; page navigation keeps them alive.") { FontSize = 12 });
        pages = [Viewport(controls), Viewport(collection), Viewport(layout), Viewport(rendering)];

        modal = Add(new Element { Visible = false, HitTestVisible = true, Background = Color.Hex(0x020711, .75f) });
        dialog = modal.Add(new Panel { Radius = 16 });
        dialog.Add(new Label("Reset the demos?") { FontSize = 23, Bold = true, Transform = RectTransform.Fixed(24, 22, 380, 38) });
        dialog.Add(new Label("Restore controls, rendering, filter and spacing.\nCancel keeps your current values.") { FontSize = 13, Transform = RectTransform.Fixed(24, 76, 380, 62) });
        CancelButton = dialog.Add(new Button("Cancel") { Transform = RectTransform.Fixed(24, 158, 176, 42) });
        ConfirmButton = dialog.Add(new Button("Reset") { Primary = true, Transform = RectTransform.Fixed(216, 158, 176, 42) });
        CancelButton.Clicked += CloseReset;
        ConfirmButton.Clicked += () => { Reset(); CloseReset(); };
        modal.Event += e =>
        {
            // A backdrop pointer-down normally clears document focus. Keep Escape reachable.
            if (e.Kind == EventKind.PointerDown && e.Phase == EventPhase.Target)
            {
                Document?.Focus(CancelButton); e.Handled = true;
            }
            if (e.Kind == EventKind.KeyDown && e.Key == 27) { CloseReset(); e.Handled = true; }
        };
        SelectPage(0);
    }
    private static float ParseSpacing(string value)
    {
        float result = float.Parse(value, NumberStyles.Float, CultureInfo.InvariantCulture);
        if (!float.IsFinite(result) || result < 0 || result > 32) throw new ArgumentOutOfRangeException(nameof(value), "Use 0..32 DIP.");
        return result;
    }
    private static ScrollView Viewport(Element content)
    {
        var viewport = new ScrollView(content);
        // Keyboard focus must reveal children below the compact viewport.
        void Wire(Element node)
        {
            node.Event += e => { if (e.Kind == EventKind.Focus) viewport.ScrollIntoView(node); };
            foreach (var child in node.Children) Wire(child);
        }
        Wire(content);
        return viewport;
    }
    protected override void OnDocumentChanged(UiDocument? previous, UiDocument? current)
    {
        // A child synchronizing its model can detach/reparent this gallery before this hook.
        if (!ReferenceEquals(Document, current)) return;
        if (previous != null) State.Changed -= UpdateRating;
        if (current != null) { State.Changed += UpdateRating; UpdateRating(State.Value); }
        else modal.Visible = false;
    }
    private void UpdateRating(int _) => ratingValue.Text = $"Model: {State.Value} / 5 · control reattaches from the current model.";
    public void SelectPage(int page)
    {
        if ((uint)page >= pages.Length) throw new ArgumentOutOfRangeException(nameof(page));
        if (Page == page) return;
        CloseReset(); Document?.CancelPointer(); host.Clear();
        Page = page; host.Add(pages[page]);
        for (int i = 0; i < Navigation.Length; i++)
        {
            Navigation[i].Selected = i == page;
            Navigation[i].Primary = i == page;
        }
        Document?.Focus(Navigation[page]);
    }
    private void FilterRecords()
    {
        filtered = Enumerable.Range(0, records.Length).Where(i => records[i].Contains(Search.Text, StringComparison.OrdinalIgnoreCase)).ToArray();
        int next = Array.IndexOf(filtered, selectedRecord);
        Items.ItemCount = filtered.Length; Items.SelectedIndex = next; Items.ScrollOffset = 0; Items.Refresh();
        UpdateSelection();
    }
    private void UpdateSelection()
    {
        selectedRecord = Items.SelectedIndex >= 0 && Items.SelectedIndex < filtered.Length ? filtered[Items.SelectedIndex] : -1;
        selection.Text = selectedRecord < 0 ? $"No record selected. {filtered.Length} results." : $"Selected {records[selectedRecord]} · {filtered.Length} results";
    }
    public void OpenReset()
    {
        if (Document == null || ModalOpen) return;
        modal.Visible = true; Document.SetModal(modal);
    }
    public void CloseReset()
    {
        if (!ModalOpen) return;
        modal.Visible = false; Document?.SetModal(null);
    }
    private void Reset()
    {
        State.Value = 3; Opacity.Value = .8f; ShowPreview.Value = true;
        selectedRecord = -1; Search.Text = ""; FilterRecords();
        LayoutPreview.Gap = LayoutPreview.Padding = 12; Properties.RefreshValues();
        RenderingClip.Value = true; RenderingOpacity.Value = .65f;
        status.Text = "Demos reset.";
    }
    protected override void ArrangeChildren(Rect bounds)
    {
        // Supported gallery viewport: at least 640 x 600 DIP. Pages scroll vertically.
        foreach (var child in Children.Where(child => child.Transform != null))
        {
            var rect = child.Transform!.Value.Resolve(bounds);
            child.Measure(new(rect.Width, rect.Height)); child.Arrange(rect);
        }
        float gap = 8, width = (bounds.Width - 48 - gap * (Navigation.Length - 1)) / Navigation.Length;
        for (int i = 0; i < Navigation.Length; i++) Navigation[i].Arrange(new(bounds.X + 24 + i * (width + gap), bounds.Y + 106, width, 42));
        ThemeButton.Arrange(new(bounds.X + 24, bounds.Y + 156, 142, 36));
        ResetButton.Arrange(new(bounds.X + 174, bounds.Y + 156, 142, 36));
        host.Arrange(new(bounds.X + 24, bounds.Y + 210, Math.Max(0, bounds.Width - 48), Math.Max(0, bounds.Height - 260)));
        status.Arrange(new(bounds.X + 24, bounds.Bottom - 40, Math.Max(0, bounds.Width - 48), 32));
        modal.Arrange(bounds);
        dialog.Arrange(new(bounds.X + (bounds.Width - 416) / 2, bounds.Y + (bounds.Height - 222) / 2, 416, 222));
    }
    private sealed class Panel : Element
    {
        protected override void Paint(DrawList list, Rect bounds, float opacity, Theme theme) => list.Rectangle(bounds, theme.Surface.Opacity(opacity), Radius);
    }
}
