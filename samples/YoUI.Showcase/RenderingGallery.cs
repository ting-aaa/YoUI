using System.Numerics;

namespace YoUI.Showcase;

/// <summary>Application-owned assets. Keep these alive through submission and dispose before the renderer.</summary>
public sealed class GalleryRenderingResources : IDisposable
{
    public NativeImage Texture { get; }
    public NativeImage Border { get; }
    public bool IsDisposed => Texture.IsDisposed || Border.IsDisposed;
    public GalleryRenderingResources(NativeRenderer renderer)
    {
        ArgumentNullException.ThrowIfNull(renderer);
        Texture = renderer.UploadImage(32, 32, Pixels(false));
        try { Border = renderer.UploadImage(32, 32, Pixels(true)); }
        catch { Texture.Dispose(); throw; }
    }
    private static byte[] Pixels(bool border)
    {
        var bytes = new byte[32 * 32 * 4];
        for (int y = 0; y < 32; y++)
            for (int x = 0; x < 32; x++)
            {
                // Distinct corners make image-local UV orientation and fixed nine-slice borders visible.
                uint rgb = y < 16 ? (x < 16 ? 0xF06A52u : 0xF2C85Bu) : (x < 16 ? 0x59BDA4u : 0x688FE5u);
                if (border && x >= 8 && x < 24 && y >= 8 && y < 24) rgb = 0x243C50;
                int offset = (y * 32 + x) * 4;
                bytes[offset] = (byte)(rgb >> 16); bytes[offset + 1] = (byte)(rgb >> 8);
                bytes[offset + 2] = (byte)rgb; bytes[offset + 3] = 255;
            }
        return bytes;
    }
    public void Dispose() { Border.Dispose(); Texture.Dispose(); }
}

/// <summary>A sample painter, not a new framework control. Assets are borrowed explicitly from its host.</summary>
public sealed class RenderingGallery : Element
{
    private GalleryRenderingResources? resources;
    private bool clipped = true;
    private float groupOpacity = .65f;
    public bool Clipped { get => clipped; set { if (clipped == value) return; clipped = value; Invalidate(); } }
    public float GroupOpacity
    {
        get => groupOpacity;
        set
        {
            if (!float.IsFinite(value) || value < 0 || value > 1) throw new ArgumentOutOfRangeException(nameof(value));
            if (groupOpacity == value) return; groupOpacity = value; Invalidate();
        }
    }
    /// <summary>Call before disposing/replacing the host's assets; null enables the GPU-free placeholder.</summary>
    public void SetResources(GalleryRenderingResources? value)
    {
        if (value?.IsDisposed == true) throw new ObjectDisposedException(nameof(value));
        resources = value; Invalidate();
    }
    protected override void Paint(DrawList list, Rect bounds, float opacity, Theme theme)
    {
        list.Rectangle(bounds, theme.Surface.Opacity(opacity), 10);
        if (resources == null || resources.IsDisposed)
        {
            list.Text(new(bounds.X + 16, bounds.Y + 16, bounds.Width - 32, 70),
                "Rendering assets unavailable. The host must bind textures from its renderer.", theme.Text.Opacity(opacity), 14);
            return;
        }
        var tint = new Color(1, 1, 1, opacity);
        float x = bounds.X + 20, y = bounds.Y + 16;
        void Caption(float cy, string text) => list.Text(new(x, cy, bounds.Width - 40, 26), text, theme.Text.Opacity(opacity), 13);
        Caption(y, "Image / full UVs        Textured mesh / rotated UVs");
        y += 34;
        list.Image(new(x, y, 112, 96), resources.Texture, tint);
        list.TexturedMesh([new(x + 158, y + 8), new(x + 292, y), new(x + 276, y + 96), new(x + 148, y + 82)],
            [new(0, 1), new(0, 0), new(1, 0), new(1, 1)], [0, 1, 2, 0, 2, 3], resources.Texture, tint);
        y += 112;
        Caption(y, "Nine-slice / fixed 8 DIP borders, stretched center");
        y += 32;
        list.NineSlice(new(x, y, 132, 64), resources.Border, 8, 8, 8, 8, tint);
        list.NineSlice(new(x + 152, y, Math.Max(32, bounds.Width - 192), 64), resources.Border, 8, 8, 8, 8, tint);
        y += 82;
        Caption(y, "Nested clipping + group opacity / overlap stays uniform");
        y += 34;
        var clip = new Rect(x + 16, y + 8, 268, 86);
        list.Rectangle(new(x, y, 320, 110), Color.Hex(0x102033).Opacity(opacity));
        if (Clipped) list.PushClip(clip);
        list.BeginLayer(GroupOpacity * opacity);
        list.Rectangle(new(x - 8, y + 22, 188, 64), Color.Hex(0xF06A52));
        list.Rectangle(new(x + 116, y + 22, 188, 64), Color.Hex(0xF06A52));
        list.BeginLayer(.5f);
        list.Image(new(x + 238, y - 6, 88, 112), resources.Texture);
        list.EndLayer(); list.EndLayer();
        if (Clipped) list.PopClip();
    }
}
