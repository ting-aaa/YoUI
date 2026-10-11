using System.Globalization;
using System.Numerics;
using YoUI;

namespace YoUI.Components.Sample;

/// <summary>Application-owned model. Raise changes on the same UI thread as the document.</summary>
public sealed class RatingState
{
    private int value;
    public event Action<int>? Changed;
    public int Value
    {
        get => value;
        set { int next = Math.Clamp(value, 0, 5); if (this.value == next) return; this.value = next; Changed?.Invoke(next); }
    }
}

/// <summary>An external-assembly control using only supported public/protected YoUI APIs.</summary>
public sealed class RatingControl : Element
{
    public static readonly UiProperty<RatingControl, int> ValueProperty = new(nameof(Value), 0,
        DirtyFlags.Paint | DirtyFlags.Semantics, normalize: value => Math.Clamp(value, 0, 5),
        changed: (owner, _, value) => owner.Changed?.Invoke(value));
    private int value = ValueProperty.DefaultValue;
    private bool pressed;
    private readonly RatingState state;

    public RatingControl(RatingState state)
    {
        this.state = state ?? throw new ArgumentNullException(nameof(state));
        Focusable = HitTestVisible = true;
        Role = "slider";
        AccessibleName = "Rating";
    }

    public int Value { get => value; set => SetProperty(this, ref this.value, value, ValueProperty); }
    public event Action<int>? Changed;
    public override string SemanticValue => Value.ToString(CultureInfo.InvariantCulture) + " of 5";

    protected override void OnDocumentChanged(UiDocument? previous, UiDocument? current)
    {
        // Removing a control is reusable detachment, not disposal of the application model.
        if (previous != null) { state.Changed -= ReadState; Changed -= WriteState; }
        pressed = false;
        if (current != null)
        {
            ReadState(state.Value); // The model wins after any disconnected edits.
            state.Changed += ReadState;
            Changed += WriteState;
        }
    }
    private void ReadState(int next) => Value = next;
    private void WriteState(int next) => state.Value = next;

    public override Vector2 Measure(Vector2 available)
    {
        DesiredSize = new(float.IsNaN(Width) ? 160 + Padding * 2 : Width,
            float.IsNaN(Height) ? 32 + Padding * 2 : Height);
        return DesiredSize;
    }

    protected override void OnEvent(UiEvent e)
    {
        switch (e.Kind)
        {
            case EventKind.PointerDown:
                pressed = true; e.CapturePointer = true; e.Handled = true; SetPosition(e.Position.X); break;
            case EventKind.PointerMove when pressed:
                e.Handled = true; SetPosition(e.Position.X); break;
            case EventKind.PointerUp:
            case EventKind.PointerCancel:
            case EventKind.Blur:
                pressed = false; break;
            case EventKind.KeyDown when e.Key is 37 or 39 or 36 or 35:
                Value = e.Key switch { 37 => Value - 1, 39 => Value + 1, 36 => 0, _ => 5 };
                e.Handled = true; break;
        }
    }
    private void SetPosition(float x)
    {
        var content = VisualBounds.Inset(Padding);
        if (content.Width > 0) Value = (int)MathF.Round(Math.Clamp((x - content.X) / content.Width, 0, 1) * 5);
    }
    protected override void Paint(DrawList list, Rect bounds, float opacity, Theme theme)
    {
        base.Paint(list, bounds, opacity, theme);
        var content = bounds.Inset(Padding);
        list.Rectangle(content, (IsFocused ? theme.Text : theme.Border).Opacity(opacity), 4);
        list.Rectangle(content with { Width = content.Width * Value / 5f }, theme.Accent.Opacity(opacity), 4);
        // Paint receives visual coordinates and inherited opacity already computed by YoUI.
        // Do not recurse children, reapply VisualOffset or mutate the host-owned command arena.
    }
}
