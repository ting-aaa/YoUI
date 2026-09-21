using System.Numerics;
using System.Runtime.InteropServices;

namespace YoUI;

[StructLayout(LayoutKind.Sequential)]
public readonly record struct Rect(float X, float Y, float Width, float Height)
{
    public float Right => X + Width;
    public float Bottom => Y + Height;
    public bool Contains(Vector2 p) => p.X >= X && p.Y >= Y && p.X < Right && p.Y < Bottom;
    public Rect Intersect(Rect b) => new(Math.Max(X, b.X), Math.Max(Y, b.Y), Math.Max(0, Math.Min(Right, b.Right) - Math.Max(X, b.X)), Math.Max(0, Math.Min(Bottom, b.Bottom) - Math.Max(Y, b.Y)));
    public Rect Inset(float p) => new(X + p, Y + p, Math.Max(0, Width - p * 2), Math.Max(0, Height - p * 2));
    public Rect Translate(Vector2 v) => this with { X = X + v.X, Y = Y + v.Y };
}

[StructLayout(LayoutKind.Sequential)]
public readonly record struct Color(float R, float G, float B, float A = 1)
{
    public static Color Hex(uint rgb, float alpha = 1) => new(((rgb >> 16) & 255) / 255f, ((rgb >> 8) & 255) / 255f, (rgb & 255) / 255f, alpha);
    public Color Opacity(float value) => this with { A = A * value };
    public static Color Transparent => new(0, 0, 0, 0);
}

/// <summary>Layout inputs; the resolved rectangle is the single authoritative result.
/// Coordinates use top-left origin, +Y down in both managed and native APIs.</summary>
public readonly record struct RectTransform(Vector2 AnchorMin, Vector2 AnchorMax, Vector2 Pivot, Vector2 Position, Vector2 SizeDelta)
{
    public Rect Resolve(Rect parent)
    {
        var extent = new Vector2(parent.Width, parent.Height);
        var min = new Vector2(parent.X, parent.Y) + extent * AnchorMin;
        var span = extent * (AnchorMax - AnchorMin);
        var size = Vector2.Max(Vector2.Zero, span + SizeDelta);
        var origin = min + span * Pivot + Position - size * Pivot;
        return new(origin.X, origin.Y, size.X, size.Y);
    }
    public static RectTransform Fixed(float x, float y, float w, float h) => new(Vector2.Zero, Vector2.Zero, Vector2.Zero, new(x, y), new(w, h));
    public static RectTransform Stretch(float left = 0, float top = 0, float right = 0, float bottom = 0) => new(Vector2.Zero, Vector2.One, Vector2.Zero, new(left, top), new(-left - right, -top - bottom));
}

public sealed record Theme(Color Background, Color Surface, Color SurfaceRaised, Color Text, Color Muted, Color Accent, Color Border)
{
    public Color OnAccent => Accent.R * .2126f + Accent.G * .7152f + Accent.B * .0722f > .6f ? Color.Hex(0x0B211B) : Color.Hex(0xFFFFFF);
    public static Theme Dark { get; } = new(Color.Hex(0x0B1019), Color.Hex(0x121B29), Color.Hex(0x1B293B), Color.Hex(0xEDF3FA), Color.Hex(0x8A9AAF), Color.Hex(0x76E5C0), Color.Hex(0x2A384B));
    public static Theme Light { get; } = new(Color.Hex(0xF1F4F8), Color.Hex(0xFFFFFF), Color.Hex(0xE6ECF3), Color.Hex(0x122238), Color.Hex(0x576C83), Color.Hex(0x167C63), Color.Hex(0xCFD8E4));
}
