using System.Numerics;
using System.Text.Json;
using YoUI.Platform.Windows;

namespace YoUI.Showcase;

internal static class BetaRunner
{
    internal static void Run(string[] args, string output)
    {
        if (args.Any(a => a is not ("--beta" or "--headless" or "--smoke")) || args.Contains("--headless") && args.Contains("--smoke")) throw new ArgumentException("Use --beta [--headless | --smoke].");
        using var scene = new BetaScene();
        var checks = new List<string>();
        void Require(bool value, string message) { if (!value) throw new Exception(message); checks.Add(message); }
        void Exercise(UiDocument doc, Action<Element> click, Action<int> key, Action<string> text)
        {
            click(scene.Check); Require(scene.Check.Value, "Checkbox pointer toggles state");
            click(scene.RadioB); Require(scene.RadioB.Value && !scene.RadioA.Value, "Radio group is exclusive");
            click(scene.Combo); Require(scene.Combo.IsOpen, "Combo popup opens");
            key(27); Require(!scene.Combo.IsOpen, "Escape closes popup");
            doc.Focus(scene.Combo); key(40); Require(scene.Combo.SelectedIndex == 1, "Combo keyboard selection changes");
            click(scene.NameInput); doc.Focus(scene.NameInput); doc.Dispatch(new(EventKind.KeyDown) { Key = 65, Control = true }); text("Boreal");
            Require(scene.NameModel.Value == "Boreal", "Text binding updates the model");
            doc.Focus(scene.Amount); scene.Amount.Value = 75; Require(scene.AmountModel.Value == 75 && scene.Progress.Value == .75f, "Numeric binding updates progress");
            click(scene.Submit); Require(scene.Status.Text.Contains("Boreal"), "Command reads edited form");
            scene.Table.Sort(0); scene.Table.Filter = "Ready"; Require(scene.Table.RowCount == 2, "Read-only table filters and sorts");
            scene.Table.Filter = "";
            var retained = scene.Items.ItemViews["Retained views"]; scene.Items.SetItems(["Owned subscriptions", "Retained views", "Stable keys"]);
            Require(ReferenceEquals(retained, scene.Items.ItemViews["Retained views"]), "Keyed items retain views on reorder");
            scene.Tabs.SelectedIndex = 1; doc.BuildFrame(); scene.Tabs.SelectedIndex = 0; doc.BuildFrame(); Require(scene.NameInput.Text == "Boreal", "Tab navigation retains form state");
        }
        if (args.Contains("--headless"))
        {
            using var renderer = new NativeRenderer(1100, 760);
            var doc = new UiDocument(scene, renderer, 1100, 760);
            void Click(Element e) { doc.BuildFrame(); var b = e.VisualBounds; var p = new Vector2(b.X + b.Width / 2, b.Y + b.Height / 2); doc.Dispatch(new(EventKind.PointerDown) { Position = p }); doc.Dispatch(new(EventKind.PointerUp) { Position = p }); }
            void Save(string name) { renderer.Render(doc.BuildFrame()); renderer.SavePng(Path.Combine(output, name)); }
            Save("beta-dark.png");
            Exercise(doc, Click, key => doc.Dispatch(new(EventKind.KeyDown) { Key = key }), text => doc.Dispatch(new(EventKind.TextInput) { Text = text }));
            scene.Combo.SetOpen(true); Save("beta-popup.png"); scene.Combo.SetOpen(false);
            scene.Tabs.SelectedIndex = 1; Save("beta-layout.png"); scene.Tabs.SelectedIndex = 0;
            doc.Theme = Theme.Light; Save("beta-light.png");
            doc.Resize(640, 680); renderer.Resize(640, 680); doc.BuildFrame(); scene.FormScroll.ScrollIntoView(scene.Submit); doc.BuildFrame();
            Require(scene.Submit.VisualBounds.Bottom <= scene.FormScroll.VisualBounds.Bottom, "Compact form scroll reveals Apply"); Save("beta-compact.png");
            File.WriteAllText(Path.Combine(output, "beta-headless.json"), JsonSerializer.Serialize(new { adapter = renderer.Adapter, checks, nativeWindowTested = false, input = "UiDocument.Dispatch", sizes = new[] { "1100x760", "640x680" } }, new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine($"{renderer.Adapter}\n{checks.Count} Beta routed checks passed; {output}");
            return;
        }
        using var window = new DesktopWindow("YoUI — Beta components", scene, 1100, 760);
        if (!args.Contains("--smoke")) { window.Run(); return; }
        window.RenderNow();
        void NativeClick(Element e)
        {
            window.RenderNow(); var b = e.VisualBounds; int x = (int)((b.X + b.Width / 2) * window.Scale), y = (int)((b.Y + b.Height / 2) * window.Scale); nint p = (nint)((y << 16) | (x & 0xffff));
            SmokeNative.PostMessage(window.Handle, 0x0200, 0, p); SmokeNative.PostMessage(window.Handle, 0x0201, 1, p); SmokeNative.PostMessage(window.Handle, 0x0202, 0, p); window.PumpEvents(); window.RenderNow();
        }
        void NativeKey(int key) { SmokeNative.PostMessage(window.Handle, 0x0100, (nuint)key, 0); window.PumpEvents(); window.RenderNow(); }
        void NativeText(string text) { foreach (char c in text) SmokeNative.PostMessage(window.Handle, 0x0102, c, 0); window.PumpEvents(); window.RenderNow(); }
        Exercise(window.Document, NativeClick, NativeKey, NativeText);
        window.Renderer.SavePng(Path.Combine(output, "beta-window.png"));
        SmokeNative.SetWindowPos(window.Handle, 0, 0, 0, 1000, 900, 0x0006); window.PumpEvents(); window.RenderNow();
        Require(scene.Bounds.Width < 1100, "Native window resize updates Beta layout");
        scene.FormScroll.ScrollIntoView(scene.Submit); window.RenderNow();
        Require(scene.Submit.VisualBounds.Bottom <= scene.FormScroll.VisualBounds.Bottom, "Native compact form keeps Apply reachable");
        window.Renderer.SavePng(Path.Combine(output, "beta-window-compact.png"));
        File.WriteAllText(Path.Combine(output, "beta-window.json"), JsonSerializer.Serialize(new { adapter = window.Renderer.Adapter, checks, scale = window.Scale, input = "Win32 PostMessage plus explicit model/focus updates", physicalInputTested = false }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"{window.Renderer.Adapter}\n{checks.Count} Beta native-message/mixed checks passed; {output}");
    }
}

internal sealed class BetaScene : Element, IDisposable
{
    private readonly BindingScope bindings = new();
    private readonly Label heading, subtitle;
    private readonly Grid form;
    public ScrollView FormScroll { get; }
    public TextBox NameInput { get; } = new() { Text = "Aurora", Placeholder = "Project name" };
    public ObservableValue<string> NameModel { get; } = new("Aurora");
    public ObservableValue<float> AmountModel { get; } = new(35);
    public CheckBox Check { get; } = new("Enable notifications");
    public RadioButton RadioA { get; }
    public RadioButton RadioB { get; }
    public ComboBox Combo { get; } = new();
    public NumericUpDown Amount { get; } = new() { Maximum = 100, Step = 5, Value = 35 };
    public ProgressBar Progress { get; } = new() { Value = .35f };
    public Button Submit { get; } = new("Apply settings") { Primary = true };
    public Label Status { get; } = new("Ready to configure your workspace.") { FontSize = 13 };
    public DataGrid Table { get; } = new();
    public TabControl Tabs { get; }
    public ItemsControl<string> Items { get; } = new() { Key = value => value, ItemTemplate = value => new Label(value) { FontSize = 16, Height = 32 }, Gap = 10, Padding = 12 };
    public BetaScene()
    {
        heading = Add(new Label("Build with more possibilities.") { FontSize = 28, Bold = true });
        subtitle = Add(new Label("YOUI BETA   /   COMPONENTS · COMPOSITION · BINDING") { FontSize = 12 });
        var radio = new RadioGroup(); RadioA = new("Personal", radio) { Value = true }; RadioB = new("Team", radio);
        Tabs = Add(new TabControl());
        form = new Grid { Height = 500, Rows = [GridTrack.Fixed(48), GridTrack.Fixed(44), GridTrack.Fixed(44), GridTrack.Fixed(44), GridTrack.Fixed(44), GridTrack.Fixed(30), GridTrack.Fixed(44), GridTrack.Star()], Columns = [GridTrack.Fixed(118), GridTrack.Star()], Gap = 12, Padding = 20, Radius = 14 };
        void Row(int row, string title, Element value) { var label = form.Add(new Label(title) { FontSize = 13, Height = 24 }); form.SetCell(label, row, 0); form.Add(value); form.SetCell(value, row, 1); }
        Row(0, "Name", NameInput); Row(1, "Workspace", Combo); Combo.SetItems(["Design", "Development", "Review"]);
        var radios = new StackPanel { Horizontal = true, Gap = 8 }; radios.Add(RadioA); radios.Add(RadioB); Row(2, "Account", radios);
        Row(3, "Preferences", Check); Row(4, "Capacity", Amount); Row(5, "Progress", Progress);
        var buttonContent = new StackPanel { Horizontal = true, Gap = 6 }; buttonContent.Add(new Label("✓") { Width = 22, Height = 24 }); buttonContent.Add(new Label("Apply settings") { Width = 160, Height = 24 });
        Submit.Content = new ContentPresenter { Content = buttonContent };
        form.Add(Submit); form.SetCell(Submit, 6, 1); form.Add(Status); form.SetCell(Status, 7, 0, 1, 2);
        FormScroll = new ScrollView(form); Tabs.AddPage("Form & controls", FormScroll);
        var layout = new Grid { Rows = [GridTrack.Fixed(90), GridTrack.Fixed(1), GridTrack.Star()], Columns = [GridTrack.Star(), GridTrack.Star(2)], Gap = 12, Padding = 16 };
        var content = layout.Add(new ContentControl { Background = Color.Hex(0x22344B), Padding = 12, Content = new ContentPresenter { Content = new Label("A replaceable content slot") { FontSize = 20 } } }); layout.SetCell(content, 0, 0, 1, 2);
        var separator = layout.Add(new Separator()); layout.SetCell(separator, 1, 0, 1, 2);
        Items.SetItems(["Stable keys", "Retained views", "Owned subscriptions"]); layout.Add(Items); layout.SetCell(Items, 2, 0);
        var wrap = layout.Add(new WrapPanel { Gap = 10, Padding = 12 }); layout.SetCell(wrap, 2, 1);
        foreach (var name in new[] { "Auto", "Fixed", "Percent", "Grid", "Wrap", "Content", "Items", "Commands" }) wrap.Add(new Button(name) { Width = 128, Height = 42 });
        Tabs.AddPage("Layout & composition", layout);
        Add(Table); Table.SetData([new("Project", 150), new("Status", 120)], new[] { ("a", new[] { "Aurora", "Ready" }), ("b", new[] { "Boreal", "Running" }), ("c", new[] { "Cinder", "Ready" }) });
        var command = new UiCommand(_ => Status.Text = $"Applied: {NameModel.Value} · {AmountModel.Value:0}%", _ => !string.IsNullOrWhiteSpace(NameInput.Text));
        bindings.BindText(NameInput, NameModel, value => string.IsNullOrWhiteSpace(value) ? "Enter a project name." : null, error => { Status.Text = error ?? "Ready to apply."; command.Refresh(); });
        bindings.BindNumber(Amount, AmountModel); bindings.Bind(AmountModel, v => Progress.Value = v / 100); bindings.BindCommand(Submit, command);
    }
    protected override void Paint(DrawList list, Rect bounds, float opacity, Theme theme)
    {
        form.Background = theme.Surface;
        if (Submit.Content is ContentPresenter { Content: StackPanel row }) foreach (var label in row.Children.OfType<Label>()) label.Color = theme.OnAccent;
        base.Paint(list, bounds, opacity, theme);
    }
    protected override void ArrangeChildren(Rect b)
    {
        void Place(Element e, Rect bounds) { e.Measure(new(bounds.Width, bounds.Height)); e.Arrange(bounds); }
        Place(subtitle, new(b.X + 24, b.Y + 24, Math.Max(0, b.Width - 48), 22));
        Place(heading, new(b.X + 24, b.Y + 58, Math.Max(0, b.Width - 48), 44));
        bool compact = b.Width < 850;
        Place(Tabs, new(b.X + 24, b.Y + 126, Math.Max(0, b.Width - (compact ? 48 : 386)), Math.Max(0, b.Height - (compact ? 310 : 156))));
        Place(Table, compact ? new(b.X + 24, b.Bottom - 166, b.Width - 48, 140) : new(b.Right - 338, b.Y + 170, 314, b.Height - 200));
    }
    public void Dispose() => bindings.Dispose();
}
