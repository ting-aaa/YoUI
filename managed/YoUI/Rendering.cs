using System.Runtime.InteropServices;
using System.Text;

namespace YoUI;

[StructLayout(LayoutKind.Sequential)]
public struct DrawCommand
{
    public uint Kind, Flags, TextOffset, TextLength;
    public Rect Rect;
    public Color Color, Color2;
    public float Radius, FontSize, Reserved0, Reserved1;
}

[StructLayout(LayoutKind.Sequential)]
public readonly struct FrameStats
{
    public readonly uint Commands, Instances, DrawCalls, Layers;
    public readonly ulong UploadBytes, PrepareMicroseconds, SubmitMicroseconds;
    public readonly uint AtlasGlyphs, TextCacheHits;
    public override string ToString() => $"{Commands} commands · {Instances} quads · {DrawCalls} draws · CPU prepare {PrepareMicroseconds / 1000.0:F2} ms";
}

/// <summary>Reusable command and UTF-8 arenas, pinned only during synchronous submission.</summary>
public sealed class DrawList
{
    private readonly List<DrawCommand> commands = [];
    private byte[] text = new byte[4096];
    private int textLength;
    public ReadOnlySpan<DrawCommand> Commands => CollectionsMarshal.AsSpan(commands);
    public ReadOnlySpan<byte> TextBytes => text.AsSpan(0, textLength);
    public void Clear() { commands.Clear(); textLength = 0; }
    public void Rectangle(Rect rect, Color color, float radius = 0, Color? gradientEnd = null) => commands.Add(new() { Rect = rect, Color = color, Color2 = gradientEnd ?? color, Radius = radius });
    public void Text(Rect rect, string value, Color color, float size = 14, bool bold = false)
    {
        int count = Encoding.UTF8.GetByteCount(value);
        if (textLength + count > text.Length) Array.Resize(ref text, Math.Max(text.Length * 2, textLength + count));
        Encoding.UTF8.GetBytes(value, text.AsSpan(textLength, count));
        commands.Add(new() { Kind = 1, Flags = bold ? 1u : 0, Rect = rect, Color = color, Color2 = color, FontSize = size, TextOffset = (uint)textLength, TextLength = (uint)count });
        textLength += count;
    }
    public void PushClip(Rect rect) => commands.Add(new() { Kind = 2, Rect = rect });
    public void PopClip() => commands.Add(new() { Kind = 3 });
    public void BeginLayer(float opacity) => commands.Add(new() { Kind = 4, Color = new(0, 0, 0, Math.Clamp(opacity, 0, 1)) });
    public void EndLayer() => commands.Add(new() { Kind = 5 });
    public void Triangle(System.Numerics.Vector2 a, System.Numerics.Vector2 b, System.Numerics.Vector2 c, Color color) => commands.Add(new() { Kind = 6, Rect = new(a.X, a.Y, b.X, b.Y), Color = color, Color2 = color, Reserved0 = c.X, Reserved1 = c.Y });
    public void Image(Rect rect, NativeImage image, Color? tint = null) { ObjectDisposedException.ThrowIf(image.Id == 0, image); commands.Add(new() { Kind = 7, Flags = image.Id, Rect = rect, Color = tint ?? new Color(1, 1, 1), Color2 = tint ?? new Color(1, 1, 1) }); }
    public void CopyScaledTo(DrawList target, float scale)
    {
        if (ReferenceEquals(this, target) || !float.IsFinite(scale) || scale <= 0) throw new ArgumentException("Require a distinct target and positive finite scale.");
        target.Clear();
        target.Append(this, System.Numerics.Vector2.Zero, scale);
    }
    /// <summary>Append a document's render IR with a uniform viewport transform; preserves scopes and painter order.</summary>
    public void Append(DrawList source, System.Numerics.Vector2 offset, float scale = 1)
    {
        if (ReferenceEquals(this, source) || !float.IsFinite(scale) || scale <= 0 || !float.IsFinite(offset.X) || !float.IsFinite(offset.Y)) throw new ArgumentException("Require a distinct source and finite viewport transform.");
        var target = this;
        foreach (var original in source.Commands)
        {
            var c = original; c.Rect = new(c.Rect.X * scale + offset.X, c.Rect.Y * scale + offset.Y, c.Rect.Width * scale, c.Rect.Height * scale); c.Radius *= scale; c.FontSize *= scale;
            if (c.Kind == 1) target.Text(c.Rect, Encoding.UTF8.GetString(source.TextBytes.Slice((int)c.TextOffset, (int)c.TextLength)), c.Color, c.FontSize, (c.Flags & 1) != 0);
            else { if (c.Kind == 6) { c.Rect = c.Rect with { Width = c.Rect.Width + offset.X, Height = c.Rect.Height + offset.Y }; c.Reserved0 = c.Reserved0 * scale + offset.X; c.Reserved1 = c.Reserved1 * scale + offset.Y; } target.commands.Add(c); }
        }
    }
}

public sealed class NativeImage : IDisposable
{
    internal uint Id { get; private set; }
    private readonly NativeRenderer owner;
    internal NativeImage(NativeRenderer owner, uint id) { this.owner = owner; Id = id; }
    public void Dispose() { if (Id == 0) return; owner.ReleaseImage(Id); Id = 0; }
}

public interface ITextMeasurer { (float Width, float Height) Measure(string text, float size, float width, bool bold = false); }

