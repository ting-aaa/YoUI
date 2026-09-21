using System.Numerics;
using YoUI;

namespace YoUI.Showcase;

public sealed class Showcase : Element
{
    private readonly Element sidebar, content, inspector, modal;
    private readonly Label title, subtitle, status, resultCount, selectedName;
    private readonly Preview preview;
    public TextBox Search { get; }
    public VirtualList Library { get; }
    public Toggle Motion { get; }
    public Toggle Clip { get; }
    public Slider Opacity { get; }
    public Button ThemeButton { get; }
    public Button LaunchButton { get; }
    public Button ConfirmButton { get; }
    public Button CancelButton { get; }
    public bool ModalOpen => modal.Visible;
    public string Status => status.Text;
    private string[] names = ["Aurora / Atmosphere", "Orbit / Geometry", "Solstice / Gradient", "Lumen / Composition", "Tide / Soft forms", "Nova / Typography"];
    private int[] filtered = Enumerable.Range(0, 100000).ToArray();
    public event Action<bool>? MotionChanged;
    public Showcase()
    {
        sidebar = Add(new Surface { Name = "navigation", Radius = 0 });
        sidebar.Add(new Label("y /  YoUI") { FontSize = 25, Bold = true, Transform = RectTransform.Fixed(26, 30, 164, 44) });
        sidebar.Add(new Label("THE INTERFACE LAB") { FontSize = 10, Color = Color.Hex(0x8192AA), Transform = RectTransform.Fixed(28, 78, 170, 20) });
        var navs = new List<Button>();
        string[] navLabels = ["01   Playground", "02   Components", "03   Performance"];
        for (int i = 0; i < navLabels.Length; i++)
        {
            int mode = i; var nav = sidebar.Add(new Button(navLabels[i]) { Transform = RectTransform.Fixed(16, 142 + i * 54, 178, 42), Selected = i == 0 });
            navs.Add(nav); nav.Clicked += () => { foreach (var n in navs) n.Selected = n == nav; SetMode(mode); };
        }
        sidebar.Add(new Label("WORKSPACE") { FontSize = 10, Color = Color.Hex(0x8192AA), Transform = RectTransform.Fixed(28, 350, 160, 20) });
        sidebar.Add(new Label("Native rendering\nRetained UI\nOne shared canvas") { FontSize = 13, Transform = RectTransform.Fixed(28, 382, 162, 100) });
        sidebar.Add(new Label("RUST  +  C#\nv0.1 / desktop preview") { FontSize = 11, Color = Color.Hex(0x8192AA), Transform = new(new(0, 1), new(0, 1), Vector2.Zero, new(28, -82), new(175, 56)) });
        content = Add(new Element());
        content.Add(new Label("WORKSPACE   /   EXPLORE") { FontSize = 10, Color = Color.Hex(0x8192AA), Transform = RectTransform.Fixed(0, 0, 320, 22) });
        title = content.Add(new Label("Make room for possibility.") { FontSize = 32, Bold = true, Transform = new(Vector2.Zero, new(1, 0), Vector2.Zero, new(0, 33), new(0, 50)) });
        subtitle = content.Add(new Label("A small canvas. A native foundation. A different kind of UI.") { FontSize = 13, Color = Color.Hex(0x8192AA), Transform = new(Vector2.Zero, new(1, 0), Vector2.Zero, new(0, 88), new(0, 38)) });
        preview = content.Add(new Preview { Transform = new(Vector2.Zero, new(1, 0), Vector2.Zero, new(0, 139), new(0, 302)) });
        content.Add(new Label("THE COLLECTION") { FontSize = 11, Bold = true, Transform = RectTransform.Fixed(0, 470, 210, 26) });
        resultCount = content.Add(new Label("100,000 items · recycled rows") { FontSize = 11, Color = Color.Hex(0x8192AA), Transform = new(new(1, 0), new(1, 0), new(1, 0), new(0, 470), new(220, 26)) });
        Search = content.Add(new TextBox { Placeholder = "Search the collection…", AccessibleName = "Search collection", Transform = new(Vector2.Zero, new(1, 0), Vector2.Zero, new(0, 507), new(0, 44)) });
        Library = content.Add(new VirtualList { AccessibleName = "Scene collection", ItemCount = filtered.Length, ItemText = ItemText, Transform = RectTransform.Stretch(0, 569, 0, 52) });
        status = content.Add(new Label("Ready to explore. Select a scene from the collection.") { FontSize = 11, Color = Color.Hex(0x8192AA), Transform = new(new(0, 1), new(1, 1), Vector2.Zero, new(0, -29), new(0, 28)) });
        Search.Changed += query =>
        {
            filtered = Enumerable.Range(0, 100000).Where(i => ItemName(i).Contains(query, StringComparison.OrdinalIgnoreCase)).ToArray();
            Library.ItemCount = filtered.Length; Library.ScrollOffset = 0; Library.Refresh(); resultCount.Text = $"{filtered.Length:N0} items · recycled rows";
        };
        Library.SelectionChanged += OnSelectionChanged;

        inspector = Add(new Surface { Radius = 0 });
        inspector.Add(new Label("CANVAS SETTINGS") { FontSize = 10, Color = Color.Hex(0x8192AA), Transform = RectTransform.Fixed(24, 33, 220, 20) });
        ThemeButton = inspector.Add(new Button("Switch to light") { Transform = RectTransform.Fixed(24, 72, 224, 42) });
        ThemeButton.Clicked += () => { bool dark = Document!.Theme == Theme.Dark; Document.Theme = dark ? Theme.Light : Theme.Dark; ThemeButton.Text = dark ? "Switch to dark" : "Switch to light"; };
        inspector.Add(new Label("A scene, reimagined.") { FontSize = 21, Bold = true, Transform = RectTransform.Fixed(24, 152, 224, 40) });
        inspector.Add(new Label("Tune the rendering properties.\nEvery control shares the same\nlayout, input and drawing system.") { FontSize = 12, Color = Color.Hex(0x8192AA), Transform = RectTransform.Fixed(24, 201, 224, 70) });
        inspector.Add(new Label("ISOLATED OPACITY") { FontSize = 10, Color = Color.Hex(0x8192AA), Transform = RectTransform.Fixed(24, 307, 220, 20) });
        Opacity = inspector.Add(new Slider { Value = 0.86f, AccessibleName = "Scene group opacity", Transform = RectTransform.Fixed(20, 344, 232, 28) });
        Opacity.Changed += value => { preview.GroupAlpha = value; status.Text = $"Group opacity {value:P0} · composed through an offscreen layer"; };
        Motion = inspector.Add(new Toggle("Motion") { Transform = RectTransform.Fixed(24, 402, 224, 42) });
        Clip = inspector.Add(new Toggle("Parent clip", true) { Transform = RectTransform.Fixed(24, 458, 224, 42) });
        Motion.Changed += enabled => MotionChanged?.Invoke(enabled);
        Clip.Changed += enabled => { preview.UseClip = enabled; status.Text = enabled ? "Parent clipping enabled" : "Parent clipping disabled"; };
        inspector.Add(new Label("ACTIVE SCENE") { FontSize = 10, Color = Color.Hex(0x8192AA), Transform = RectTransform.Fixed(24, 548, 220, 20) });
        selectedName = inspector.Add(new Label("Aurora / Atmosphere") { FontSize = 14, Bold = true, Transform = RectTransform.Fixed(24, 580, 224, 48) });
        LaunchButton = inspector.Add(new Button("Use this scene   →") { Primary = true, Transform = new(new(0, 1), new(0, 1), Vector2.Zero, new(24, -100), new(224, 46)) });
        LaunchButton.Clicked += OpenModal;
        inspector.Add(new Label("Tab to navigate · Enter to select") { FontSize = 10, Color = Color.Hex(0x8192AA), Transform = new(new(0, 1), new(0, 1), Vector2.Zero, new(24, -38), new(224, 22)) });

        modal = Add(new Element { Visible = false, HitTestVisible = true, Background = Color.Hex(0x020711, 0.72f) });
        var dialog = modal.Add(new Surface { Radius = 18, Padding = 0, Transform = new(new(0.5f), new(0.5f), new(0.5f), Vector2.Zero, new(430, 252)) });
        dialog.Add(new Label("Your next starting point.") { FontSize = 24, Bold = true, Transform = RectTransform.Fixed(28, 28, 374, 40) });
        dialog.Add(new Label("Apply the selected scene to this workspace?\nYou can keep exploring and switch at any time.") { FontSize = 13, Transform = RectTransform.Fixed(28, 89, 374, 68) });
        CancelButton = dialog.Add(new Button("Keep exploring") { Transform = RectTransform.Fixed(28, 183, 174, 42) });
        ConfirmButton = dialog.Add(new Button("Apply scene") { Primary = true, Transform = RectTransform.Fixed(220, 183, 182, 42) });
        CancelButton.Clicked += CloseModal;
        ConfirmButton.Clicked += () => { status.Text = $"Applied: {selectedName.Text}"; CloseModal(); };
        modal.Event += e => { if (e.Kind == EventKind.KeyDown && e.Key == 27) { CloseModal(); e.Handled = true; } };
    }
    private string ItemName(int index) => names[index % names.Length];
    private void OnSelectionChanged(int index) { if (index < 0) return; int item = filtered[index]; preview.Variant = item % names.Length; selectedName.Text = ItemName(item); status.Text = $"Selected {ItemName(item)} · item {item + 1:N0}"; }
    private string ItemText(int index) { int n = filtered[index]; return $"{n + 1:000000}    {ItemName(n)}"; }
    private void SetMode(int mode)
    {
        title.Text = mode switch { 1 => "Built from a few good pieces.", 2 => "Small work. Smooth frames.", _ => "Make room for possibility." };
        subtitle.Text = mode switch { 1 => "Buttons, text, focus, scrolling and composition — one native canvas.", 2 => $"100,000 data items. {Library.RealizedCount} realized rows. Idle frames sleep.", _ => "A small canvas. A native foundation. A different kind of UI." };
        preview.Variant = mode; status.Text = mode == 2 ? "Performance: inspect artifacts/verification.md for measured results." : "Use the collection and inspector to explore the framework.";
    }
    public void OpenModal() { modal.Visible = true; Document?.SetModal(modal); }
    public void CloseModal() { modal.Visible = false; Document?.SetModal(null); }
    public void Step(float time) { if (Motion.Value) preview.Time = time; }
    protected override void ArrangeChildren(Rect b)
    {
        float side = b.Width >= 1120 ? 210 : 0;
        float right = b.Width >= 930 ? 272 : 244;
        sidebar.Visible = side > 0; sidebar.Arrange(new(0, 0, side, b.Height));
        inspector.Arrange(new(b.Right - right, 0, right, b.Height));
        // Inspector's inner controls retain a comfortable width on compact windows.
        if (right != 272) inspector.Arrange(new(b.Right - 272, 0, 272, b.Height));
        content.Arrange(new(side + 32, 32, Math.Max(220, b.Width - side - 272 - 64), b.Height - 44));
        modal.Arrange(b);
    }
    private sealed class Surface : Element
    {
        protected override void Paint(DrawList list, Rect b, float opacity, Theme theme) => list.Rectangle(b, theme.Surface.Opacity(opacity), Radius);
    }
    private sealed class Preview : Element
    {
        private int variant;
        private float time, alpha = 0.86f;
        private bool clip = true;
        public int Variant { get => variant; set => Set(ref variant, value, DirtyFlags.Paint); }
        public float Time { get => time; set => Set(ref time, value, DirtyFlags.Paint); }
        public float GroupAlpha { get => alpha; set => Set(ref alpha, value, DirtyFlags.Paint); }
        public bool UseClip { get => clip; set => Set(ref clip, value, DirtyFlags.Paint); }
        protected override void Paint(DrawList list, Rect b, float opacity, Theme theme)
        {
            var ink = Color.Hex(0x101D29); var mint = Color.Hex(variant % 3 == 0 ? 0x8EF2CFu : variant % 3 == 1 ? 0xBEA8FFu : 0xFFC48Bu);
            list.Rectangle(b, Color.Hex(0x1F3C48), 18, Color.Hex(0x122333));
            list.PushClip(b.Inset(1));
            for (int x = 22; x < b.Width; x += 24) for (int y = 20; y < b.Height; y += 24) list.Rectangle(new(b.X + x, b.Y + y, 1.5f, 1.5f), Color.Hex(0x79BCA7, 0.17f), 1);
            list.Text(new(b.X + 24, b.Y + 22, 220, 22), "NATIVE CANVAS / 001", Color.Hex(0xACD4CA), 10, true);
            float cx = b.X + b.Width * 0.6f, cy = b.Y + 137;
            float sway = MathF.Sin(time * 1.4f) * 15;
            if (clip) list.PushClip(new(b.X + 19, b.Y + 58, b.Width - 38, 184));
            list.BeginLayer(alpha);
            for (int i = 0; i < 7; i++)
            {
                float size = 194 - i * 23;
                list.Rectangle(new(cx - size / 2 + sway + i * 7, cy - size / 2 + i * 3, size, size), mint.Opacity(0.12f + i * 0.055f), size * 0.33f);
            }
            list.Rectangle(new(cx - 82 + sway, cy - 62, 112, 122), mint, 28, Color.Hex(0x399C91));
            list.Rectangle(new(cx - 62 + sway, cy - 43, 72, 45), ink, 16);
            list.Rectangle(new(cx - 46 + sway, cy - 28, 11, 13), mint, 5);
            list.Rectangle(new(cx - 16 + sway, cy - 28, 11, 13), mint, 5);
            list.Rectangle(new(cx - 59 + sway, cy + 23, 65, 5), Color.Hex(0xE4FFF2, 0.65f), 2.5f);
            list.Rectangle(new(cx - 104 + sway, cy + 8, 18, 55), mint.Opacity(0.7f), 9);
            list.Rectangle(new(cx + 34 + sway, cy - 9, 18, 55), mint.Opacity(0.7f), 9);
            list.Rectangle(new(b.Right - 58, b.Y + 192, 74, 74), mint.Opacity(0.6f), 22);
            list.EndLayer();
            if (clip) list.PopClip();
            list.Text(new(b.X + 24, b.Bottom - 54, b.Width - 40, 29), variant % 3 == 0 ? "Aurora, in good company." : variant % 3 == 1 ? "A little more dimension." : "Warmth in every detail.", Color.Hex(0xE2F6EE), 20, true);
            list.PopClip();
        }
    }
}
