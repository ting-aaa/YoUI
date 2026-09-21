using System.Numerics;
using YoUI.Assets;

namespace YoUI.Editor.App;

/// <summary>Parent anchor and local pivot are deliberately separate spatial controls.</summary>
public sealed class LayoutReferences : Element, IPropertyEditor
{
    private readonly ReferencePicker anchor, pivot;
    private readonly Label anchorLabel, pivotLabel;
    public LayoutReferences(Func<UiNode> read, Action<AnchorMode> setAnchor, Action<Vector2> setPivot)
    {
        Height = 184;
        AnchorMode[] modes = [AnchorMode.TopLeft, AnchorMode.TopCenter, AnchorMode.TopRight, AnchorMode.CenterLeft, AnchorMode.Center, AnchorMode.CenterRight, AnchorMode.BottomLeft, AnchorMode.BottomCenter, AnchorMode.BottomRight, AnchorMode.Stretch];
        anchorLabel = Add(new Label("Parent anchor") { FontSize = 11, Color = Theme.Dark.Muted });
        pivotLabel = Add(new Label("Local pivot") { FontSize = 11, Color = Theme.Dark.Muted });
        anchor = Add(new ReferencePicker(() => Array.IndexOf(modes, read().Anchor), i => setAnchor(modes[i]), true));
        pivot = Add(new ReferencePicker(() => { var p = read().Pivot; return p.X * 2 == MathF.Round(p.X * 2) && p.Y * 2 == MathF.Round(p.Y * 2) ? (int)(p.Y * 2) * 3 + (int)(p.X * 2) : -1; }, i => setPivot(new(i % 3 * .5f, i / 3 * .5f))) { Enabled = read().Anchor != AnchorMode.Stretch });
    }
    public void RefreshValue() { anchor.RefreshValue(); pivot.RefreshValue(); }
    public override Vector2 Measure(Vector2 available) { DesiredSize = new(available.X, Height); return DesiredSize; }
    protected override void ArrangeChildren(Rect b)
    {
        float half = b.Width / 2;
        anchorLabel.Arrange(new(b.X, b.Y, half, 18)); pivotLabel.Arrange(new(b.X + half, b.Y, half, 18));
        anchor.Arrange(new(b.X, b.Y + 24, half - 4, 160)); pivot.Arrange(new(b.X + half, b.Y + 24, half - 4, 160));
    }
}