public sealed unsafe class NativeRenderer : IDisposable, ITextMeasurer
{
    private ulong handle;
    private readonly int ownerThread = Environment.CurrentManagedThreadId;
    public string Adapter { get; }
    public NativeRenderer(uint width, uint height, nint window = 0)
    {
        if (Native.AbiVersion() != 0x00010000 || Native.CommandSize() != Marshal.SizeOf<DrawCommand>()) throw new InvalidOperationException("Incompatible YoUI native ABI.");
        Check(Native.Create(width, height, window, out handle));
        byte[] name = new byte[512];
        fixed (byte* ptr = name) { Check(Native.AdapterName(handle, ptr, 512, out uint length)); Adapter = Encoding.UTF8.GetString(name, 0, (int)length); }
    }
    private void Verify() { ObjectDisposedException.ThrowIf(handle == 0, this); if (ownerThread != Environment.CurrentManagedThreadId) throw new InvalidOperationException("Renderer calls must stay on the creating thread."); }
    public FrameStats Render(DrawList list)
    {
        Verify();
        fixed (DrawCommand* commands = list.Commands)
        fixed (byte* text = list.TextBytes) { Check(Native.Render(handle, commands, (uint)list.Commands.Length, text, (uint)list.TextBytes.Length, out var stats)); return stats; }
    }
    public void Resize(uint width, uint height) { Verify(); Check(Native.Resize(handle, width, height)); }
    public NativeImage UploadImage(uint width, uint height, ReadOnlySpan<byte> rgba)
    {
        Verify(); fixed (byte* data = rgba) { Check(Native.UploadImage(handle, width, height, data, (uint)rgba.Length, out uint image)); return new(this, image); }
    }
    internal void ReleaseImage(uint image) { if (handle == 0) return; Verify(); Check(Native.ReleaseImage(handle, image)); }
    public (float Width, float Height) Measure(string text, float size, float width, bool bold = false)
    {
        Verify(); var bytes = Encoding.UTF8.GetBytes(text);
        fixed (byte* ptr = bytes) { Check(Native.Measure(handle, ptr, (uint)bytes.Length, size, width, bold ? 1u : 0, out float w, out float h)); return (w, h); }
    }
    public void SavePng(string path) { Verify(); var bytes = Encoding.UTF8.GetBytes(Path.GetFullPath(path)); fixed (byte* ptr = bytes) Check(Native.SavePng(handle, ptr, (uint)bytes.Length)); }
    public void Dispose() { if (handle == 0) return; Verify(); Check(Native.Destroy(handle)); handle = 0; GC.SuppressFinalize(this); }
    private static void Check(int status)
    {
        if (status == 0) return;
        uint length = Native.LastError(null, 0); var bytes = new byte[length];
        fixed (byte* ptr = bytes) Native.LastError(ptr, length);
        throw new InvalidOperationException($"YoUI native ({status}): {Encoding.UTF8.GetString(bytes)}");
    }
    private static class Native
    {
        private const string Dll = "youi_render";
        [DllImport(Dll, EntryPoint = "youi_abi_version", CallingConvention = CallingConvention.Cdecl)] internal static extern uint AbiVersion();
        [DllImport(Dll, EntryPoint = "youi_command_size", CallingConvention = CallingConvention.Cdecl)] internal static extern uint CommandSize();
        [DllImport(Dll, EntryPoint = "youi_create", CallingConvention = CallingConvention.Cdecl)] internal static extern int Create(uint w, uint h, nint window, out ulong handle);
        [DllImport(Dll, EntryPoint = "youi_destroy", CallingConvention = CallingConvention.Cdecl)] internal static extern int Destroy(ulong handle);
        [DllImport(Dll, EntryPoint = "youi_resize", CallingConvention = CallingConvention.Cdecl)] internal static extern int Resize(ulong handle, uint w, uint h);
        [DllImport(Dll, EntryPoint = "youi_upload_image", CallingConvention = CallingConvention.Cdecl)] internal static extern int UploadImage(ulong handle, uint w, uint h, byte* data, uint length, out uint image);
        [DllImport(Dll, EntryPoint = "youi_release_image", CallingConvention = CallingConvention.Cdecl)] internal static extern int ReleaseImage(ulong handle, uint image);
        [DllImport(Dll, EntryPoint = "youi_render", CallingConvention = CallingConvention.Cdecl)] internal static extern int Render(ulong handle, DrawCommand* commands, uint count, byte* text, uint length, out FrameStats stats);
        [DllImport(Dll, EntryPoint = "youi_measure", CallingConvention = CallingConvention.Cdecl)] internal static extern int Measure(ulong handle, byte* text, uint length, float size, float maxWidth, uint bold, out float width, out float height);
        [DllImport(Dll, EntryPoint = "youi_save_png", CallingConvention = CallingConvention.Cdecl)] internal static extern int SavePng(ulong handle, byte* path, uint length);
        [DllImport(Dll, EntryPoint = "youi_last_error", CallingConvention = CallingConvention.Cdecl)] internal static extern uint LastError(byte* output, uint capacity);
        [DllImport(Dll, EntryPoint = "youi_adapter_name", CallingConvention = CallingConvention.Cdecl)] internal static extern int AdapterName(ulong handle, byte* output, uint capacity, out uint written);
    }
}
