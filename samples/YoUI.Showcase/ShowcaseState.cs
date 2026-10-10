namespace YoUI.Showcase;

/// <summary>Plain sample state. The view explicitly connects it while attached to a document.</summary>
public sealed class ShowcaseState
{
    private bool motionEnabled;
    private float opacity = 0.86f;

    public event Action<bool>? MotionChanged;
    public event Action<float>? OpacityChanged;

    public bool MotionEnabled
    {
        get => motionEnabled;
        set
        {
            if (motionEnabled == value) return;
            motionEnabled = value;
            MotionChanged?.Invoke(value);
        }
    }

    public float Opacity
    {
        get => opacity;
        set
        {
            value = Math.Clamp(value, 0, 1);
            if (opacity.Equals(value)) return;
            opacity = value;
            OpacityChanged?.Invoke(value);
        }
    }
}
