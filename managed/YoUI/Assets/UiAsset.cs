using System.Globalization;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace YoUI.Assets;

public enum NodeKind { Canvas, Panel, Text, Button, Toggle, Slider, TextBox, VerticalStack, HorizontalStack }
public enum AnchorMode { TopLeft, Center, Stretch, TopCenter, TopRight, CenterLeft, CenterRight, BottomLeft, BottomCenter, BottomRight }

/// <summary>Versioned authoring data. Never contains GPU resources, resolved bounds, or executable code.</summary>
public sealed class UiNode
{
    [JsonRequired] public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "Node";
    [JsonRequired] public NodeKind Kind { get; set; } = NodeKind.Panel;
    public string Text { get; set; } = "";
    public float X { get; set; }
    public float Y { get; set; }
    public float Width { get; set; } = 160;
    public float Height { get; set; } = 48;
    public AnchorMode Anchor { get; set; }
    // Null preserves the pivot used by version-1 files before independent pivots existed.
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public float? PivotX { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public float? PivotY { get; set; }
    public string Fill { get; set; } = "#00000000";
    public string Foreground { get; set; } = "";
    public float Radius { get; set; } = 8;
    public float FontSize { get; set; } = 16;
    public float Opacity { get; set; } = 1;
    public float Padding { get; set; }
    public float Gap { get; set; } = 12;
    public float Grow { get; set; }
    public bool Bold { get; set; }
    public bool Primary { get; set; }
    public bool Visible { get; set; } = true;
    public bool Enabled { get; set; } = true;
    public bool Clip { get; set; }
    public bool Checked { get; set; }
    public float Value { get; set; } = .5f;
    public string Command { get; set; } = "";
    public List<UiNode> Children { get; set; } = [];
    [JsonIgnore] public bool IsContainer => Kind is NodeKind.Canvas or NodeKind.Panel or NodeKind.VerticalStack or NodeKind.HorizontalStack;
    [JsonIgnore] public bool IsStack => Kind is NodeKind.VerticalStack or NodeKind.HorizontalStack;
    public IEnumerable<UiNode> DescendantsAndSelf() { yield return this; foreach (var c in Children) foreach (var n in c.DescendantsAndSelf()) yield return n; }
    public static Vector2 AnchorPoint(AnchorMode anchor) => anchor switch
    {
        AnchorMode.TopCenter => new(.5f, 0), AnchorMode.TopRight => new(1, 0),
        AnchorMode.CenterLeft => new(0, .5f), AnchorMode.Center => new(.5f), AnchorMode.CenterRight => new(1, .5f),
        AnchorMode.BottomLeft => new(0, 1), AnchorMode.BottomCenter => new(.5f, 1), AnchorMode.BottomRight => Vector2.One,
        _ => Vector2.Zero
    };
    [JsonIgnore] public Vector2 Pivot => new(PivotX ?? AnchorPoint(Anchor).X, PivotY ?? AnchorPoint(Anchor).Y);
    [JsonIgnore] public RectTransform Transform => Anchor == AnchorMode.Stretch
        ? RectTransform.Stretch(X, Y, Width, Height) // width/height are right/bottom insets
        : new(AnchorPoint(Anchor), AnchorPoint(Anchor), Pivot, new(X, Y), new(Width, Height));
}

public sealed class UiAsset
{
    [JsonRequired] public int Version { get; set; } = 1;
    public string Name { get; set; } = "Untitled";
    public bool DarkTheme { get; set; } = true;
    [JsonRequired] public UiNode Root { get; set; } = new() { Name = "Artboard", Kind = NodeKind.Canvas, Width = 640, Height = 480, Fill = "#111827", Clip = true };
}

public static class UiAssetJson
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow, MaxDepth = 96,
        Converters = { new JsonStringEnumConverter(allowIntegerValues: false) }
    };
    public static string Serialize(UiAsset asset)
    {
        Validate(asset); string json = JsonSerializer.Serialize(asset, Options);
        if (json.Length > 4 * 1024 * 1024) throw new InvalidDataException("Serialized UI asset exceeds 4 MiB.");
        return json;
    }
    public static UiAsset Parse(string json)
    {
        if (json.Length > 4 * 1024 * 1024) throw new InvalidDataException("UI asset exceeds 4 MiB.");
        var asset = JsonSerializer.Deserialize<UiAsset>(json, Options) ?? throw new InvalidDataException("Empty UI asset.");
        Validate(asset); return asset;
    }
    public static UiAsset Load(string path)
    {
        if (new FileInfo(path).Length > 4 * 1024 * 1024) throw new InvalidDataException("UI asset exceeds 4 MiB.");
        return Parse(File.ReadAllText(path));
    }
    public static Color ParseColor(string value)
    {
        if (value is null || value.Length is not (7 or 9) || value[0] != '#' || !uint.TryParse(value.AsSpan(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint n)) throw new InvalidDataException("Color must be #RRGGBB or #RRGGBBAA.");
        return value.Length == 7 ? Color.Hex(n) : Color.Hex(n >> 8, (n & 255) / 255f);
    }
    public static void Validate(UiAsset asset)
    {
        if (asset.Version != 1) throw new InvalidDataException($"Unsupported UI asset version {asset.Version}.");
        CheckString(asset.Name, 128);
        if (asset.Root is null || asset.Root.Kind != NodeKind.Canvas) throw new InvalidDataException("Root must be a Canvas.");
        var ids = new HashSet<string>(); int count = 0;
        void Visit(UiNode n, int depth)
        {
            if (n is null || depth > 24 || ++count > 1000) throw new InvalidDataException("UI limit: 1000 nodes, 24 levels.");
            if (!Guid.TryParseExact(n.Id, "N", out _) || !ids.Add(n.Id)) throw new InvalidDataException("Node IDs must be unique GUIDs.");
            if (!Enum.IsDefined(n.Kind) || !Enum.IsDefined(n.Anchor)) throw new InvalidDataException("Unknown node or anchor kind.");
            if (depth > 0 && n.Kind == NodeKind.Canvas) throw new InvalidDataException("Canvas is only allowed at the root.");
            CheckString(n.Name, 128); CheckString(n.Text, 16384); CheckString(n.Command, 128); CheckString(n.Foreground, 9);
            _ = ParseColor(n.Fill); if (n.Foreground.Length != 0) _ = ParseColor(n.Foreground);
            float[] values = [n.X, n.Y, n.Width, n.Height, n.Radius, n.FontSize, n.Opacity, n.Padding, n.Gap, n.Grow, n.Value];
            if (values.Any(v => !float.IsFinite(v) || Math.Abs(v) > 32768) || (n.Anchor != AnchorMode.Stretch && (n.Width < 0 || n.Height < 0)) || n.Radius < 0 || n.FontSize < 1 || n.FontSize > 256 || n.Opacity is < 0 or > 1 || n.Padding < 0 || n.Gap < 0 || n.Grow < 0 || n.Value is < 0 or > 1) throw new InvalidDataException("Property is outside its allowed range.");
            if (new[] { n.PivotX, n.PivotY }.Any(v => v.HasValue && (!float.IsFinite(v.Value) || v.Value is < 0 or > 1))) throw new InvalidDataException("Pivot must be between 0 and 1.");
            if (depth == 0 && (n.Width is < 64 or > 4096 || n.Height is < 64 or > 4096 || n.Anchor != AnchorMode.TopLeft || !n.Visible || !n.Enabled)) throw new InvalidDataException("Artboard must be visible/enabled, TopLeft, 64–4096 pixels.");
            if (n.Children is null || (!n.IsContainer && n.Children.Count != 0)) throw new InvalidDataException("Only containers may have children.");
            foreach (var c in n.Children)
            {
                if (n.IsStack && c != null && (c.Width < 0 || c.Height < 0)) throw new InvalidDataException("Stack children require non-negative dimensions.");
                Visit(c!, depth + 1);
            }
        }
        Visit(asset.Root, 0);
    }
    private static void CheckString(string value, int max) { if (value is null || value.Length > max || value.Contains('\0')) throw new InvalidDataException($"Invalid string (maximum {max} characters)."); }
}

public sealed record UiInstance(Element Root, IReadOnlyDictionary<string, Element> Nodes)
{
    public UiDocument CreateDocument(ITextMeasurer measurer, float width, float height, bool darkTheme = true)
        => new(Root, measurer, width, height) { Theme = darkTheme ? Theme.Dark : Theme.Light };
}

public static class UiAssetRuntime
{
    public static UiInstance Build(UiAsset asset, Action<string>? command = null)
    {
        UiAssetJson.Validate(asset);
        var nodes = new Dictionary<string, Element>();
        Element BuildNode(UiNode n, UiNode? parent)
        {
            Element e = n.Kind switch
            {
                NodeKind.Text => new Label(n.Text) { Name = n.Name, FontSize = n.FontSize, Bold = n.Bold, Color = n.Foreground.Length == 0 ? null : UiAssetJson.ParseColor(n.Foreground) },
                NodeKind.Button => new Button(n.Text) { Name = n.Name, Primary = n.Primary },
                NodeKind.Toggle => new Toggle(n.Text, n.Checked) { Name = n.Name, Primary = n.Primary },
                NodeKind.Slider => new Slider { Name = n.Name, Value = n.Value },
                NodeKind.TextBox => new TextBox { Name = n.Name, Text = n.Text, Placeholder = n.Name },
                NodeKind.VerticalStack or NodeKind.HorizontalStack => new StackPanel { Name = n.Name, Horizontal = n.Kind == NodeKind.HorizontalStack, Gap = n.Gap },
                _ => new Element { Name = n.Name }
            };
            Apply(e, n, parent?.IsStack == true, parent == null);
            if (e is Button button)
            {
                button.Caption.FontSize = n.FontSize; button.Caption.Bold = n.Bold;
                button.TextColor = n.Foreground.Length == 0 ? null : UiAssetJson.ParseColor(n.Foreground);
                if (n.Command.Length != 0) { string name = n.Command; button.Clicked += () => command?.Invoke(name); }
            }
            nodes.Add(n.Id, e);
            foreach (var child in n.Children) e.Add(BuildNode(child, n));
            return e;
        }
        return new(BuildNode(asset.Root, null), nodes);
    }
    /// <summary>Shared by the asset loader and generated C#; layout ownership remains in the UI library.</summary>
    public static void Apply(Element e, UiNode n, bool stackChild, bool root = false)
    {
        e.Background = UiAssetJson.ParseColor(n.Fill); e.Radius = n.Radius; e.Padding = n.Padding;
        e.InheritedOpacity = n.Opacity; e.ClipToBounds = n.Clip; e.Visible = n.Visible; e.Enabled = n.Enabled; e.Grow = n.Grow;
        if (stackChild) { e.Width = n.Width; e.Height = n.Height; }
        else if (!root) e.Transform = n.Transform;
    }
}
